using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Services;

public sealed class ExportBuilder(AppDbContext db, RosterService roster, IConfiguration config)
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

		// Commandes actives, plus l'historique récent pour l'affichage en jeu.
		var historyThreshold = DateTime.UtcNow.AddDays(-7);
		var orders = await db.CraftOrders
			.AsNoTracking()
			.Where(o => o.Status == CraftOrderStatus.Open || o.Status == CraftOrderStatus.Accepted ||
				o.UpdatedUtc >= historyThreshold)
			.OrderBy(o => o.Id)
			.Select(o => new ExportOrderDto(
				o.Id, o.ClientId, o.RequesterCharacter, o.CrafterCharacter, o.Item,
				o.Quantity, o.Note, o.Status.ToString().ToLower(), o.CreatedUtc.ToString("yyyy-MM-dd HH:mm")))
			.ToListAsync(ct);

		var version = await roster.GetDataVersionAsync(ct);
		var guildName = config["Guild:Name"];
		return new ExportDto(version, DateTime.UtcNow, recipeNames, exportCharacters, orders,
			string.IsNullOrWhiteSpace(guildName) ? null : guildName);
	}
}
