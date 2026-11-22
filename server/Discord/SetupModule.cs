using System.Collections.Concurrent;
using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// /setup : seule commande d'administration. Sur un serveur sans guilde, elle
/// ouvre l'assistant de création en trois étapes (nom, options, récapitulatif) ;
/// sur un serveur déjà inscrit, le panneau de gestion de la guilde. Tout est
/// visible du seul administrateur (messages éphémères).
/// </summary>
[CommandContextType(InteractionContextType.Guild)]
public sealed class SetupModule(GuildDirectory guilds, GuildContext guildContext, AppDbContext db, SiteLinks links)
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

	[SlashCommand("setup", "Créer ta guilde Guildkeep, ou gérer celle de ce serveur")]
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
		await RespondWithModalAsync<SetupModal>("gk_setup_modal");
	}

	// ---- Assistant : étape 1, le nom ----------------------------------------------------

	public sealed class SetupModal : IModal
	{
		public string Title => "Guildkeep · Étape 1 sur 3";

		[InputLabel("Nom exact de la guilde en jeu")]
		[ModalTextInput("guild_name", TextInputStyle.Short, placeholder: "ex. FORMER HOPE", minLength: 2, maxLength: 32)]
		public string Name { get; set; } = "";
	}

	[ModalInteraction("gk_setup_modal", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupNameAsync(SetupModal modal)
	{
		if (guildContext.Guild is not null)
		{
			await RespondAsync("Ce serveur a déjà sa guilde : relance `/setup` pour la gérer.", ephemeral: true);
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

	private static (Embed, MessageComponent) OptionsStep(SetupDraft draft)
	{
		var embed = new EmbedBuilder()
			.WithColor(Gold)
			.WithTitle("Étape 2 sur 3 · Options")
			.WithDescription($"Guilde : **{draft.Name}**\nDeux réglages facultatifs, modifiables plus tard avec `/setup`.")
			.AddField("Pseudos Discord", "Le bot peut renommer chaque membre avec le nom de son personnage principal.")
			.AddField("Annonces de commandes", "Un salon où le bot annonce chaque nouvelle commande de craft. Laisse vide pour ne rien annoncer.")
			.Build();

		var nicknames = new SelectMenuBuilder()
			.WithCustomId("gk_setup_nick")
			.AddOption("Ne pas toucher aux pseudos", "no", isDefault: !draft.SyncNicknames)
			.AddOption("Renommer avec le personnage principal", "yes", isDefault: draft.SyncNicknames);

		var channel = new SelectMenuBuilder()
			.WithCustomId("gk_setup_channel")
			.WithType(ComponentType.ChannelSelect)
			.WithChannelTypes(ChannelType.Text)
			.WithPlaceholder("Salon des annonces (facultatif)")
			.WithMinValues(0)
			.WithMaxValues(1);
		if (draft.OrdersChannelId is { } channelId)
		{
			channel.WithDefaultValues(new SelectMenuDefaultValue(channelId, SelectDefaultValueType.Channel));
		}

		var components = new ComponentBuilder()
			.WithSelectMenu(nicknames, row: 0)
			.WithSelectMenu(channel, row: 1)
			.WithButton("Continuer", "gk_setup_review", ButtonStyle.Primary, row: 2)
			.WithButton("Modifier le nom", "gk_setup_rename", ButtonStyle.Secondary, row: 2)
			.WithButton("Annuler", "gk_setup_cancel", ButtonStyle.Danger, row: 2)
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
	public async Task SetupRenameAsync() => await RespondWithModalAsync<SetupModal>("gk_setup_modal");

	[ComponentInteraction("gk_setup_cancel", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task SetupCancelAsync()
	{
		Drafts.TryRemove(DraftKey, out _);
		await UpdateMessageAsync(new EmbedBuilder().WithDescription("Création annulée. Relance `/setup` quand tu veux.").Build(), null);
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
			.WithTitle("Étape 3 sur 3 · Récapitulatif")
			.AddField("Guilde en jeu", draft.Name, inline: true)
			.AddField("Pseudos", draft.SyncNicknames ? "renommés" : "inchangés", inline: true)
			.AddField("Annonces", draft.OrdersChannelId is { } id ? $"<#{id}>" : "aucune", inline: true)
			.WithFooter("Le nom doit être exactement celui de la guilde en jeu : seuls ses personnages entreront dans l'annuaire.");
		foreach (var warning in PermissionWarnings(draft.SyncNicknames, draft.OrdersChannelId))
		{
			embed.AddField("À vérifier", warning);
		}
		var components = new ComponentBuilder()
			.WithButton("Créer ma guilde", "gk_setup_create", ButtonStyle.Success)
			.WithButton("Retour", "gk_setup_back", ButtonStyle.Secondary)
			.WithButton("Annuler", "gk_setup_cancel", ButtonStyle.Danger)
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
				.WithDescription($"Ce serveur a déjà sa guilde, **{existing.Name}**. Relance `/setup` pour la gérer.").Build(), null);
			return;
		}

		var guild = await guilds.CreateAsync(draft.Name, Context.Guild.Id.ToString(), Context.User.Id.ToString(),
			draft.SyncNicknames, draft.OrdersChannelId?.ToString());
		var embed = new EmbedBuilder()
			.WithColor(Gold)
			.WithTitle($"{guild.Name} est prête")
			.WithDescription(
				$"**Partage ce lien à tes joueurs** : chacun y télécharge le pack de la guilde (addon + app compagnon), déjà réglé.\n{links.Join(guild)}")
			.AddField("Sur Discord", "Tes membres peuvent déjà utiliser `/link`, `/metier` et `/craft`.")
			.AddField("Plus tard", "`/setup` ouvre la gestion de la guilde : options, nouveau lien, suppression.")
			.WithFooter("Ce lien donne accès à l'annuaire de la guilde : ne le publie pas hors de ton serveur.")
			.Build();
		var components = new ComponentBuilder()
			.WithButton("Annoncer dans ce salon", "gk_setup_announce", ButtonStyle.Primary)
			.WithButton("Ouvrir la page", url: links.Join(guild), style: ButtonStyle.Link)
			.Build();
		await UpdateMessageAsync(embed, components);
	}

	[ComponentInteraction("gk_setup_announce", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild)]
	public async Task AnnounceAsync()
	{
		if (guildContext.Guild is not { } guild)
		{
			await RespondAsync(RequireRegisteredGuildAttribute.NotRegistered, ephemeral: true);
			return;
		}
		try
		{
			await Context.Channel.SendMessageAsync(embed: new EmbedBuilder()
				.WithColor(Gold)
				.WithTitle($"{guild.Name} utilise Guildkeep")
				.WithDescription("L'annuaire des métiers et des commandes de craft de la guilde, en jeu et ici sur Discord.\n" +
					$"**Installe-le** depuis cette page, puis lance le programme : {links.Join(guild)}")
				.Build());
			await RespondAsync("Annonce publiée.", ephemeral: true);
		}
		catch (Exception)
		{
			await RespondAsync("Je ne peux pas écrire dans ce salon : vérifie mes permissions, ou copie le lien toi-même.", ephemeral: true);
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
			.WithDescription(notice ?? "Gestion de la guilde de ce serveur.")
			.AddField("Lien d'installation pour les joueurs", links.Join(guild))
			.AddField("Annuaire", $"{characters} personnage(s), {members} membre(s) Discord", inline: true)
			.AddField("Pseudos", guild.SyncNicknames ? "renommés" : "inchangés", inline: true)
			.AddField("Annonces", ulong.TryParse(guild.OrdersChannelId, out var id) ? $"<#{id}>" : "aucune", inline: true);
		foreach (var warning in PermissionWarnings(guild.SyncNicknames, ulong.TryParse(guild.OrdersChannelId, out var c) ? c : null))
		{
			embed.AddField("À vérifier", warning);
		}

		var channel = new SelectMenuBuilder()
			.WithCustomId("gk_admin_channel")
			.WithType(ComponentType.ChannelSelect)
			.WithChannelTypes(ChannelType.Text)
			.WithPlaceholder("Salon des annonces de commandes (vide = aucune)")
			.WithMinValues(0)
			.WithMaxValues(1);
		if (ulong.TryParse(guild.OrdersChannelId, out var current))
		{
			channel.WithDefaultValues(new SelectMenuDefaultValue(current, SelectDefaultValueType.Channel));
		}
		var components = new ComponentBuilder()
			.WithSelectMenu(channel, row: 0)
			.WithButton(guild.SyncNicknames ? "Ne plus renommer les pseudos" : "Renommer les pseudos", "gk_admin_nick", ButtonStyle.Secondary, row: 1)
			.WithButton("Nouveau lien d'installation", "gk_admin_regen", ButtonStyle.Secondary, row: 1)
			.WithButton("Supprimer la guilde", "gk_admin_delete", ButtonStyle.Danger, row: 1)
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
			? "Les membres seront renommés avec leur personnage principal (à leur prochain `/link` ou `/main`, et au redémarrage du bot)."
			: "Le bot ne touche plus aux pseudos.");
	}

	[ComponentInteraction("gk_admin_channel", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminChannelAsync(string[] values)
	{
		var guild = guildContext.Required;
		guild.OrdersChannelId = ulong.TryParse(values.FirstOrDefault(), out var id) ? id.ToString() : null;
		await db.SaveChangesAsync();
		await RefreshPanelAsync(guild, guild.OrdersChannelId is null
			? "Les nouvelles commandes ne sont plus annoncées."
			: $"Les nouvelles commandes seront annoncées dans <#{guild.OrdersChannelId}>.");
	}

	[ComponentInteraction("gk_admin_regen", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminRegenerateAsync() => await ConfirmAsync(
		"Nouveau lien d'installation ?",
		"L'ancien lien et les packs déjà installés cesseront de fonctionner : chaque joueur devra retélécharger le pack. " +
		"À faire si le lien a fuité hors de la guilde.",
		"Oui, nouveau lien", "gk_admin_regen_ok");

	[ComponentInteraction("gk_admin_regen_ok", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminRegenerateConfirmedAsync()
	{
		var guild = guildContext.Required;
		await guilds.RegenerateSecretsAsync(guild);
		await RefreshPanelAsync(guild, "Nouveau lien créé. Partage-le à nouveau : l'ancien ne fonctionne plus.");
	}

	[ComponentInteraction("gk_admin_delete", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminDeleteAsync() => await ConfirmAsync(
		$"Supprimer {guildContext.Required.Name} ?",
		"Tout l'annuaire (personnages, métiers, recettes, équipement), les commandes et les liens Discord seront effacés. " +
		"Les packs installés cesseront de fonctionner. C'est définitif.",
		"Oui, tout supprimer", "gk_admin_delete_ok");

	[ComponentInteraction("gk_admin_delete_ok", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminDeleteConfirmedAsync()
	{
		var guild = guildContext.Required;
		var name = guild.Name;
		await guilds.DeleteAsync(guild);
		await UpdateMessageAsync(new EmbedBuilder().WithColor(Danger)
			.WithDescription($"**{name}** et toutes ses données ont été supprimées. `/setup` pour repartir de zéro.").Build(), null);
	}

	[ComponentInteraction("gk_admin_back", ignoreGroupNames: true)]
	[RequireUserPermission(GuildPermission.ManageGuild), RequireRegisteredGuild]
	public async Task AdminBackAsync() => await RefreshPanelAsync(guildContext.Required, "Gestion de la guilde de ce serveur.");

	// ---- Outils ---------------------------------------------------------------------------

	private async Task ConfirmAsync(string title, string text, string confirmLabel, string confirmId)
	{
		var embed = new EmbedBuilder().WithColor(Danger).WithTitle(title).WithDescription(text).Build();
		var components = new ComponentBuilder()
			.WithButton(confirmLabel, confirmId, ButtonStyle.Danger)
			.WithButton("Retour", "gk_admin_back", ButtonStyle.Secondary)
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
			warnings.Add("Le bot n'a pas la permission « Gérer les pseudos » : donne-la-lui, et place son rôle au-dessus de ceux des membres.");
		}
		if (channelId is { } id && Context.Guild.GetTextChannel(id) is { } channel &&
			!self.GetPermissions(channel).SendMessages)
		{
			warnings.Add($"Le bot ne peut pas écrire dans <#{id}> : ajoute-le à ce salon.");
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
		.WithDescription("Cet assistant a expiré (redémarrage du bot). Relance `/setup`.").Build(), null);
}
