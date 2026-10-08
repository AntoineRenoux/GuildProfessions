using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

[Group("craft", "Commandes de craft entre membres")]
public sealed class CraftModule(CraftOrderService orders, AppDbContext db) : InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("order", "Passer une commande de craft à un artisan")]
	public async Task OrderAsync(
		[Summary("artisan", "Nom du personnage artisan")] string artisan,
		[Summary("objet", "L'objet ou la recette demandés")] string objet,
		[Summary("quantite", "Quantité"), MinValue(1), MaxValue(200)] int quantite = 1,
		[Summary("note", "Précisions (matériaux fournis, urgence...)")] string? note = null)
	{
		await DeferAsync();
		var result = await orders.CreateFromDiscordAsync(
			Context.User.Id.ToString(), Context.User.Username, artisan.Trim(), objet.Trim(), quantite, note);
		await FollowupAsync(result.Message);
	}

	[SlashCommand("list", "Voir les commandes de craft en cours")]
	public async Task ListAsync(
		[Summary("filtre", "Limiter aux commandes qui me concernent")]
		[Choice("toutes", "toutes"), Choice("mes commandes", "mine"), Choice("à crafter pour moi", "pour-moi")]
		string filtre = "toutes")
	{
		await DeferAsync();
		var memberCharacters = await db.Characters
			.Where(c => c.Member!.DiscordId == Context.User.Id.ToString())
			.Select(c => c.Name)
			.ToListAsync();

		var query = db.CraftOrders.AsNoTracking()
			.Where(o => o.Status == CraftOrderStatus.Open || o.Status == CraftOrderStatus.Accepted);
		query = filtre switch
		{
			"mine" => query.Where(o => memberCharacters.Contains(o.RequesterCharacter) || o.RequesterCharacter == Context.User.Username),
			"pour-moi" => query.Where(o => memberCharacters.Contains(o.CrafterCharacter)),
			_ => query,
		};
		var list = await query.OrderBy(o => o.Id).Take(25).ToListAsync();

		if (list.Count == 0)
		{
			await FollowupAsync("Aucune commande en cours.");
			return;
		}

		var embed = new EmbedBuilder()
			.WithTitle("Commandes de craft")
			.WithColor(new Color(0xff, 0xa5, 0x00));
		foreach (var order in list)
		{
			embed.AddField(
				$"#{order.Id} — {order.Quantity}× {order.Item} ({CraftOrderService.Label(order.Status)})",
				$"demandé par **{order.RequesterCharacter}** à **{order.CrafterCharacter}**" +
				(string.IsNullOrEmpty(order.Note) ? "" : $"\n_{order.Note}_"));
		}
		embed.WithFooter("/craft accept|done|cancel <id>");
		await FollowupAsync(embed: embed.Build());
	}

	[SlashCommand("accept", "Accepter une commande (artisan)")]
	public async Task AcceptAsync([Summary("id", "Numéro de la commande")] int id)
	{
		await DeferAsync();
		var result = await orders.UpdateStatusFromDiscordAsync(Context.User.Id.ToString(), id, CraftOrderStatus.Accepted);
		await FollowupAsync(result.Message);
	}

	[SlashCommand("done", "Marquer une commande comme terminée (artisan)")]
	public async Task DoneAsync([Summary("id", "Numéro de la commande")] int id)
	{
		await DeferAsync();
		var result = await orders.UpdateStatusFromDiscordAsync(Context.User.Id.ToString(), id, CraftOrderStatus.Done);
		await FollowupAsync(result.Message);
	}

	[SlashCommand("cancel", "Annuler une commande (artisan ou demandeur)")]
	public async Task CancelAsync([Summary("id", "Numéro de la commande")] int id)
	{
		await DeferAsync();
		var result = await orders.UpdateStatusFromDiscordAsync(Context.User.Id.ToString(), id, CraftOrderStatus.Cancelled);
		await FollowupAsync(result.Message);
	}
}
