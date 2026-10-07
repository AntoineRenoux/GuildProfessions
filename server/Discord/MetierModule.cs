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
		if (!ProfessionChoices.IsValid(metier))
		{
			await RespondAsync($"Métier inconnu : **{metier}**. Utilise l'autocomplétion.", ephemeral: true);
			return;
		}
		var result = await roster.SetProfessionFromDiscordAsync(
			Context.User.Id.ToString(), Context.User.Username, personnage.Trim(), metier, niveau, 300, note);
		await RespondAsync(result.Message, ephemeral: !result.Success);
	}

	[SlashCommand("remove", "Retirer un métier d'un personnage")]
	public async Task RemoveAsync(
		[Summary("personnage", "Nom du personnage en jeu")] string personnage,
		[Summary("metier", "Le métier à retirer"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string metier)
	{
		var result = await roster.RemoveProfessionAsync(Context.User.Id.ToString(), personnage.Trim(), metier);
		await RespondAsync(result.Message, ephemeral: !result.Success);
	}

	[SlashCommand("list", "Lister les métiers de la guilde")]
	public async Task ListAsync(
		[Summary("metier", "Limiter à un métier"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? metier = null)
	{
		var query = db.Professions.AsNoTracking().Include(p => p.Character).AsQueryable();
		if (metier is not null)
		{
			query = query.Where(p => p.Name == metier);
		}
		var professions = await query
			.OrderBy(p => p.Name).ThenByDescending(p => p.SkillLevel)
			.ToListAsync();

		if (professions.Count == 0)
		{
			await RespondAsync("Aucun métier enregistré pour l'instant. `/metier set` pour commencer !", ephemeral: true);
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
				(string.IsNullOrEmpty(p.Note) ? "" : $" — _{p.Note}_"));
			embed.AddField(group.Key, string.Join("\n", lines));
		}
		embed.WithFooter("✓ = scanné en jeu par l'addon");

		await RespondAsync(embed: embed.Build());
	}

	[SlashCommand("who", "Qui peut craft ? Recherche par métier, niveau ou recette")]
	public async Task WhoAsync(
		[Summary("metier", "Le métier recherché"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? metier = null,
		[Summary("niveau_min", "Niveau de compétence minimal"), MinValue(1), MaxValue(300)] int? niveauMin = null,
		[Summary("recette", "Texte à chercher dans les recettes connues")] string? recette = null)
	{
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
			await RespondAsync("Personne ne correspond à ces critères.", ephemeral: true);
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

		await RespondAsync(embed: embed.Build());
	}
}
