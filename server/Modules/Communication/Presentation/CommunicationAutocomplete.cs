using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Communication.Application;
using GuildProfessions.Server.Modules.Evenements.Contracts;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Communication.Presentation;

/// <summary>Modèles d'annonce de la guilde.</summary>
public sealed class TemplateAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
	{
		var communication = services.GetRequiredService<CommunicationService>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim();
		var suggestions = (await communication.TemplatesAsync())
			.Where(t => typed == "" || t.Key.Contains(typed, StringComparison.OrdinalIgnoreCase) || t.Title.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(25)
			.Select(t => new AutocompleteResult(Text.ForAutocomplete($"{t.Key} · {t.Title}"), t.Key));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}

/// <summary>Sections de la charte ; une clé nouvelle tapée est proposée telle quelle (création).</summary>
public sealed class HandbookAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
	{
		var communication = services.GetRequiredService<CommunicationService>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim();
		var sections = (await communication.HandbookAsync())
			.Where(h => typed == "" || h.Key.Contains(typed, StringComparison.OrdinalIgnoreCase) || h.Title.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(24)
			.Select(h => new AutocompleteResult(Text.ForAutocomplete($"{h.Title} ({h.Key})"), h.Key))
			.ToList();
		if (CommunicationService.NormalizeKey(typed) is { } key && sections.All(s => (string)s.Value != key))
		{
			sections.Insert(0, new AutocompleteResult(Text.ForAutocomplete(key), key));
		}
		return AutocompletionResult.FromSuccess(sections);
	}
}

/// <summary>Objectifs de la guilde, ouverts d'abord.</summary>
public sealed class GoalAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
	{
		var communication = services.GetRequiredService<CommunicationService>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().TrimStart('#');
		var suggestions = (await communication.GoalsAsync(includeClosed: true))
			.Where(g => typed == "" || g.Id.ToString().StartsWith(typed, StringComparison.Ordinal) || g.Title.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(25)
			.Select(g => new AutocompleteResult(
				Text.ForAutocomplete($"#{g.Id} · {g.Title} · {g.Contributions.Sum(c => c.Amount)}/{g.Target}{(g.Closed ? " ✓" : "")}"), g.Id));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}

/// <summary>Annonces récentes.</summary>
public sealed class AnnouncementAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
	{
		var communication = services.GetRequiredService<CommunicationService>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().TrimStart('#');
		var suggestions = (await communication.RecentAnnouncementsAsync(50))
			.Where(a => typed == "" || a.Id.ToString().StartsWith(typed, StringComparison.Ordinal) || a.Title.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(25)
			.Select(a => new AutocompleteResult(Text.ForAutocomplete($"#{a.Id} · {a.Title}"), a.Id));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}

/// <summary>Prochains événements, lus par le contrat public du module Événements.</summary>
public sealed class AnnouncementEventAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction, IParameterInfo parameter, IServiceProvider services)
	{
		var evenements = services.GetRequiredService<IEvenements>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().TrimStart('#');
		var suggestions = (await evenements.UpcomingBriefsAsync(50))
			.Where(e => typed == "" || e.Id.ToString().StartsWith(typed, StringComparison.Ordinal) || e.Title.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(25)
			.Select(e => new AutocompleteResult(
				Text.ForAutocomplete($"#{e.Id} · {e.Title} · {DateTimeOffset.FromUnixTimeSeconds(e.StartsAt):dd/MM HH:mm} UTC"), e.Id));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}
