using GuildProfessions.Server.Data;
using GuildProfessions.Server.Shared;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Annuaire.Application;

/// <summary>Lectures de l'annuaire pour les commandes et suggestions Discord du module.</summary>
public sealed class AnnuaireReader(AppDbContext db)
{
	/// <summary>Qui peut le fabriquer : métiers filtrés par nom, niveau minimal et texte d'une recette connue.</summary>
	public async Task<List<Profession>> SearchProfessionsAsync(string? recipe, string? profession, int? minLevel, int max,
		CancellationToken ct = default)
	{
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
		return await query.OrderByDescending(p => p.SkillLevel).Take(max).ToListAsync(ct);
	}

	/// <summary>Un personnage avec ses métiers et recettes, sans tenir compte de la casse.</summary>
	public async Task<Character?> CharacterWithRecipesAsync(string name, CancellationToken ct = default)
	{
		var lowered = name.Trim().ToLower();
		return await db.Characters.AsNoTracking()
			.Include(c => c.Professions).ThenInclude(p => p.Recipes)
			.FirstOrDefaultAsync(c => c.Name.ToLower() == lowered, ct);
	}

	/// <summary>Tout l'annuaire des métiers (ou d'un seul), avec le nombre de recettes relevées par métier.</summary>
	public async Task<(List<Profession> Professions, Dictionary<int, int> RecipeCounts)> DirectoryAsync(string? profession,
		CancellationToken ct = default)
	{
		var query = db.Professions.AsNoTracking().Include(p => p.Character).AsQueryable();
		if (profession is not null)
		{
			query = query.Where(p => p.Name == profession);
		}
		var professions = await query.OrderBy(p => p.Name).ThenByDescending(p => p.SkillLevel).ToListAsync(ct);
		var recipeCounts = await db.Recipes.AsNoTracking()
			.GroupBy(r => r.ProfessionId)
			.Select(g => new { ProfessionId = g.Key, Count = g.Count() })
			.ToDictionaryAsync(x => x.ProfessionId, x => x.Count, ct);
		return (professions, recipeCounts);
	}

	/// <summary>Nom de métier du jeu, ou déjà présent dans l'annuaire de la guilde (client dans une autre langue).</summary>
	public async Task<bool> IsKnownProfessionAsync(string name, CancellationToken ct = default) =>
		ProfessionChoices.IsGameName(name) || await db.Professions.AnyAsync(p => p.Name == name, ct);
}
