using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Services;

public sealed class ExportBuilder(AppDbContext db, RosterService roster)
{
	public async Task<ExportDto> BuildAsync(CancellationToken ct = default)
	{
		var characters = await db.Characters
			.AsNoTracking()
			.Include(c => c.Professions).ThenInclude(p => p.Recipes)
			.OrderBy(c => c.Name)
			.ToListAsync(ct);

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
				character.RaceId, character.Gender, character.Level, professions));
		}

		var version = await roster.GetDataVersionAsync(ct);
		return new ExportDto(version, DateTime.UtcNow, recipeNames, exportCharacters);
	}
}
