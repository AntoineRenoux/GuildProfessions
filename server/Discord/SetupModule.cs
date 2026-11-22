using System.Collections.Concurrent;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// /gkeep-setup : seule commande d'administration, masquée aux membres sans le
/// droit « Gérer le serveur ». Sur un serveur sans guilde, elle ouvre
/// l'assistant de création en trois étapes (nom, options, récapitulatif) ;
/// sur un serveur déjà inscrit, le panneau de gestion de la guilde. Tout est
/// visible du seul administrateur (messages éphémères), en français ou en
/// anglais selon sa langue Discord.
/// </summary>
[CommandContextType(InteractionContextType.Guild)]
public sealed class SetupModule(GuildDirectory guilds, GuildContext guildContext, AppDbContext db, SiteLinks links, BotLocalizer l)
	: InteractionModuleBase<SocketInteractionContext>
{
	private static readonly Color Gold = new(0xC9, 0xA2, 0x27);
	private static readonly Color Danger = new(0xD0, 0x4A, 0x4A);

	/// <summary>Brouillons d'assistant en cours, par (serveur, administrateur). Perdus au redémarrage : l'assistant se relance.</summary>
	private static readonly ConcurrentDictionary<string, SetupDraft> Drafts = new();

	private sealed class SetupDraft
	{
		public required string Name { get; set; }
		public bool SyncNicknames { get; set; }
		public ulong? OrdersChannelId { get; set; }
	}

	private string DraftKey => $"{Context.Guild.Id}:{Context.User.Id}";

	[SlashCommand("gkeep-setup", "Create this server's Guildkeep guild, or manage it")]
	[DefaultMemberPermissions(GuildPermission.ManageGuild)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupAsync()
	{
		if (guildContext.Guild is { } guild)
		{
			var (embed, components) = await AdminPanelAsync(guild);
			await RespondAsync(embed: embed, components: components, ephemeral: true);
			return;
		}
		await OpenNameStepAsync();
	}

	private Task OpenNameStepAsync() =>
		RespondWithModalAsync<SetupModal>("gk_setup_modal", modifyModal: modal => TranslateNameStep(modal, l));

	/// <summary>
	/// Traduit la fenêtre de l'étape 1. Depuis Discord.Net 3.18, [InputLabel] enveloppe le champ
	/// dans un composant Label : le libellé se met sur ce Label, jamais sur le champ lui-même,
	/// sinon Discord refuse la fenêtre (erreur 50035, TEXT_INPUT_COMPONENT_LABEL_IN_LABEL_COMPONENT).
	/// </summary>
	public static void TranslateNameStep(ModalBuilder modal, BotLocalizer l)
	{
		modal.WithTitle(l.T("Guildkeep · Étape 1 sur 3", "Guildkeep · Step 1 of 3"));
		var label = modal.Components.OfType<LabelBuilder>()
			.FirstOrDefault(c => c.Component is TextInputBuilder { CustomId: "guild_name" });
		if (label is not null)
			label.Label = l.T("Nom exact de la guilde en jeu", "Exact in-game guild name");
		modal.UpdateTextInput("guild_name", input =>
		{
			input.Label = null;
			input.Placeholder = l.T("ex. FORMER HOPE", "e.g. FORMER HOPE");
		});
	}

	// ---- Assistant : étape 1, le nom ----------------------------------------------------

	public sealed class SetupModal : IModal
	{
		// Titre, libellé et exemple traduits à l'ouverture (OpenNameStepAsync).
		public string Title => "Guildkeep";

		[InputLabel("Guild name")]
		[ModalTextInput("guild_name", TextInputStyle.Short, placeholder: "FORMER HOPE", minLength: 2, maxLength: 32)]
		public string Name { get; set; } = "";
	}

	[ModalInteraction("gk_setup_modal", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupNameAsync(SetupModal modal)
	{
		if (guildContext.Guild is not null)
		{
			await RespondAsync(l.T("Ce serveur a déjà sa guilde : relance `/gkeep-setup` pour la gérer.",
				"This server already has its guild: run `/gkeep-setup` again to manage it."), ephemeral: true);
			return;
		}
		var name = modal.Name.Trim();
		var draft = Drafts.AddOrUpdate(DraftKey,
			_ => new SetupDraft { Name = name },
			(_, existing) => { existing.Name = name; return existing; });
		var (embed, components) = OptionsStep(draft);
		await RespondAsync(embed: embed, components: components, ephemeral: true);
	}

	// ---- Assistant : étape 2, les options -----------------------------------------------

	private (Embed, MessageComponent) OptionsStep(SetupDraft draft)
	{
		var embed = new EmbedBuilder()
			.WithColor(Gold)
			.WithTitle(l.T("Étape 2 sur 3 · Options", "Step 2 of 3 · Options"))
			.WithDescription(l.T($"Guilde : **{draft.Name}**\nDeux réglages facultatifs, modifiables plus tard avec `/gkeep-setup`.",
				$"Guild: **{draft.Name}**\nTwo optional settings, which you can change later with `/gkeep-setup`."))
			.AddField(l.T("Pseudos Discord", "Discord nicknames"),
				l.T("Le bot peut renommer chaque membre avec le nom de son personnage principal.",
					"The bot can rename each member after their main character."))
			.AddField(l.T("Annonces de commandes", "Order announcements"),
				l.T("Un salon où le bot annonce chaque nouvelle commande de craft. Laisse vide pour ne rien annoncer.",
					"A channel where the bot announces every new craft order. Leave empty to announce nothing."))
			.Build();

		var nicknames = new SelectMenuBuilder()
			.WithCustomId("gk_setup_nick")
			.AddOption(l.T("Ne pas toucher aux pseudos", "Leave nicknames alone"), "no", isDefault: !draft.SyncNicknames)
			.AddOption(l.T("Renommer avec le personnage principal", "Rename after the main character"), "yes", isDefault: draft.SyncNicknames);

		var channel = new SelectMenuBuilder()
			.WithCustomId("gk_setup_channel")
			.WithType(ComponentType.ChannelSelect)
			.WithChannelTypes(ChannelType.Text)
			.WithPlaceholder(l.T("Salon des annonces (facultatif)", "Announcement channel (optional)"))
			.WithMinValues(0)
			.WithMaxValues(1);
		if (draft.OrdersChannelId is { } channelId)
		{
			channel.WithDefaultValues(new SelectMenuDefaultValue(channelId, SelectDefaultValueType.Channel));
		}

		var components = new ComponentBuilder()
			.WithSelectMenu(nicknames, row: 0)
			.WithSelectMenu(channel, row: 1)
			.WithButton(l.T("Continuer", "Continue"), "gk_setup_review", ButtonStyle.Primary, row: 2)
			.WithButton(l.T("Modifier le nom", "Change the name"), "gk_setup_rename", ButtonStyle.Secondary, row: 2)
			.WithButton(l.T("Annuler", "Cancel"), "gk_setup_cancel", ButtonStyle.Danger, row: 2)
			.Build();
		return (embed, components);
	}

	[ComponentInteraction("gk_setup_nick", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupNicknamesAsync(string[] values)
	{
		if (Drafts.TryGetValue(DraftKey, out var draft))
		{
			draft.SyncNicknames = values.FirstOrDefault() == "yes";
		}
		await DeferAsync();
	}

	[ComponentInteraction("gk_setup_channel", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupChannelAsync(string[] values)
	{
		if (Drafts.TryGetValue(DraftKey, out var draft))
		{
			draft.OrdersChannelId = ulong.TryParse(values.FirstOrDefault(), out var id) ? id : null;
		}
		await DeferAsync();
	}

	[ComponentInteraction("gk_setup_rename", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupRenameAsync() => await OpenNameStepAsync();

	[ComponentInteraction("gk_setup_cancel", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupCancelAsync()
	{
		Drafts.TryRemove(DraftKey, out _);
		await UpdateMessageAsync(new EmbedBuilder().WithDescription(l.T("Création annulée. Relance `/gkeep-setup` quand tu veux.",
			"Setup cancelled. Run `/gkeep-setup` again whenever you like.")).Build(), null);
	}

	[ComponentInteraction("gk_setup_back", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupBackAsync()
	{
		if (!Drafts.TryGetValue(DraftKey, out var draft))
		{
			await ExpiredAsync();
			return;
		}
		var (embed, components) = OptionsStep(draft);
		await UpdateMessageAsync(embed, components);
	}

	// ---- Assistant : étape 3, récapitulatif et création ---------------------------------

	[ComponentInteraction("gk_setup_review", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupReviewAsync()
	{
		if (!Drafts.TryGetValue(DraftKey, out var draft))
		{
			await ExpiredAsync();
			return;
		}
		var embed = new EmbedBuilder()
			.WithColor(Gold)
			.WithTitle(l.T("Étape 3 sur 3 · Récapitulatif", "Step 3 of 3 · Summary"))
			.AddField(l.T("Guilde en jeu", "In-game guild"), draft.Name, inline: true)
			.AddField(l.T("Pseudos", "Nicknames"), NicknamesLabel(draft.SyncNicknames), inline: true)
			.AddField(l.T("Annonces", "Announcements"), draft.OrdersChannelId is { } id ? $"<#{id}>" : l.T("aucune", "none"), inline: true)
			.WithFooter(l.T("Le nom doit être exactement celui de la guilde en jeu : seuls ses personnages entreront dans l'annuaire.",
				"The name must match the in-game guild exactly: only its characters will enter the directory."));
		foreach (var warning in PermissionWarnings(draft.SyncNicknames, draft.OrdersChannelId))
		{
			embed.AddField(l.T("À vérifier", "Check this"), warning);
		}
		var components = new ComponentBuilder()
			.WithButton(l.T("Créer ma guilde", "Create my guild"), "gk_setup_create", ButtonStyle.Success)
			.WithButton(l.T("Retour", "Back"), "gk_setup_back", ButtonStyle.Secondary)
			.WithButton(l.T("Annuler", "Cancel"), "gk_setup_cancel", ButtonStyle.Danger)
			.Build();
		await UpdateMessageAsync(embed.Build(), components);
	}

	[ComponentInteraction("gk_setup_create", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupCreateAsync()
	{
		if (!Drafts.TryRemove(DraftKey, out var draft))
		{
			await ExpiredAsync();
			return;
		}
		// Un autre admin a pu créer la guilde entre-temps.
		var existing = await guilds.FindByDiscordServerAsync(Context.Guild.Id);
		if (existing is not null)
		{
			await UpdateMessageAsync(new EmbedBuilder().WithColor(Gold)
				.WithDescription(l.T($"Ce serveur a déjà sa guilde, **{existing.Name}**. Relance `/gkeep-setup` pour la gérer.",
					$"This server already has its guild, **{existing.Name}**. Run `/gkeep-setup` again to manage it.")).Build(), null);
			return;
		}

		var guild = await guilds.CreateAsync(draft.Name, Context.Guild.Id.ToString(), Context.User.Id.ToString(),
			draft.SyncNicknames, draft.OrdersChannelId?.ToString(),
			BotLocalizer.ToLocale(BotLocalizer.FromLocale(Context.Guild.PreferredLocale)));
		var embed = new EmbedBuilder()
			.WithColor(Gold)
			.WithTitle(l.T($"{guild.Name} est prête", $"{guild.Name} is ready"))
			.WithDescription(l.T(
				$"**Partage ce lien à tes joueurs** : chacun y télécharge le pack de la guilde (addon + app compagnon), déjà réglé.\n{links.Join(guild)}",
				$"**Share this link with your players**: each one downloads the guild pack there (addon + companion app), already set up.\n{links.Join(guild)}"))
			.AddField(l.T("Sur Discord", "On Discord"), l.T(
				"Tes membres peuvent déjà utiliser `/gkeep` : `/gkeep aide` leur explique tout.",
				"Your members can already use `/gkeep`: `/gkeep help` explains everything."))
			.AddField(l.T("Plus tard", "Later"), l.T(
				"`/gkeep-setup` ouvre la gestion de la guilde : options, nouveau lien, suppression.",
				"`/gkeep-setup` opens guild management: settings, new link, deletion."))
			.WithFooter(l.T("Ce lien donne accès à l'annuaire de la guilde : ne le publie pas hors de ton serveur.",
				"This link gives access to the guild directory: don't post it outside your server."))
			.Build();
		var components = new ComponentBuilder()
			.WithButton(l.T("Annoncer dans ce salon", "Announce in this channel"), "gk_setup_announce", ButtonStyle.Primary)
			.WithButton(l.T("Ouvrir la page", "Open the page"), url: links.Join(guild), style: ButtonStyle.Link)
			.Build();
		await UpdateMessageAsync(embed, components);
	}

	[ComponentInteraction("gk_setup_announce", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task AnnounceAsync()
	{
		if (guildContext.Guild is not { } guild)
		{
			await RespondAsync(RequireRegisteredGuildAttribute.NotRegistered(l), ephemeral: true);
			return;
		}
		try
		{
			// L'annonce publique suit la langue de la guilde, pas celle de l'admin.
			var g = BotLocalizer.For(BotLocalizer.FromLocale(guild.Locale));
			await Context.Channel.SendMessageAsync(embed: new EmbedBuilder()
				.WithColor(Gold)
				.WithTitle(g.T($"{guild.Name} utilise Guildkeep", $"{guild.Name} uses Guildkeep"))
				.WithDescription(g.T(
					"L'annuaire des métiers et les commandes de craft de la guilde, en jeu et ici sur Discord.\n" +
						$"**Installe-le** depuis cette page, puis lance le programme : {links.Join(guild)}\nSur Discord : `/gkeep aide`.",
					"The guild's profession directory and craft orders, in game and right here on Discord.\n" +
						$"**Install it** from this page, then run the program: {links.Join(guild)}\nOn Discord: `/gkeep help`."))
				.Build());
			await RespondAsync(l.T("Annonce publiée.", "Announcement posted."), ephemeral: true);
		}
		catch (Exception)
		{
			await RespondAsync(l.T("Je ne peux pas écrire dans ce salon : vérifie mes permissions, ou copie le lien toi-même.",
				"I can't post in this channel: check my permissions, or copy the link yourself."), ephemeral: true);
		}
	}

	// ---- Panneau de gestion d'une guilde existante --------------------------------------

	private async Task<(Embed, MessageComponent)> AdminPanelAsync(Guild guild, string? notice = null)
	{
		var characters = await db.Characters.CountAsync();
		var members = await db.Members.CountAsync();
		var embed = new EmbedBuilder()
			.WithColor(Gold)
			.WithTitle($"Guildkeep · {guild.Name}")
			.WithDescription(notice ?? l.T("Gestion de la guilde de ce serveur.", "Manage this server's guild."))
			.AddField(l.T("Lien d'installation pour les joueurs", "Install link for players"), links.Join(guild))
			.AddField(l.T("Annuaire", "Directory"), l.T($"{characters} personnage(s), {members} membre(s) Discord", $"{characters} character(s), {members} Discord member(s)"), inline: true)
			.AddField(l.T("Pseudos", "Nicknames"), NicknamesLabel(guild.SyncNicknames), inline: true)
			.AddField(l.T("Annonces", "Announcements"), ulong.TryParse(guild.OrdersChannelId, out var id) ? $"<#{id}>" : l.T("aucune", "none"), inline: true);
		foreach (var warning in PermissionWarnings(guild.SyncNicknames, ulong.TryParse(guild.OrdersChannelId, out var c) ? c : null))
		{
			embed.AddField(l.T("À vérifier", "Check this"), warning);
		}

		var channel = new SelectMenuBuilder()
			.WithCustomId("gk_admin_channel")
			.WithType(ComponentType.ChannelSelect)
			.WithChannelTypes(ChannelType.Text)
			.WithPlaceholder(l.T("Salon des annonces de commandes (vide = aucune)", "Order announcement channel (empty = none)"))
			.WithMinValues(0)
			.WithMaxValues(1);
		if (ulong.TryParse(guild.OrdersChannelId, out var current))
		{
			channel.WithDefaultValues(new SelectMenuDefaultValue(current, SelectDefaultValueType.Channel));
		}
		var components = new ComponentBuilder()
			.WithSelectMenu(channel, row: 0)
			.WithButton(guild.SyncNicknames ? l.T("Ne plus renommer les pseudos", "Stop renaming nicknames") : l.T("Renommer les pseudos", "Rename nicknames"),
				"gk_admin_nick", ButtonStyle.Secondary, row: 1)
			.WithButton(l.T("Nouveau lien d'installation", "New install link"), "gk_admin_regen", ButtonStyle.Secondary, row: 1)
			.WithButton(l.T("Supprimer la guilde", "Delete the guild"), "gk_admin_delete", ButtonStyle.Danger, row: 1)
			.Build();
		return (embed.Build(), components);
	}

	private async Task RefreshPanelAsync(Guild guild, string notice)
	{
		var (embed, components) = await AdminPanelAsync(guild, notice);
		await UpdateMessageAsync(embed, components);
	}

	[ComponentInteraction("gk_admin_nick", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminNicknamesAsync()
	{
		var guild = guildContext.Required;
		guild.SyncNicknames = !guild.SyncNicknames;
		await db.SaveChangesAsync();
		await RefreshPanelAsync(guild, guild.SyncNicknames
			? l.T("Les membres seront renommés avec leur personnage principal (à leur prochain `/gkeep lier` ou `/gkeep principal`, et au redémarrage du bot).",
				"Members will be renamed after their main character (on their next `/gkeep link` or `/gkeep main`, and when the bot restarts).")
			: l.T("Le bot ne touche plus aux pseudos.", "The bot no longer touches nicknames."));
	}

	[ComponentInteraction("gk_admin_channel", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminChannelAsync(string[] values)
	{
		var guild = guildContext.Required;
		guild.OrdersChannelId = ulong.TryParse(values.FirstOrDefault(), out var id) ? id.ToString() : null;
		await db.SaveChangesAsync();
		await RefreshPanelAsync(guild, guild.OrdersChannelId is null
			? l.T("Les nouvelles commandes ne sont plus annoncées.", "New orders are no longer announced.")
			: l.T($"Les nouvelles commandes seront annoncées dans <#{guild.OrdersChannelId}>.", $"New orders will be announced in <#{guild.OrdersChannelId}>."));
	}

	[ComponentInteraction("gk_admin_regen", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminRegenerateAsync() => await ConfirmAsync(
		l.T("Nouveau lien d'installation ?", "New install link?"),
		l.T("L'ancien lien et les packs déjà installés cesseront de fonctionner : chaque joueur devra retélécharger le pack. " +
				"À faire si le lien a fuité hors de la guilde.",
			"The old link and the packs already installed will stop working: every player will have to download the pack again. " +
				"Do this if the link leaked outside the guild."),
		l.T("Oui, nouveau lien", "Yes, new link"), "gk_admin_regen_ok");

	[ComponentInteraction("gk_admin_regen_ok", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminRegenerateConfirmedAsync()
	{
		var guild = guildContext.Required;
		await guilds.RegenerateSecretsAsync(guild);
		await RefreshPanelAsync(guild, l.T("Nouveau lien créé. Partage-le à nouveau : l'ancien ne fonctionne plus.",
			"New link created. Share it again: the old one no longer works."));
	}

	[ComponentInteraction("gk_admin_delete", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminDeleteAsync() => await ConfirmAsync(
		l.T($"Supprimer {guildContext.Required.Name} ?", $"Delete {guildContext.Required.Name}?"),
		l.T("Tout l'annuaire (personnages, métiers, recettes, équipement), les commandes et les liens Discord seront effacés. " +
				"Les packs installés cesseront de fonctionner. C'est définitif.",
			"The whole directory (characters, professions, recipes, gear), the orders and the Discord links will be erased. " +
				"Installed packs will stop working. This cannot be undone."),
		l.T("Oui, tout supprimer", "Yes, delete everything"), "gk_admin_delete_ok");

	[ComponentInteraction("gk_admin_delete_ok", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminDeleteConfirmedAsync()
	{
		var guild = guildContext.Required;
		var name = guild.Name;
		await guilds.DeleteAsync(guild);
		await UpdateMessageAsync(new EmbedBuilder().WithColor(Danger)
			.WithDescription(l.T($"**{name}** et toutes ses données ont été supprimées. `/gkeep-setup` pour repartir de zéro.",
				$"**{name}** and all its data were deleted. `/gkeep-setup` to start over.")).Build(), null);
	}

	[ComponentInteraction("gk_admin_back", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminBackAsync() => await RefreshPanelAsync(guildContext.Required, l.T("Gestion de la guilde de ce serveur.", "Manage this server's guild."));

	// ---- Outils ---------------------------------------------------------------------------

	private string NicknamesLabel(bool sync) => sync ? l.T("renommés", "renamed") : l.T("inchangés", "unchanged");

	private async Task ConfirmAsync(string title, string text, string confirmLabel, string confirmId)
	{
		var embed = new EmbedBuilder().WithColor(Danger).WithTitle(title).WithDescription(text).Build();
		var components = new ComponentBuilder()
			.WithButton(confirmLabel, confirmId, ButtonStyle.Danger)
			.WithButton(l.T("Retour", "Back"), "gk_admin_back", ButtonStyle.Secondary)
			.Build();
		await UpdateMessageAsync(embed, components);
	}

	// Ce que le bot ne pourra pas faire avec ses permissions actuelles.
	private List<string> PermissionWarnings(bool syncNicknames, ulong? channelId)
	{
		var warnings = new List<string>();
		var self = Context.Guild.CurrentUser;
		if (syncNicknames && !self.GuildPermissions.ManageNicknames)
		{
			warnings.Add(l.T("Le bot n'a pas la permission « Gérer les pseudos » : donne-la-lui, et place son rôle au-dessus de ceux des membres.",
				"The bot lacks the \"Manage Nicknames\" permission: grant it, and place its role above the members' roles."));
		}
		if (channelId is { } id && Context.Guild.GetTextChannel(id) is { } channel &&
			!self.GetPermissions(channel).SendMessages)
		{
			warnings.Add(l.T($"Le bot ne peut pas écrire dans <#{id}> : ajoute-le à ce salon.", $"The bot can't post in <#{id}>: add it to that channel."));
		}
		return warnings;
	}

	private async Task UpdateMessageAsync(Embed embed, MessageComponent? components)
	{
		if (Context.Interaction is SocketMessageComponent component)
		{
			await component.UpdateAsync(message =>
			{
				message.Embed = embed;
				message.Components = components ?? new ComponentBuilder().Build();
			});
		}
		else
		{
			await RespondAsync(embed: embed, components: components, ephemeral: true);
		}
	}

	private Task ExpiredAsync() => UpdateMessageAsync(new EmbedBuilder()
		.WithDescription(l.T("Cet assistant a expiré (redémarrage du bot). Relance `/gkeep-setup`.",
			"This wizard expired (the bot restarted). Run `/gkeep-setup` again.")).Build(), null);
}
