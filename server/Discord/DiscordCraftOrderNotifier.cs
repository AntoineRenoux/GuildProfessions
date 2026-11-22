using Discord;
using Discord.WebSocket;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;

namespace GuildProfessions.Server.Discord;

/// <summary>DM Discord à l'artisan (nouvelle commande) et au demandeur (changement de statut).</summary>
public sealed class DiscordCraftOrderNotifier(DiscordSocketClient client, ILogger<DiscordCraftOrderNotifier> logger) : ICraftOrderNotifier
{
	public async Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId, string? announceChannelId = null)
	{
		await SendAsync(crafterDiscordId,
			$"🛠️ Nouvelle commande **#{order.Id}** pour **{order.CrafterCharacter}** : " +
			$"{order.Quantity}× **{order.Item}** demandé par {order.RequesterCharacter}." +
			(string.IsNullOrEmpty(order.Note) ? "" : $"\n> {order.Note}") +
			"\n`/craft accept " + order.Id + "` pour accepter.");
		await AnnounceAsync(announceChannelId,
			$"🛠️ Commande **#{order.Id}** : {order.Quantity}× **{order.Item}** pour **{order.CrafterCharacter}**, " +
			$"demandée par {order.RequesterCharacter}.");
	}

	// Annonce dans le salon choisi par l'admin ; un salon supprimé ou fermé au bot ne casse rien.
	private async Task AnnounceAsync(string? channelId, string message)
	{
		if (!ulong.TryParse(channelId, out var id) || client.ConnectionState != ConnectionState.Connected)
		{
			return;
		}
		try
		{
			if (await client.GetChannelAsync(id) is IMessageChannel channel)
			{
				await channel.SendMessageAsync(message);
			}
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Annonce impossible dans le salon {ChannelId}.", id);
		}
	}

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
