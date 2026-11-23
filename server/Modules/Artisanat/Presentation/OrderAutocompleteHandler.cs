using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Artisanat.Application;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Artisanat.Presentation;

/// <summary>
/// Propose les commandes en cours, celles qui concernent l'utilisateur en
/// premier : plus besoin de retenir un numéro.
/// </summary>
public sealed class OrderAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var service = services.GetRequiredService<CraftOrderService>();
		var l = services.GetRequiredService<BotLocalizer>();
		var discordId = context.User.Id.ToString();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().TrimStart('#').ToLower();

		var mine = await service.MemberCharactersAsync(discordId);
		var orders = (await service.ActiveAsync()).OrderByDescending(o => o.Id).Take(100).ToList();

		var suggestions = orders
			.Where(o => typed == "" || o.Id.ToString().StartsWith(typed) || o.Item.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(o => (o.CrafterCharacter is not null && mine.Contains(o.CrafterCharacter)) || mine.Contains(o.RequesterCharacter))
			.Take(25)
			.Select(o => new AutocompleteResult(
				Text.ForAutocomplete($"#{o.Id} · {o.Quantity}× {o.Item} · " +
					(o.CrafterCharacter is null
						? l.T($"demande de {o.RequesterCharacter} ({CraftOrderService.Label(o.Status, l)})", $"request by {o.RequesterCharacter} ({CraftOrderService.Label(o.Status, l)})")
						: l.T($"pour {o.CrafterCharacter} ({CraftOrderService.Label(o.Status, l)})", $"for {o.CrafterCharacter} ({CraftOrderService.Label(o.Status, l)})"))),
				o.Id));
		return AutocompletionResult.FromSuccess(suggestions);
	}
}
