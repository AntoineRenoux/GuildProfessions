using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Application;
using GuildProfessions.Server.Modules.Annuaire.Presentation;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

// Commandes Discord du module Annuaire : /gkeep profession (qui sait faire quoi).
// Le groupe reste imbriqué dans GkeepModule (Discord ne connaît les sous-commandes que
// par imbrication) et garde son namespace : même hiérarchie /gkeep <groupe> <commande>,
// mêmes clés de traduction dans commands.fr.json. Il ne dépend que de son module.
public sealed partial class GkeepModule
{
	// ---- Métiers ------------------------------------------------------------------------------

	[Group("profession", "Who can craft what in the guild")]
	[RequireRegisteredGuild]
	public sealed class ProfessionGroup(RosterService roster, AnnuaireReader reader, BotLocalizer l)
		: GuildkeepGroup
	{
		[SlashCommand("who", "Who can craft it? Search by profession, level or recipe")]
		public async Task WhoAsync(
			[Summary("recipe", "Text to look for in known recipes")] string? recipe = null,
			[Summary("profession", "Profession"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? profession = null,
			[Summary("min_level", "Minimum skill level"), MinValue(1), MaxValue(300)] int? minLevel = null)
		{
			await DeferAsync(ephemeral: true);
			var search = recipe?.Trim();
			var professions = await reader.SearchProfessionsAsync(search, profession, minLevel, 20);
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
			var found = await reader.CharacterWithRecipesAsync(character);
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
			var (professions, recipeCounts) = await reader.DirectoryAsync(profession);
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
			if (!await reader.IsKnownProfessionAsync(profession))
			{
				await FollowupAsync(l.T($"Métier inconnu : **{profession}**. Choisis-le dans la liste proposée.",
					$"Unknown profession: **{profession}**. Pick one from the suggestions."), ephemeral: true);
				return;
			}
			var result = await roster.SetProfessionFromDiscordAsync(
				DiscordId, Context.User.Username, character.Trim(), profession, level, 300, note);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("remove", "Remove a profession from one of your characters")]
		public async Task RemoveAsync(
			[Summary("character", "Character name in game"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))] string character,
			[Summary("profession", "Profession to remove"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string profession)
		{
			await DeferAsync(ephemeral: true);
			var result = await roster.RemoveProfessionAsync(DiscordId, character.Trim(), profession);
			await FollowupAsync(result.Message, ephemeral: true);
		}
	}
}
