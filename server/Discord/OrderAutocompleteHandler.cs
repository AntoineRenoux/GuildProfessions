using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

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
		var db = services.GetRequiredService<AppDbContext>();
		var l = services.GetRequiredService<BotLocalizer>();
		var discordId = context.User.Id.ToString();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().TrimStart('#').ToLower();

		var mine = await db.Characters.AsNoTracking()
			.Where(c => c.Member != null && c.Member.DiscordId == discordId)
			.Select(c => c.Name)
			.ToListAsync();
		var orders = await db.CraftOrders.AsNoTracking()
			.Where(o => o.Status == CraftOrderStatus.Open || o.Status == CraftOrderStatus.Accepted)
			.OrderByDescending(o => o.Id)
			.Take(100)
			.ToListAsync();

		var suggestions = orders
			.Where(o => typed == "" || o.Id.ToString().StartsWith(typed) || o.Item.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.OrderByDescending(o => mine.Contains(o.CrafterCharacter) || mine.Contains(o.RequesterCharacter))
			.Take(25)
			.Select(o => new AutocompleteResult(
				Truncate($"#{o.Id} · {o.Quantity}× {o.Item} · " +
					l.T($"pour {o.CrafterCharacter} ({CraftOrderService.Label(o.Status, l)})", $"for {o.CrafterCharacter} ({CraftOrderService.Label(o.Status, l)})")),
				o.Id));
		return AutocompletionResult.FromSuccess(suggestions);
	}

	// Discord limite le libellé d'une suggestion à 100 caractères.
	private static string Truncate(string text) => text.Length <= 100 ? text : text[..97] + "...";
}
