using Discord;
using Discord.WebSocket;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Message privé à l'artisan (nouvelle commande) et au demandeur (changement
/// de statut), plus l'annonce dans le salon choisi par la guilde. Dans la
/// langue de la guilde : celle du destinataire n'est pas connue hors interaction.
/// </summary>
public sealed class DiscordCraftOrderNotifier(DiscordSocketClient client, ILogger<DiscordCraftOrderNotifier> logger) : ICraftOrderNotifier
{
	public async Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId, string? announceChannelId = null,
		BotLanguage language = BotLanguage.French)
	{
		var l = BotLocalizer.For(language);
		var note = string.IsNullOrEmpty(order.Note) ? "" : $"\n> {order.Note}";
		await SendAsync(crafterDiscordId, l.T(
			$"🛠️ Nouvelle commande **#{order.Id}** pour **{order.CrafterCharacter}** : {order.Quantity}× **{order.Item}**, " +
				$"demandée par {order.RequesterCharacter}.{note}\nSur le serveur : `/gkeep commande accepter numero:{order.Id}`",
			$"🛠️ New order **#{order.Id}** for **{order.CrafterCharacter}**: {order.Quantity}× **{order.Item}**, " +
				$"requested by {order.RequesterCharacter}.{note}\nOn the server: `/gkeep order accept id:{order.Id}`"));
		await AnnounceAsync(announceChannelId, l.T(
			$"🛠️ Commande **#{order.Id}** : {order.Quantity}× **{order.Item}** pour **{order.CrafterCharacter}**, demandée par {order.RequesterCharacter}.",
			$"🛠️ Order **#{order.Id}**: {order.Quantity}× **{order.Item}** for **{order.CrafterCharacter}**, requested by {order.RequesterCharacter}."));
	}

	public Task NotifyStatusChangedAsync(CraftOrder order, string? requesterDiscordId, BotLanguage language = BotLanguage.French)
	{
		var l = BotLocalizer.For(language);
		var status = CraftOrderService.Label(order.Status, l);
		return SendAsync(requesterDiscordId, l.T(
			$"📦 Ta commande **#{order.Id}** ({order.Quantity}× {order.Item}, artisan {order.CrafterCharacter}) est maintenant **{status}**.",
			$"📦 Your order **#{order.Id}** ({order.Quantity}× {order.Item}, crafter {order.CrafterCharacter}) is now **{status}**."));
	}

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
			// Messages privés fermés ou utilisateur introuvable : on ne casse jamais l'écriture pour une notification.
			logger.LogWarning(exception, "Message privé Discord impossible vers {UserId}.", userId);
		}
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
}
