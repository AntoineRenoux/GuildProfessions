using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Artisanat.Application;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Artisanat.Presentation;

/// <summary>Recettes du catalogue de la guilde (puis de l'annuaire) dont le nom contient le texte tapé.</summary>
public sealed class RecipeAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var catalog = services.GetRequiredService<CatalogService>();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim();
		if (typed.Length < 2)
		{
			return AutocompletionResult.FromSuccess();
		}
		var names = await catalog.SuggestNamesAsync(typed);
		var suggestions = names.Select(n => new AutocompleteResult(n.Length <= 100 ? n : n[..100], n.Length <= 100 ? n : n[..100]));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}

/// <summary>Offres en cours sur la commande choisie dans la même commande Discord.</summary>
public sealed class OfferAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var orders = services.GetRequiredService<CraftOrderService>();
		var option = autocompleteInteraction.Data.Options.FirstOrDefault(o => o.Name == "id");
		if (option?.Value is null || !int.TryParse(option.Value.ToString(), out var orderId))
		{
			return AutocompletionResult.FromSuccess();
		}
		var offers = await orders.OpenOffersAsync(orderId);
		return AutocompletionResult.FromSuccess(offers.Select(f =>
			new AutocompleteResult(f.Note is null ? f.Crafter : Text.ForAutocomplete($"{f.Crafter} — {f.Note}"), f.Crafter)));
	}
}
