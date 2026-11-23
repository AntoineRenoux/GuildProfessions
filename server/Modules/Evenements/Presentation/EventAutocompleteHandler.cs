using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Evenements.Application;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Evenements.Presentation;

/// <summary>Propose les événements à venir, le plus proche en premier.</summary>
public sealed class EventAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var events = services.GetRequiredService<EventService>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().TrimStart('#');
		var upcoming = await events.UpcomingAsync(50);
		var suggestions = upcoming
			.Where(e => typed == "" || e.Id.ToString().StartsWith(typed) || e.Title.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(25)
			.Select(e => new AutocompleteResult(
				Text.ForAutocomplete($"#{e.Id} · {e.Title} · {DateTimeOffset.FromUnixTimeSeconds(e.StartsAt):dd/MM HH:mm} UTC"), e.Id));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}
