using Discord;
using Discord.Interactions;

namespace GuildProfessions.Server.Discord;

public sealed class ProfessionAutocompleteHandler : AutocompleteHandler
{
	public override Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
		var suggestions = ProfessionChoices.All
			.Where(p => p.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(25)
			.Select(p => new AutocompleteResult(p, p));
		return Task.FromResult(AutocompletionResult.FromSuccess(suggestions));
	}
}
