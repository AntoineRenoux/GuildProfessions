using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Annuaire.Contracts;

namespace GuildProfessions.Server.Discord;

/// <summary>Propose les personnages connus de l'annuaire (ceux qui ont des recettes en premier).</summary>
public sealed class CharacterAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var annuaire = services.GetRequiredService<IAnnuaire>();
		var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
		var names = await annuaire.CharacterSuggestionsAsync(typed, 25);

		return AutocompletionResult.FromSuccess(names.Select(name => new AutocompleteResult(name, name)));
	}
}
