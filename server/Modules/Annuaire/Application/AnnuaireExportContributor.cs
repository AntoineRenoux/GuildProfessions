using System.Text.Json;
using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Annuaire.Application;

/// <summary>Part de l'annuaire dans l'export : personnages, métiers, équipement et noms de recettes.</summary>
public sealed class AnnuaireExportContributor(AppDbContext db) : IExportContributor
{
	public string Module => "Annuaire";

	public int Order => 0;

	public async Task ContributeAsync(ExportDraft draft, CancellationToken ct)
	{
		var characters = await db.Characters
			.AsNoTracking()
			.Include(c => c.Professions).ThenInclude(p => p.Recipes)
			.OrderBy(c => c.Name)
			.ToListAsync(ct);

		// Sections assignées d'un bloc à la fin : une panne en cours de route ne laisse rien de partiel.
		var recipeNames = new Dictionary<int, string>();
		var exportCharacters = new List<ExportCharacterDto>(characters.Count);
		foreach (var character in characters)
		{
			var professions = character.Professions
				.OrderBy(p => p.Name)
				.Select(p =>
				{
					foreach (var recipe in p.Recipes.Where(r => r.Name is not null))
					{
						recipeNames.TryAdd(recipe.SpellId, recipe.Name!);
					}
					return new ExportProfessionDto(
						p.Name, p.SkillLevel, p.MaxSkill, p.Note,
						p.Source == DataSource.Addon ? "addon" : "discord",
						p.ScannedAt,
						p.Recipes.OrderBy(r => r.SpellId).Select(r => r.SpellId).ToList());
				})
				.ToList();

			exportCharacters.Add(new ExportCharacterDto(
				character.Name, character.ClassFile, character.RaceFile,
				character.RaceId, character.Gender, character.Level, professions, ReadEquipment(character.EquipmentJson)));
		}
		draft.RecipeNames = recipeNames;
		draft.Characters = exportCharacters;
	}

	private static Dictionary<int, string>? ReadEquipment(string? json)
	{
		if (json is null)
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<Dictionary<int, string>>(json);
		}
		catch (JsonException)
		{
			// Colonne corrompue : on exporte sans équipement plutôt que d'échouer.
			return null;
		}
	}
}
