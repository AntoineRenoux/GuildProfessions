using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Evenements.Application;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Evenements.Presentation;

/// <summary>
/// Propose les objets déjà nommés dans les réservations de la guilde (relevés
/// en jeu) ; un numéro tapé est proposé tel quel, son nom viendra du jeu.
/// </summary>
public sealed class ItemAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var events = services.GetRequiredService<EventService>();
		var l = services.GetRequiredService<BotLocalizer>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim();
		var suggestions = new List<AutocompleteResult>();
		if (EventService.ParseItemId(typed) is { } typedId)
		{
			suggestions.Add(new AutocompleteResult(l.T($"Objet #{typedId}", $"Item #{typedId}"), typedId.ToString()));
		}
		suggestions.AddRange((await events.KnownItemsAsync())
			.Where(i => typed == "" || i.Name.Contains(typed, StringComparison.OrdinalIgnoreCase) || i.Id.ToString().StartsWith(typed))
			.Take(25 - suggestions.Count)
			.Select(i => new AutocompleteResult(Text.ForAutocomplete($"{i.Name} · #{i.Id}"), i.Id.ToString())));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}
