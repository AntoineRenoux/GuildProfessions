using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Artisanat.Application;

/// <summary>
/// Catalogue des recettes de la guilde, nourri par les fenêtres de métier
/// des membres (toutes les recettes visibles, apprises ou non) et par les
/// recettes connues de l'annuaire. Aucune base de données extérieure : le
/// catalogue grandit à mesure que les membres ouvrent leurs métiers.
/// </summary>
public sealed class CatalogService(AppDbContext db, IAnnuaire annuaire, IDataVersion dataVersion)
{
	/// <summary>Ajoute au catalogue les recettes vues dans l'envoi ; true si quelque chose a changé (version incrémentée).</summary>
	public async Task<bool> ApplyUploadAsync(UploadPayload payload, CancellationToken ct = default)
	{
		var incoming = new Dictionary<int, (string Name, string Profession)>();
		foreach (var profession in payload.Characters.SelectMany(c => c.Professions))
		{
			foreach (var recipe in (profession.Catalog ?? []).Concat(profession.Recipes ?? []))
			{
				if (recipe.Id > 0 && !string.IsNullOrWhiteSpace(recipe.Name))
				{
					incoming[recipe.Id] = (Text.Clip(recipe.Name, 100) ?? "", profession.Name);
				}
			}
		}
		if (incoming.Count == 0)
		{
			return false;
		}
		var ids = incoming.Keys.ToList();
		var existing = await db.CatalogRecipes.Where(r => ids.Contains(r.SpellId)).ToDictionaryAsync(r => r.SpellId, ct);
		var changed = false;
		foreach (var (spellId, (name, profession)) in incoming)
		{
			if (existing.TryGetValue(spellId, out var row))
			{
				if (row.Name != name || row.Profession != profession)
				{
					row.Name = name;
					row.Profession = profession;
					row.UpdatedUtc = DateTime.UtcNow;
					changed = true;
				}
				continue;
			}
			db.CatalogRecipes.Add(new CatalogRecipe { SpellId = spellId, Name = name, Profession = profession, UpdatedUtc = DateTime.UtcNow });
			changed = true;
		}
		if (changed)
		{
			await dataVersion.SaveAndBumpAsync(ct);
		}
		return changed;
	}

	public async Task<List<ExportCatalogRecipeDto>> BuildExportAsync(CancellationToken ct = default) =>
		await db.CatalogRecipes.AsNoTracking()
			.OrderBy(r => r.Profession).ThenBy(r => r.Name)
			.Select(r => new ExportCatalogRecipeDto(r.SpellId, r.Name, r.Profession))
			.ToListAsync(ct);

	public sealed record CatalogHit(int SpellId, string Name, string Profession, List<string> Crafters);

	/// <summary>Recettes du catalogue dont le nom contient le texte, avec qui sait les faire.</summary>
	public async Task<List<CatalogHit>> SearchAsync(string text, string? profession = null, int limit = 25, CancellationToken ct = default)
	{
		var needle = text.Trim().ToLower();
		var query = db.CatalogRecipes.AsNoTracking().Where(r => r.Name.ToLower().Contains(needle));
		if (!string.IsNullOrWhiteSpace(profession))
		{
			query = query.Where(r => r.Profession == profession);
		}
		var hits = await query.OrderBy(r => r.Name).Take(limit).ToListAsync(ct);
		var crafters = await annuaire.CraftersOfRecipesAsync(hits.Select(h => h.SpellId).ToList(), ct);
		return hits.Select(h => new CatalogHit(h.SpellId, h.Name, h.Profession,
			crafters.TryGetValue(h.SpellId, out var names) ? [.. names] : [])).ToList();
	}

	/// <summary>Noms de recettes contenant le texte : catalogue de la guilde d'abord, puis recettes connues de l'annuaire.</summary>
	public async Task<List<string>> SuggestNamesAsync(string text, int max = 25, CancellationToken ct = default)
	{
		var needle = text.Trim().ToLower();
		var names = await db.CatalogRecipes.AsNoTracking()
			.Where(r => r.Name.ToLower().Contains(needle))
			.Select(r => r.Name).Distinct().OrderBy(n => n).Take(max)
			.ToListAsync(ct);
		if (names.Count < max)
		{
			names.AddRange(await annuaire.RecipeNamesContainingAsync(text, max, ct));
		}
		return names.Distinct().Take(max).ToList();
	}
}
