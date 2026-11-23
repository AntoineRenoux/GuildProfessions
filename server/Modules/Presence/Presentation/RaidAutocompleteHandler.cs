using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Presence.Application;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Presence.Presentation;

/// <summary>Propose les raids relevés des 90 derniers jours, le plus récent en premier.</summary>
public sealed class RaidAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var attendance = services.GetRequiredService<AttendanceService>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().TrimStart('#');
		var raids = await attendance.RecentAsync(AttendanceService.HistoryWindow);
		var suggestions = raids
			.Where(r => typed == "" || r.Id.ToString().StartsWith(typed) || r.Instance.Contains(typed, StringComparison.OrdinalIgnoreCase) || r.Day.Contains(typed))
			.Take(25)
			.Select(r => new AutocompleteResult($"#{r.Id} · {r.Day} · {r.Instance} · {r.Participants.Count}", r.Id));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}

/// <summary>Propose les objets relevés récemment (pour les annuler ou les rétablir).</summary>
public sealed class LootAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var attendance = services.GetRequiredService<AttendanceService>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().TrimStart('#');
		var raids = await attendance.RecentAsync(TimeSpan.FromDays(30));
		var suggestions = raids
			.SelectMany(r => r.Loot.Select(l => (r, l)))
			.Where(x => typed == "" || x.l.Id.ToString().StartsWith(typed) || x.l.Character.Contains(typed, StringComparison.OrdinalIgnoreCase) ||
				(x.l.ItemName ?? "").Contains(typed, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(x => x.l.At)
			.Take(25)
			.Select(x => new AutocompleteResult(Text.ForAutocomplete(
				$"#{x.l.Id} · {x.l.ItemName ?? $"objet {x.l.ItemId}"} · {x.l.Character} · {x.r.Day}{(x.l.Voided ? " (annulé)" : "")}"), x.l.Id));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}
