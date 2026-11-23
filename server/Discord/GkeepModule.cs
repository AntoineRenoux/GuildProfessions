using System.Text;
using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Application;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Services;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Toutes les commandes des membres, sous /gkeep pour ne jamais entrer en
/// conflit avec d'autres bots. Noms en anglais par défaut, traduits en
/// français par Localization/commands.fr.json ; réponses dans la langue de
/// l'utilisateur, toujours éphémères pour ne pas encombrer les salons.
/// L'administration est à part (/gkeep-setup), réservée au droit « Gérer le serveur ».
/// Ce fichier ne garde que les commandes racines (aide, compte, export et
/// import) ; chaque groupe vit dans le dossier Presentation de son module
/// (Modules/{Module}/Presentation/*Commands.cs).
/// </summary>
[Group("gkeep", "Guildkeep: your guild's events, reserves, attendance, professions and orders")]
[CommandContextType(InteractionContextType.Guild)]
public sealed partial class GkeepModule(RosterService roster, NicknameSync nicknames, GuildContext guildContext,
	BotLocalizer l, IServiceProvider services)
	: GuildkeepGroup
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
				"`/gkeep commande nouvelle` demande à tous les artisans ou à l'un d'eux · `liste` avec filtres (que je peux faire, mes offres…) · `proposer` pour un artisan · `accepter`, `prete`, `envoyee`, `recu`, `annuler` pour avancer · `catalogue` cherche une recette",
				"`/gkeep order new` asks every crafter or one of them · `list` with filters (I can make, my offers…) · `offer` for crafters · `accept`, `ready`, `mailed`, `received`, `cancel` move it along · `catalog` finds a recipe"))
			.AddField(l.T("Événements", "Events"), l.T(
				"`/gkeep annonce liste`, `/gkeep charte lire` et `/gkeep objectif liste` pour suivre la vie de la guilde · `objectif contribuer` pour participer · publier, écrire et créer pour les officiers · `/gkeep evenement rappel` prévient les inscrits\n" +
				"`/gkeep quete liste` les quêtes élites, de donjon et de raid des membres · `qui` a une quête · `miennes` tes quêtes et qui les a aussi\n" +
				"`/gkeep presence resume`, `joueur`, `raids`, `raid` la présence réelle en raid (30, 60, 90 jours) · `/gkeep butin liste` l'historique du butin · `corriger`, `annuler` pour les officiers\n" +
				"`/gkeep reserve poser`, `retirer`, `liste` tes réservations de butin (haute, normale, basse) · `ferme`, `verrouiller` pour les officiers · `/gkeep evenement soutien` et `checklist` pour préparer la soirée\n" +
				"`/gkeep evenement liste` les raids et sorties à venir · `voir` le détail et le roster · `inscription` pour répondre (rôle, spé, personnage) · `creer`, `modifier`, `annuler`, `retablir` et `placer` (confirmé, remplaçant, banc) pour l'organisateur et les officiers",
				"`/gkeep announce list`, `/gkeep handbook show` and `/gkeep goal list` keep up with guild life · `goal contribute` to take part · post, edit and create for officers · `/gkeep event remind` pings signed-up players\n" +
				"`/gkeep quest list` members' elite, dungeon and raid quests · `who` has a quest · `mine` your quests and who else has them\n" +
				"`/gkeep attendance summary`, `player`, `raids`, `raid` real raid attendance (30, 60, 90 days) · `/gkeep loot list` the loot history · `correct`, `void` for officers\n" +
				"`/gkeep reserve set`, `clear`, `list` your loot reserves (high, normal, low) · `hard`, `lock` for officers · `/gkeep event duty` and `checklist` to prepare the night\n" +
				"`/gkeep event list` shows upcoming raids and outings · `show` the details and roster · `signup` to answer (role, spec, character) · `create`, `edit`, `cancel`, `restore` and `place` (confirmed, standby, bench) for the organizer and officers"))
			.AddField(l.T("Profils et accueil", "Profiles and onboarding"), l.T(
				"`/gkeep tableau voir` ton tableau de bord : événements, annonces, commandes, présence et accueil\n" +
				"`/gkeep profil voir` le profil d'un joueur · `modifier` rôles, disponibilités, note · `arrivees` arrivées et départs · `versions` versions de l'addon (officier)\n" +
				"`/gkeep accueil etat` ta liste d'accueil · `fait` / `annuler` une étape · `liste` et `relancer` les nouveaux (officier)",
				"`/gkeep home show` your dashboard: events, announcements, orders, attendance and onboarding\n" +
				"`/gkeep profile show` a player's profile · `edit` roles, availability, note · `arrivals` arrivals and departures · `versions` addon versions (officer)\n" +
				"`/gkeep welcome status` your onboarding checklist · `done` / `undo` a step · `list` and `remind` newcomers (officer)"))
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
		var discordId = DiscordId;
		var result = await roster.LinkCharacterAsync(discordId, Context.User.Username, character.Trim());
		var nicknameNote = "";
		if (result.IsSuccess && await roster.GetMainCharacterNameAsync(discordId) is { } main)
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
		var discordId = DiscordId;
		var result = await roster.SetMainCharacterAsync(discordId, character.Trim());
		var nicknameNote = "";
		if (result.IsSuccess && await roster.GetMainCharacterNameAsync(discordId) is { } main)
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
		var token = await roster.GetOrCreateUploadTokenAsync(DiscordId, Context.User.Username);
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
		// Résolu à la demande : les autres commandes racines ne construisent pas tous les modules.
		var build = await services.GetRequiredService<ExportBuilder>().BuildAsync();
		var export = build.Export;
		var encoded = StringCodec.Encode(export);
		// Un module en panne ne prive pas les autres : la chaîne contient les sections disponibles,
		// celles du module en échec en sont absentes (l'addon les traite comme « pas de données »).
		var warning = build.Complete ? "" : l.T(
			$"\n⚠ Module(s) indisponible(s) : {string.Join(", ", build.FailedModules)}. Leurs données ne sont pas dans ce fichier ; " +
				"le reste est à jour. Refais l'export plus tard pour les récupérer.",
			$"\n⚠ Unavailable module(s): {string.Join(", ", build.FailedModules)}. Their data is not in this file; " +
				"the rest is up to date. Export again later to get it.");
		var characters = export.Characters?.Count ?? 0;
		// En pièce jointe : pas de limite des 2000 caractères d'un message.
		using var stream = new MemoryStream(Encoding.UTF8.GetBytes(encoded));
		await FollowupWithFileAsync(new FileAttachment(stream, "guildkeep-import.txt"), l.T(
			$"Données de la guilde (version {export.Version}, {characters} personnages). " +
				"Ouvre le fichier, copie tout, puis en jeu : `/gkeep` → **Importer**.",
			$"Guild data (version {export.Version}, {characters} characters). " +
				"Open the file, copy everything, then in game: `/gkeep` → **Import**.") + warning, ephemeral: true);
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

		// L'annuaire est le socle d'identité : sans lui, l'import ne peut pas savoir qui l'envoie.
		var memberId = await services.GetRequiredService<IAnnuaire>().EnsureMemberAsync(DiscordId, Context.User.Username);
		// Chaîne collée à la main : rien ne garantit son contenu, le rang d'officier déclaré n'est pas retenu.
		var outcome = await services.GetRequiredService<UploadIngestion>().IngestAsync(memberId, payload, UploadSource.DiscordImport);
		var result = outcome.Result;
		var summary = new StringBuilder(l.T(
			$"Import appliqué : {payload.Characters?.Count ?? 0} personnage(s), {payload.Orders?.Count ?? 0} commande(s).",
			$"Import applied: {payload.Characters?.Count ?? 0} character(s), {payload.Orders?.Count ?? 0} order(s)."));
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

	internal static string Clip(string text, int max) => Text.Ellipsize(text, max);
}
