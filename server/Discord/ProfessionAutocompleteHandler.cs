using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

/// <summary>Métiers déjà présents dans l'annuaire de la guilde d'abord, puis ceux du jeu dans la langue de l'utilisateur.</summary>
public sealed class ProfessionAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var annuaire = services.GetRequiredService<IAnnuaire>();
		var language = services.GetRequiredService<BotLocalizer>().Language;
		var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
		var suggestions = await annuaire.ProfessionSuggestionsAsync(typed, language, 25);
		return AutocompletionResult.FromSuccess(suggestions.Select(p => new AutocompleteResult(p, p)));
	}
}
