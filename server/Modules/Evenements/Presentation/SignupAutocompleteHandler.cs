using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Evenements.Application;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Evenements.Presentation;

/// <summary>
/// Propose les inscrits de l'événement choisi dans la même commande (les
/// présents d'abord) : placer un joueur sans retaper son nom.
/// </summary>
public sealed class SignupAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var events = services.GetRequiredService<EventService>();
		var l = services.GetRequiredService<BotLocalizer>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim();
		var eventOption = autocompleteInteraction.Data.Options.FirstOrDefault(o => o.Name == "event");
		if (eventOption?.Value is null || !int.TryParse(eventOption.Value.ToString(), out var eventId) ||
			await events.GetAsync(eventId) is not { } guildEvent)
		{
			return AutocompletionResult.FromSuccess();
		}
		var order = new Dictionary<string, int> { ["yes"] = 0, ["tentative"] = 1, ["none"] = 2, ["no"] = 3 };
		var suggestions = guildEvent.Signups
			.Where(s => typed == "" || s.Character.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.OrderBy(s => order.GetValueOrDefault(s.Response, 2)).ThenBy(s => s.Character)
			.Take(25)
			.Select(s => new AutocompleteResult(
				$"{s.Character} · {EventLabels.RoleLabel(s.Role, l)} · {EventLabels.ResponseLabel(s.Response, l)} · {EventLabels.PlacementLabel(s.Placement, l)}",
				s.Character));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}
