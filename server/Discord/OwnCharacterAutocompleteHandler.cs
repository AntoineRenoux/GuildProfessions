using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Annuaire.Contracts;

namespace GuildProfessions.Server.Discord;

/// <summary>Propose uniquement les personnages liés au compte Discord de l'appelant.</summary>
public sealed class OwnCharacterAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var annuaire = services.GetRequiredService<IAnnuaire>();
		var typed = autocompleteInteraction.Data.Current.Value?.ToString() ?? "";
		var names = await annuaire.OwnCharacterSuggestionsAsync(context.User.Id.ToString(), typed, 25);

		return AutocompletionResult.FromSuccess(names.Select(name => new AutocompleteResult(name, name)));
	}
}
