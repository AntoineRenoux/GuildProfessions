using Discord;
using Discord.WebSocket;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Artisanat.Application;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Artisanat.Infrastructure;

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
		if (order.CrafterCharacter is null)
		{
			await AnnounceAsync(announceChannelId, l.T(
				$"🛠️ Demande **#{order.Id}** : {order.Quantity}× **{order.Item}**, par {order.RequesterCharacter}. Qui peut le faire ? `/gkeep commande proposer numero:{order.Id}`{note}",
				$"🛠️ Request **#{order.Id}**: {order.Quantity}× **{order.Item}**, by {order.RequesterCharacter}. Who can make it? `/gkeep order offer id:{order.Id}`{note}"));
			return;
		}
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
		var next = order.Status is CraftOrderStatus.Ready or CraftOrderStatus.Mailed
			? l.T($"\nUne fois l'objet en main : `/gkeep commande recu numero:{order.Id}`", $"\nOnce you have it: `/gkeep order received id:{order.Id}`")
			: "";
		return SendAsync(requesterDiscordId, l.T(
			$"📦 Ta commande **#{order.Id}** ({order.Quantity}× {order.Item}, artisan {order.CrafterCharacter}) est maintenant **{status}**.{next}",
			$"📦 Your order **#{order.Id}** ({order.Quantity}× {order.Item}, crafter {order.CrafterCharacter}) is now **{status}**.{next}"));
	}

	public Task NotifyOfferAsync(CraftOrder order, CraftOffer offer, string? requesterDiscordId, BotLanguage language = BotLanguage.French)
	{
		var l = BotLocalizer.For(language);
		var note = string.IsNullOrEmpty(offer.Note) ? "" : $"\n> {offer.Note}";
		return SendAsync(requesterDiscordId, l.T(
			$"🙋 **{offer.Crafter}** propose de faire ta demande **#{order.Id}** ({order.Quantity}× {order.Item}).{note}\n" +
				$"Pour la lui confier : `/gkeep commande accepter numero:{order.Id} artisan:{offer.Crafter}`",
			$"🙋 **{offer.Crafter}** offers to make your request **#{order.Id}** ({order.Quantity}× {order.Item}).{note}\n" +
				$"To give it to them: `/gkeep order accept id:{order.Id} crafter:{offer.Crafter}`"));
	}

	public Task NotifyCrafterAsync(CraftOrder order, string? crafterDiscordId, BotLanguage language = BotLanguage.French)
	{
		var l = BotLocalizer.For(language);
		var text = order.Status switch
		{
			CraftOrderStatus.Accepted => l.T(
				$"✅ {order.RequesterCharacter} te confie la commande **#{order.Id}** : {order.Quantity}× **{order.Item}**. Signale-la prête ou envoyée une fois faite.",
				$"✅ {order.RequesterCharacter} gave you order **#{order.Id}**: {order.Quantity}× **{order.Item}**. Mark it ready or mailed once made."),
			CraftOrderStatus.Done => l.T(
				$"🎉 {order.RequesterCharacter} a bien reçu la commande **#{order.Id}** ({order.Item}). Merci !",
				$"🎉 {order.RequesterCharacter} received order **#{order.Id}** ({order.Item}). Thanks!"),
			_ => l.T(
				$"📦 La commande **#{order.Id}** ({order.Item}) de {order.RequesterCharacter} est maintenant **{CraftOrderService.Label(order.Status, l)}**.",
				$"📦 Order **#{order.Id}** ({order.Item}) from {order.RequesterCharacter} is now **{CraftOrderService.Label(order.Status, l)}**."),
		};
		return SendAsync(crafterDiscordId, text);
	}

	public async Task NotifyOpenRequestAsync(CraftOrder order, IReadOnlyList<string> crafterDiscordIds, BotLanguage language = BotLanguage.French)
	{
		var l = BotLocalizer.For(language);
		foreach (var discordId in crafterDiscordIds)
		{
			await SendAsync(discordId, l.T(
				$"🛠️ {order.RequesterCharacter} cherche quelqu'un pour **{order.Quantity}× {order.Item}**, et tu connais la recette. " +
					$"Pour proposer : `/gkeep commande proposer numero:{order.Id}`",
				$"🛠️ {order.RequesterCharacter} is looking for someone to make **{order.Quantity}× {order.Item}**, and you know the recipe. " +
					$"To offer: `/gkeep order offer id:{order.Id}`"));
		}
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
