using Discord;
using Discord.WebSocket;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;

namespace GuildProfessions.Server.Discord;

/// <summary>DM Discord à l'artisan (nouvelle commande) et au demandeur (changement de statut).</summary>
public sealed class DiscordCraftOrderNotifier(DiscordSocketClient client, ILogger<DiscordCraftOrderNotifier> logger) : ICraftOrderNotifier
{
	public Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId) =>
		SendAsync(crafterDiscordId,
			$"🛠️ Nouvelle commande **#{order.Id}** pour **{order.CrafterCharacter}** : " +
			$"{order.Quantity}× **{order.Item}** demandé par {order.RequesterCharacter}." +
			(string.IsNullOrEmpty(order.Note) ? "" : $"\n> {order.Note}") +
			"\n`/craft accept " + order.Id + "` pour accepter.");

	public Task NotifyStatusChangedAsync(CraftOrder order, string? requesterDiscordId) =>
		SendAsync(requesterDiscordId,
			$"📦 Ta commande **#{order.Id}** ({order.Quantity}× {order.Item}, artisan {order.CrafterCharacter}) " +
			$"est maintenant **{CraftOrderService.Label(order.Status)}**.");

	private async Task SendAsync(string? discordId, string message)
	{
		if (discordId is null || client.ConnectionState != ConnectionState.Connected ||
			!ulong.TryParse(discordId, out var userId))
		{
			return;
		}
		try
		{
			var user = await client.GetUserAsync(userId);
			if (user is not null)
			{
				await user.SendMessageAsync(message);
			}
		}
		catch (Exception exception)
		{
			// DM fermés ou utilisateur introuvable : on ne casse jamais l'écriture pour une notification.
			logger.LogWarning(exception, "DM Discord impossible vers {UserId}.", userId);
		}
	}
}
