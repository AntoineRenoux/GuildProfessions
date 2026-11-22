using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

/// <summary>Métiers déjà présents dans l'annuaire de la guilde d'abord, puis ceux du jeu dans la langue de l'utilisateur.</summary>
public sealed class ProfessionAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var db = services.GetRequiredService<AppDbContext>();
		var language = services.GetRequiredService<BotLocalizer>().Language;
		var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";

		var inGuild = await db.Professions.AsNoTracking().Select(p => p.Name).Distinct().ToListAsync();
		var suggestions = inGuild.OrderBy(n => n)
			.Concat(ProfessionChoices.For(language))
			.Distinct()
			.Where(p => p.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(25)
			.Select(p => new AutocompleteResult(p, p));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}
