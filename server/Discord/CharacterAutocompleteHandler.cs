using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

/// <summary>Propose les personnages connus de l'annuaire (ceux qui ont des recettes en premier).</summary>
public sealed class CharacterAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var db = services.GetRequiredService<AppDbContext>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().ToLower();

		var names = await db.Characters.AsNoTracking()
			.Where(c => typed == "" || c.Name.ToLower().Contains(typed))
			.OrderByDescending(c => c.Professions.Any(p => p.Recipes.Any()))
			.ThenBy(c => c.Name)
			.Select(c => c.Name)
			.Take(25)
			.ToListAsync();

		return AutocompletionResult.FromSuccess(names.Select(name => new AutocompleteResult(name, name)));
	}
}
