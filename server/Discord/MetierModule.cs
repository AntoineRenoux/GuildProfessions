using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

[Group("metier", "Annuaire des métiers de la guilde")]
public sealed class MetierModule(RosterService roster, AppDbContext db) : InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("set", "Déclarer ou mettre à jour un métier d'un personnage")]
	public async Task SetAsync(
		[Summary("personnage", "Nom du personnage en jeu")] string personnage,
		[Summary("metier", "Le métier"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string metier,
		[Summary("niveau", "Niveau de compétence actuel"), MinValue(1), MaxValue(300)] int niveau,
		[Summary("note", "Note libre (dispo, conditions...)")] string? note = null)
	{
		await DeferAsync();
		if (!ProfessionChoices.IsValid(metier))
		{
			await FollowupAsync($"Métier inconnu : **{metier}**. Utilise l'autocomplétion.");
			return;
		}
		var result = await roster.SetProfessionFromDiscordAsync(
			Context.User.Id.ToString(), Context.User.Username, personnage.Trim(), metier, niveau, 300, note);
		await FollowupAsync(result.Message);
	}

	[SlashCommand("remove", "Retirer un métier d'un personnage")]
	public async Task RemoveAsync(
		[Summary("personnage", "Nom du personnage en jeu")] string personnage,
		[Summary("metier", "Le métier à retirer"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string metier)
	{
		await DeferAsync();
		var result = await roster.RemoveProfessionAsync(Context.User.Id.ToString(), personnage.Trim(), metier);
		await FollowupAsync(result.Message);
	}

	[SlashCommand("list", "Lister les métiers de la guilde")]
	public async Task ListAsync(
		[Summary("metier", "Limiter à un métier"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? metier = null)
	{
		await DeferAsync();
		var query = db.Professions.AsNoTracking().Include(p => p.Character).AsQueryable();
		if (metier is not null)
		{
			query = query.Where(p => p.Name == metier);
		}
		var professions = await query
			.OrderBy(p => p.Name).ThenByDescending(p => p.SkillLevel)
			.ToListAsync();
		var recipeCounts = await db.Recipes.AsNoTracking()
			.GroupBy(r => r.ProfessionId)
			.Select(g => new { ProfessionId = g.Key, Count = g.Count() })
			.ToDictionaryAsync(x => x.ProfessionId, x => x.Count);

		if (professions.Count == 0)
		{
			await FollowupAsync("Aucun métier enregistré pour l'instant. `/metier set` pour commencer !");
			return;
		}

		var embed = new EmbedBuilder()
			.WithTitle(metier is null ? "Métiers de la guilde" : $"Métiers — {metier}")
			.WithColor(new Color(0x33, 0xff, 0x99));

		foreach (var group in professions.GroupBy(p => p.Name).Take(25))
		{
			var lines = group.Select(p =>
				$"**{p.Character.Name}** {p.SkillLevel}/{p.MaxSkill}" +
				(p.Source == DataSource.Addon ? " ✓" : "") +
				(recipeCounts.TryGetValue(p.Id, out var count) ? $" · {count} recette{(count > 1 ? "s" : "")}" : "") +
				(string.IsNullOrEmpty(p.Note) ? "" : $" — _{p.Note}_"));
			embed.AddField(group.Key, string.Join("\n", lines));
		}
		embed.WithFooter("✓ = scanné en jeu par l'addon · /metier recettes pour le détail");

		await FollowupAsync(embed: embed.Build());
	}

	[SlashCommand("recettes", "Voir les recettes connues d'un personnage")]
	public async Task RecipesAsync(
		[Summary("personnage", "Nom du personnage"), Autocomplete(typeof(CharacterAutocompleteHandler))] string personnage,
		[Summary("metier", "Limiter à un métier"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? metier = null,
		[Summary("recherche", "Filtrer les recettes par texte")] string? recherche = null)
	{
		await DeferAsync();
		var name = personnage.Trim().ToLower();
		var character = await db.Characters.AsNoTracking()
			.Include(c => c.Professions).ThenInclude(p => p.Recipes)
			.FirstOrDefaultAsync(c => c.Name.ToLower() == name);
		if (character is null)
		{
			await FollowupAsync($"Personnage **{personnage}** inconnu de l'annuaire.");
			return;
		}

		var filter = recherche?.Trim();
		var professions = character.Professions
			.Where(p => metier is null || p.Name == metier)
			.OrderBy(p => p.Name)
			.Select(p => (Profession: p.Name, Recipes: (IReadOnlyList<string>)p.Recipes
				.Select(r => r.Name ?? $"Recette #{r.SpellId}")
				.Where(n => string.IsNullOrEmpty(filter) || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
				.ToList()))
			.Where(p => p.Recipes.Count > 0)
			.ToList();

		if (professions.Count == 0)
		{
			var reason = !string.IsNullOrEmpty(filter)
				? $"aucune recette ne contient « {filter} »"
				: "aucune recette scannée — il faut ouvrir la fenêtre du métier en jeu, avec l'addon installé";
			await FollowupAsync($"**{character.Name}** : {reason}.");
			return;
		}

		var layout = RecipeListFormatter.Layout(professions);
		var total = professions.Sum(p => p.Recipes.Count);
		var embed = new EmbedBuilder()
			.WithTitle($"Recettes de {character.Name}")
			.WithDescription($"{total} recette{(total > 1 ? "s" : "")}" +
				(string.IsNullOrEmpty(filter) ? "" : $" contenant « {filter} »"))
			.WithColor(new Color(0x33, 0xff, 0x99));
		foreach (var field in layout.Fields)
		{
			embed.AddField(field.Title, field.Body, inline: false);
		}
		var footer = "Données scannées en jeu par l'addon";
		if (layout.HiddenCount > 0)
		{
			footer = $"… et {layout.HiddenCount} autres — affine avec metier: ou recherche:";
		}
		embed.WithFooter(footer);

		await FollowupAsync(embed: embed.Build());
	}

	[SlashCommand("who", "Qui peut craft ? Recherche par métier, niveau ou recette")]
	public async Task WhoAsync(
		[Summary("metier", "Le métier recherché"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? metier = null,
		[Summary("niveau_min", "Niveau de compétence minimal"), MinValue(1), MaxValue(300)] int? niveauMin = null,
		[Summary("recette", "Texte à chercher dans les recettes connues")] string? recette = null)
	{
		await DeferAsync();
		var query = db.Professions.AsNoTracking()
			.Include(p => p.Character)
			.Include(p => p.Recipes)
			.AsQueryable();

		if (metier is not null)
		{
			query = query.Where(p => p.Name == metier);
		}
		if (niveauMin is not null)
		{
			query = query.Where(p => p.SkillLevel >= niveauMin);
		}
		if (!string.IsNullOrWhiteSpace(recette))
		{
			var pattern = $"%{recette.Trim()}%";
			query = query.Where(p => p.Recipes.Any(r => r.Name != null && EF.Functions.Like(r.Name, pattern)));
		}

		var professions = await query
			.OrderByDescending(p => p.SkillLevel)
			.Take(20)
			.ToListAsync();

		if (professions.Count == 0)
		{
			await FollowupAsync("Personne ne correspond à ces critères.");
			return;
		}

		var embed = new EmbedBuilder()
			.WithTitle("Qui peut craft ?")
			.WithColor(new Color(0x33, 0xff, 0x99));

		foreach (var profession in professions)
		{
			var matchedRecipes = string.IsNullOrWhiteSpace(recette)
				? ""
				: "\n" + string.Join("\n", profession.Recipes
					.Where(r => r.Name is not null && r.Name.Contains(recette.Trim(), StringComparison.OrdinalIgnoreCase))
					.Take(5)
					.Select(r => $"• {r.Name}"));
			embed.AddField(
				$"{profession.Character.Name} — {profession.Name} {profession.SkillLevel}/{profession.MaxSkill}",
				(string.IsNullOrEmpty(profession.Note) ? "_(pas de note)_" : $"_{profession.Note}_") + matchedRecipes);
		}

		await FollowupAsync(embed: embed.Build());
	}
}
