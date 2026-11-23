using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Quetes.Application;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Quetes.Presentation;

/// <summary>Quêtes relevées dans la guilde dont le titre contient le texte tapé, les plus partagées d'abord.</summary>
public sealed class QuestAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var quests = services.GetRequiredService<QuestService>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim();
		var suggestions = (await quests.AllAsync())
			.Where(q => typed == "" || q.Title.Contains(typed, StringComparison.OrdinalIgnoreCase) || (q.Location ?? "").Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(25)
			.Select(q => new AutocompleteResult(Text.ForAutocomplete($"{q.Title} · {q.Location ?? "?"} · {q.Holders.Count}"), q.Id.ToString()));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}
