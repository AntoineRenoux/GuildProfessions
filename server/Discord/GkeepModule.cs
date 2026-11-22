using System.Text;
using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Toutes les commandes des membres, sous /gkeep pour ne jamais entrer en
/// conflit avec d'autres bots. Noms en anglais par défaut, traduits en
/// français par Localization/commands.fr.json ; réponses dans la langue de
/// l'utilisateur, toujours éphémères pour ne pas encombrer les salons.
/// L'administration est à part (/gkeep-setup), réservée au droit « Gérer le serveur ».
/// </summary>
[Group("gkeep", "Guildkeep: your guild's professions, crafters and orders")]
[CommandContextType(InteractionContextType.Guild)]
public sealed class GkeepModule(RosterService roster, NicknameSync nicknames, GuildContext guildContext,
	BotLocalizer l, ExportBuilder exportBuilder, UploadIngestion ingestion)
	: InteractionModuleBase<SocketInteractionContext>
{
	internal static readonly Color Green = new(0x3F, 0xB9, 0x7A);
	internal static readonly Color Amber = new(0xE0, 0x9A, 0x2B);

	// ---- Aide ------------------------------------------------------------------------------

	[SlashCommand("help", "What Guildkeep does and how to use it")]
	public async Task HelpAsync()
	{
		var embed = new EmbedBuilder()
			.WithColor(Green)
			.WithTitle(l.T("Guildkeep · aide", "Guildkeep · help"))
			.WithDescription(l.T(
				"L'annuaire des artisans de la guilde et ses commandes de craft, en jeu (addon) et ici.",
				"Your guild's crafter directory and craft orders, in game (addon) and here."));
		if (guildContext.Guild is null)
		{
			embed.AddField(l.T("Pas encore installé", "Not set up yet"), l.T(
				"Un administrateur du serveur doit lancer `/gkeep-setup` : deux minutes, un seul champ obligatoire.",
				"A server admin needs to run `/gkeep-setup`: two minutes, one required field."));
		}
		embed.AddField(l.T("Ton compte", "Your account"), l.T(
				"`/gkeep lier` relie un personnage à ton compte Discord · `/gkeep principal` choisit ton personnage principal",
				"`/gkeep link` ties a character to your Discord account · `/gkeep main` picks your main character"))
			.AddField(l.T("Trouver un artisan", "Find a crafter"), l.T(
				"`/gkeep metier qui` cherche par métier, niveau ou recette · `/gkeep metier recettes` liste les recettes d'un personnage · `/gkeep metier liste` montre tout l'annuaire",
				"`/gkeep profession who` searches by profession, level or recipe · `/gkeep profession recipes` lists a character's recipes · `/gkeep profession list` shows the whole directory"))
			.AddField(l.T("Commandes de craft", "Craft orders"), l.T(
				"`/gkeep commande nouvelle` commande à un artisan · `/gkeep commande liste` les commandes en cours · `accepter`, `terminer`, `annuler` pour les faire avancer",
				"`/gkeep order new` orders from a crafter · `/gkeep order list` shows open orders · `accept`, `done`, `cancel` move them along"))
			.AddField(l.T("En jeu", "In game"), l.T(
				"Installe le pack de la guilde depuis le lien donné par un administrateur : l'addon s'ouvre avec `/gkeep` ou par l'onglet dans la fenêtre de guilde.",
				"Install your guild's pack from the link an admin shares: open the addon with `/gkeep` or from the tab in the guild window."));
		await RespondAsync(embed: embed.Build(), ephemeral: true);
	}

	// ---- Compte -----------------------------------------------------------------------------

	[SlashCommand("link", "Link one of your characters to your Discord account")]
	[RequireRegisteredGuild]
	public async Task LinkAsync([Summary("character", "Character name in game")] string character)
	{
		await DeferAsync(ephemeral: true);
		var discordId = Context.User.Id.ToString();
		var result = await roster.LinkCharacterAsync(discordId, Context.User.Username, character.Trim());
		var nicknameNote = "";
		if (result.Success && await roster.GetMainCharacterNameAsync(discordId) is { } main)
		{
			nicknameNote = NicknameSync.Describe(await nicknames.SyncAsync(guildContext.Required, discordId, main), main, l);
		}
		await FollowupAsync(result.Message + nicknameNote, ephemeral: true);
	}

	[SlashCommand("main", "Pick your main character (used as your nickname if the guild enabled it)")]
	[RequireRegisteredGuild]
	public async Task MainAsync(
		[Summary("character", "One of your linked characters"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))] string character)
	{
		await DeferAsync(ephemeral: true);
		var discordId = Context.User.Id.ToString();
		var result = await roster.SetMainCharacterAsync(discordId, character.Trim());
		var nicknameNote = "";
		if (result.Success && await roster.GetMainCharacterNameAsync(discordId) is { } main)
		{
			nicknameNote = NicknameSync.Describe(await nicknames.SyncAsync(guildContext.Required, discordId, main), main, l) +
				(guildContext.Required.SyncNicknames
					? l.T("\n_Ce pseudo est réappliqué automatiquement : pour le changer, utilise `/gkeep principal` plutôt que les réglages Discord._",
						"\n_This nickname is reapplied automatically: to change it, use `/gkeep main` rather than Discord settings._")
					: "");
		}
		await FollowupAsync(result.Message + nicknameNote, ephemeral: true);
	}

	[SlashCommand("token", "Your personal token for the companion app (optional)")]
	[RequireRegisteredGuild]
	public async Task TokenAsync()
	{
		await DeferAsync(ephemeral: true);
		var token = await roster.GetOrCreateUploadTokenAsync(Context.User.Id.ToString(), Context.User.Username);
		await FollowupAsync(l.T(
			"Ton token personnel pour l'app compagnon (facultatif : le pack de la guilde fonctionne sans). " +
				$"À coller dans son `config.json` sous `uploadToken`, ne le partage pas :\n```\n{token}\n```",
			"Your personal token for the companion app (optional: the guild pack works without it). " +
				$"Paste it into its `config.json` as `uploadToken`, and keep it private:\n```\n{token}\n```"), ephemeral: true);
	}

	// ---- Canal manuel, sans app compagnon -----------------------------------------------

	[SlashCommand("export", "Get the guild data string to paste in game (/gkeep, Import)")]
	[RequireRegisteredGuild]
	public async Task ExportAsync()
	{
		await DeferAsync(ephemeral: true);
		var export = await exportBuilder.BuildAsync();
		var encoded = StringCodec.Encode(export);
		// En pièce jointe : pas de limite des 2000 caractères d'un message.
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(encoded));
		await FollowupWithFileAsync(new FileAttachment(stream, "guildkeep-import.txt"), l.T(
			$"Données de la guilde (version {export.Version}, {export.Characters.Count} personnages). " +
				"Ouvre le fichier, copie tout, puis en jeu : `/gkeep` → **Importer**.",
			$"Guild data (version {export.Version}, {export.Characters.Count} characters). " +
				"Open the file, copy everything, then in game: `/gkeep` → **Import**."), ephemeral: true);
	}

	[SlashCommand("import", "Send the string exported in game (/gkeep, Export)")]
	[RequireRegisteredGuild]
	public async Task ImportAsync() =>
		await RespondWithModalAsync<ImportModal>("gk_import_modal", modifyModal: modal =>
			modal.WithTitle(l.T("Importer depuis le jeu", "Import from the game")));

	[ModalInteraction("gk_import_modal", ignoreGroupNames: true)]
	[RequireRegisteredGuild]
	public async Task HandleImportAsync(ImportModal modal)
	{
		await DeferAsync(ephemeral: true);
		UploadPayload? payload;
		try
		{
			payload = StringCodec.Decode<UploadPayload>(modal.Payload);
		}
		catch (Exception exception)
		{
			await FollowupAsync(l.T($"Import impossible : {exception.Message}", $"Import failed: {exception.Message}"), ephemeral: true);
			return;
		}
		if (payload is null)
		{
			await FollowupAsync(l.T("Import impossible : la chaîne est vide.", "Import failed: the string is empty."), ephemeral: true);
			return;
		}

		var member = await roster.GetOrCreateMemberAsync(Context.User.Id.ToString(), Context.User.Username);
		var result = await ingestion.IngestAsync(member, payload);
		var summary = new StringBuilder(l.T(
			$"Import appliqué : {payload.Characters.Count} personnage(s), {payload.Orders?.Count ?? 0} commande(s).",
			$"Import applied: {payload.Characters.Count} character(s), {payload.Orders?.Count ?? 0} order(s)."));
		foreach (var warning in result.Warnings)
		{
			summary.Append("\n⚠ ").Append(warning);
		}
		await FollowupAsync(summary.ToString(), ephemeral: true);
	}

	public sealed class ImportModal : IModal
	{
		public string Title => "Guildkeep";

		[InputLabel("Exported string / Chaîne exportée")]
		[ModalTextInput("payload", TextInputStyle.Paragraph, placeholder: "GP1!...", maxLength: 4000)]
		public string Payload { get; set; } = "";
	}

	// ---- Métiers ------------------------------------------------------------------------------

	[Group("profession", "Who can craft what in the guild")]
	[RequireRegisteredGuild]
	public sealed class ProfessionGroup(RosterService roster, AppDbContext db, BotLocalizer l)
		: InteractionModuleBase<SocketInteractionContext>
	{
		[SlashCommand("who", "Who can craft it? Search by profession, level or recipe")]
		public async Task WhoAsync(
			[Summary("recipe", "Text to look for in known recipes")] string? recipe = null,
			[Summary("profession", "Profession"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? profession = null,
			[Summary("min_level", "Minimum skill level"), MinValue(1), MaxValue(300)] int? minLevel = null)
		{
			await DeferAsync(ephemeral: true);
			var query = db.Professions.AsNoTracking().Include(p => p.Character).Include(p => p.Recipes).AsQueryable();
			if (profession is not null)
			{
				query = query.Where(p => p.Name == profession);
			}
			if (minLevel is not null)
			{
				query = query.Where(p => p.SkillLevel >= minLevel);
			}
			var search = recipe?.Trim();
			if (!string.IsNullOrEmpty(search))
			{
				var pattern = $"%{search}%";
				query = query.Where(p => p.Recipes.Any(r => r.Name != null && EF.Functions.Like(r.Name, pattern)));
			}
			var professions = await query.OrderByDescending(p => p.SkillLevel).Take(20).ToListAsync();
			if (professions.Count == 0)
			{
				await FollowupAsync(l.T(
					"Personne ne correspond. Essaie un mot plus court, ou retire le métier ou le niveau.",
					"Nobody matches. Try a shorter word, or drop the profession or level."), ephemeral: true);
				return;
			}

			var embed = new EmbedBuilder().WithColor(Green).WithTitle(l.T("Qui peut le fabriquer ?", "Who can craft it?"));
			foreach (var p in professions)
			{
				var matched = string.IsNullOrEmpty(search) ? "" : "\n" + string.Join("\n", p.Recipes
					.Where(r => r.Name is not null && r.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
					.Take(5)
					.Select(r => $"• {r.Name}"));
				embed.AddField($"{p.Character.Name} · {p.Name} {p.SkillLevel}/{p.MaxSkill}",
					(string.IsNullOrEmpty(p.Note) ? l.T("_pas de note_", "_no note_") : $"_{p.Note}_") + matched);
			}
			embed.WithFooter(l.T("Pour commander : /gkeep commande nouvelle", "To order: /gkeep order new"));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("recipes", "Recipes known by a character")]
		public async Task RecipesAsync(
			[Summary("character", "Character"), Autocomplete(typeof(CharacterAutocompleteHandler))] string character,
			[Summary("profession", "Only this profession"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? profession = null,
			[Summary("search", "Filter recipes by text")] string? search = null)
		{
			await DeferAsync(ephemeral: true);
			var name = character.Trim().ToLower();
			var found = await db.Characters.AsNoTracking()
				.Include(c => c.Professions).ThenInclude(p => p.Recipes)
				.FirstOrDefaultAsync(c => c.Name.ToLower() == name);
			if (found is null)
			{
				await FollowupAsync(l.T($"Personnage **{character}** inconnu de l'annuaire.", $"No character named **{character}** in the directory."), ephemeral: true);
				return;
			}

			var filter = search?.Trim();
			var professions = found.Professions
				.Where(p => profession is null || p.Name == profession)
				.OrderBy(p => p.Name)
				.Select(p => (Profession: p.Name, Recipes: (IReadOnlyList<string>)p.Recipes
					.Select(r => r.Name ?? l.T($"Recette #{r.SpellId}", $"Recipe #{r.SpellId}"))
					.Where(n => string.IsNullOrEmpty(filter) || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
					.ToList()))
				.Where(p => p.Recipes.Count > 0)
				.ToList();
			if (professions.Count == 0)
			{
				await FollowupAsync(!string.IsNullOrEmpty(filter)
					? l.T($"**{found.Name}** : aucune recette ne contient « {filter} ».", $"**{found.Name}**: no recipe contains \"{filter}\".")
					: l.T($"**{found.Name}** : aucune recette relevée. Il faut ouvrir la fenêtre du métier en jeu, avec l'addon installé.",
						$"**{found.Name}**: no recipes scanned yet. Open the profession window in game with the addon installed."), ephemeral: true);
				return;
			}

			var layout = RecipeListFormatter.Layout(professions, l.T("suite", "continued"));
			var total = professions.Sum(p => p.Recipes.Count);
			var embed = new EmbedBuilder()
				.WithColor(Green)
				.WithTitle(l.T($"Recettes de {found.Name}", $"{found.Name}'s recipes"))
				.WithDescription(l.T($"{total} recette(s)", $"{total} recipe(s)") + (string.IsNullOrEmpty(filter) ? "" : $" · « {filter} »"));
			foreach (var field in layout.Fields)
			{
				embed.AddField(field.Title, field.Body);
			}
			embed.WithFooter(layout.HiddenCount > 0
				? l.T($"… et {layout.HiddenCount} autres : affine avec metier ou recherche", $"… and {layout.HiddenCount} more: narrow with profession or search")
				: l.T("Relevé en jeu par l'addon", "Scanned in game by the addon"));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("list", "The guild's whole profession directory")]
		public async Task ListAsync(
			[Summary("profession", "Only this profession"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? profession = null)
		{
			await DeferAsync(ephemeral: true);
			var query = db.Professions.AsNoTracking().Include(p => p.Character).AsQueryable();
			if (profession is not null)
			{
				query = query.Where(p => p.Name == profession);
			}
			var professions = await query.OrderBy(p => p.Name).ThenByDescending(p => p.SkillLevel).ToListAsync();
			var recipeCounts = await db.Recipes.AsNoTracking()
				.GroupBy(r => r.ProfessionId)
				.Select(g => new { ProfessionId = g.Key, Count = g.Count() })
				.ToDictionaryAsync(x => x.ProfessionId, x => x.Count);
			if (professions.Count == 0)
			{
				await FollowupAsync(l.T(
					"L'annuaire est vide. Installe le pack de la guilde et ouvre tes métiers en jeu, ou déclare-les avec `/gkeep metier definir`.",
					"The directory is empty. Install the guild pack and open your professions in game, or declare them with `/gkeep profession set`."), ephemeral: true);
				return;
			}

			var embed = new EmbedBuilder().WithColor(Green)
				.WithTitle(profession is null ? l.T("Métiers de la guilde", "Guild professions") : profession);
			foreach (var group in professions.GroupBy(p => p.Name).Take(25))
			{
				var lines = group.Take(15).Select(p =>
					$"**{p.Character.Name}** {p.SkillLevel}/{p.MaxSkill}" +
					(p.Source == DataSource.Addon ? " ✓" : "") +
					(recipeCounts.TryGetValue(p.Id, out var count) ? l.T($" · {count} recette(s)", $" · {count} recipe(s)") : "") +
					(string.IsNullOrEmpty(p.Note) ? "" : $" · _{p.Note}_"));
				var more = group.Count() > 15 ? l.T($"\n… et {group.Count() - 15} autres", $"\n… and {group.Count() - 15} more") : "";
				embed.AddField(group.Key, string.Join("\n", lines) + more);
			}
			embed.WithFooter(l.T("✓ = relevé en jeu par l'addon", "✓ = scanned in game by the addon"));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("set", "Declare or update one of your character's professions")]
		public async Task SetAsync(
			[Summary("character", "Character name in game")] string character,
			[Summary("profession", "Profession"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string profession,
			[Summary("level", "Current skill level"), MinValue(1), MaxValue(300)] int level,
			[Summary("note", "Free note (availability, conditions...)")] string? note = null)
		{
			await DeferAsync(ephemeral: true);
			if (!await ProfessionChoices.IsKnownAsync(profession, db))
			{
				await FollowupAsync(l.T($"Métier inconnu : **{profession}**. Choisis-le dans la liste proposée.",
					$"Unknown profession: **{profession}**. Pick one from the suggestions."), ephemeral: true);
				return;
			}
			var result = await roster.SetProfessionFromDiscordAsync(
				Context.User.Id.ToString(), Context.User.Username, character.Trim(), profession, level, 300, note);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("remove", "Remove a profession from one of your characters")]
		public async Task RemoveAsync(
			[Summary("character", "Character name in game"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))] string character,
			[Summary("profession", "Profession to remove"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string profession)
		{
			await DeferAsync(ephemeral: true);
			var result = await roster.RemoveProfessionAsync(Context.User.Id.ToString(), character.Trim(), profession);
			await FollowupAsync(result.Message, ephemeral: true);
		}
	}

	// ---- Commandes de craft -----------------------------------------------------------------

	[Group("order", "Craft orders between members")]
	[RequireRegisteredGuild]
	public sealed class OrderGroup(CraftOrderService orders, AppDbContext db, BotLocalizer l)
		: InteractionModuleBase<SocketInteractionContext>
	{
		[SlashCommand("new", "Order a craft from a crafter")]
		public async Task NewAsync(
			[Summary("crafter", "Crafter's character"), Autocomplete(typeof(CharacterAutocompleteHandler))] string crafter,
			[Summary("item", "Item or recipe you want")] string item,
			[Summary("quantity", "Quantity"), MinValue(1), MaxValue(200)] int quantity = 1,
			[Summary("note", "Details (materials provided, deadline...)")] string? note = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await orders.CreateFromDiscordAsync(
				Context.User.Id.ToString(), Context.User.Username, crafter.Trim(), item.Trim(), quantity, note);
			await FollowupAsync(result.Message + (result.Success
				? l.T("\nL'artisan est prévenu en message privé.", "\nThe crafter was notified by direct message.")
				: ""), ephemeral: true);
		}

		[SlashCommand("list", "Open craft orders")]
		public async Task ListAsync(
			[Summary("filter", "Only the orders that concern me")]
			[Choice("All / Toutes", "all"), Choice("Mine / Les miennes", "mine"), Choice("To craft for me / À fabriquer", "for-me")]
			string filter = "all")
		{
			await DeferAsync(ephemeral: true);
			var discordId = Context.User.Id.ToString();
			var memberCharacters = await db.Characters
				.Where(c => c.Member!.DiscordId == discordId)
				.Select(c => c.Name)
				.ToListAsync();
			var query = db.CraftOrders.AsNoTracking()
				.Where(o => o.Status == CraftOrderStatus.Open || o.Status == CraftOrderStatus.Accepted);
			query = filter switch
			{
				"mine" => query.Where(o => memberCharacters.Contains(o.RequesterCharacter) || o.RequesterCharacter == Context.User.Username),
				"for-me" => query.Where(o => memberCharacters.Contains(o.CrafterCharacter)),
				_ => query,
			};
			var list = await query.OrderBy(o => o.Id).Take(25).ToListAsync();
			if (list.Count == 0)
			{
				await FollowupAsync(l.T("Aucune commande en cours.", "No open orders."), ephemeral: true);
				return;
			}

			var embed = new EmbedBuilder().WithColor(Amber).WithTitle(l.T("Commandes en cours", "Open orders"));
			foreach (var order in list)
			{
				embed.AddField(
					$"#{order.Id} · {order.Quantity}× {order.Item} ({CraftOrderService.Label(order.Status, l)})",
					l.T($"de **{order.RequesterCharacter}** pour **{order.CrafterCharacter}**", $"from **{order.RequesterCharacter}** to **{order.CrafterCharacter}**") +
					(string.IsNullOrEmpty(order.Note) ? "" : $"\n_{order.Note}_"));
			}
			embed.WithFooter(l.T("Faire avancer : /gkeep commande accepter, terminer ou annuler", "Move along: /gkeep order accept, done or cancel"));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("accept", "Accept an order (crafter)")]
		public Task AcceptAsync([Summary("id", "The order"), Autocomplete(typeof(OrderAutocompleteHandler))] int id) =>
			UpdateAsync(id, CraftOrderStatus.Accepted);

		[SlashCommand("done", "Mark an order as done (crafter)")]
		public Task DoneAsync([Summary("id", "The order"), Autocomplete(typeof(OrderAutocompleteHandler))] int id) =>
			UpdateAsync(id, CraftOrderStatus.Done);

		[SlashCommand("cancel", "Cancel an order (crafter or requester)")]
		public Task CancelAsync([Summary("id", "The order"), Autocomplete(typeof(OrderAutocompleteHandler))] int id) =>
			UpdateAsync(id, CraftOrderStatus.Cancelled);

		private async Task UpdateAsync(int id, CraftOrderStatus status)
		{
			await DeferAsync(ephemeral: true);
			var result = await orders.UpdateStatusFromDiscordAsync(Context.User.Id.ToString(), id, status);
			await FollowupAsync(result.Message, ephemeral: true);
		}
	}
}
