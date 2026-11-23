using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Artisanat.Application;
using GuildProfessions.Server.Modules.Artisanat.Presentation;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

// Commandes Discord du module Artisanat : /gkeep order (commandes de craft, offres, catalogue).
// Le groupe reste imbriqué dans GkeepModule (Discord ne connaît les sous-commandes que
// par imbrication) et garde son namespace : même hiérarchie /gkeep <groupe> <commande>,
// mêmes clés de traduction dans commands.fr.json. Il ne dépend que de son module.
public sealed partial class GkeepModule
{
	// ---- Commandes de craft -----------------------------------------------------------------

	[Group("order", "Craft orders and open requests between members")]
	[RequireRegisteredGuild]
	public sealed class OrderGroup(CraftOrderService orders, CatalogService catalog, BotLocalizer l)
		: GuildkeepGroup
	{
		[SlashCommand("new", "Ask for a craft: open to every crafter, or from a given crafter")]
		public async Task NewAsync(
			[Summary("item", "Item or recipe you want (suggestions from the guild catalog)"), Autocomplete(typeof(RecipeAutocompleteHandler))] string item,
			[Summary("crafter", "A given crafter (leave empty to ask every crafter)"), Autocomplete(typeof(CharacterAutocompleteHandler))] string? crafter = null,
			[Summary("quantity", "Quantity"), MinValue(1), MaxValue(200)] int quantity = 1,
			[Summary("note", "Details (materials provided, deadline...)")] string? note = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await orders.CreateFromDiscordAsync(DiscordId, Context.User.Username, crafter, item.Trim(), quantity, note);
			await FollowupAsync(result.Message + (result.IsSuccess && crafter is not null
				? l.T("\nL'artisan est prévenu en message privé.", "\nThe crafter was notified by direct message.")
				: ""), ephemeral: true);
		}

		[SlashCommand("list", "Craft orders and open requests")]
		public async Task ListAsync(
			[Summary("filter", "Which ones")]
			[Choice("All open / Toutes en cours", "all"), Choice("My requests / Mes demandes", "mine"), Choice("To craft for me / À fabriquer", "for-me"),
				Choice("I can make / Que je peux faire", "can-make"), Choice("My offers / Mes offres", "my-offers")]
			string filter = "all")
		{
			await DeferAsync(ephemeral: true);
			var (mine, recipes, recipeNames) = await orders.MemberCraftingAsync(DiscordId);
			var username = Context.User.Username;
			var active = await orders.ActiveAsync();
			IEnumerable<CraftOrder> list = filter switch
			{
				"mine" => active.Where(o => mine.Contains(o.RequesterCharacter) || o.RequesterCharacter == username),
				"for-me" => active.Where(o => o.CrafterCharacter is not null && mine.Contains(o.CrafterCharacter)),
				"can-make" => active.Where(o => o.Status == CraftOrderStatus.Open && !mine.Contains(o.RequesterCharacter) &&
					((o.RecipeId is { } r && recipes.Contains(r)) || recipeNames.Contains(o.Item.ToLower()))),
				"my-offers" => active.Where(o => o.Offers.Any(f => !f.Withdrawn && mine.Contains(f.Crafter))),
				_ => active,
			};
			var shown = list.Take(25).ToList();
			if (shown.Count == 0)
			{
				await FollowupAsync(l.T("Rien ici.", "Nothing here."), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder().WithColor(Amber).WithTitle(l.T("Commandes et demandes", "Orders and requests"));
			foreach (var order in shown)
			{
				var offers = order.Offers.Count(f => !f.Withdrawn);
				embed.AddField(
					$"#{order.Id} · {order.Quantity}× {order.Item} ({CraftOrderService.Label(order.Status, l)})",
					(order.CrafterCharacter is null
						? l.T($"demande de **{order.RequesterCharacter}**, ouverte à tous", $"request by **{order.RequesterCharacter}**, open to all")
						: l.T($"de **{order.RequesterCharacter}** pour **{order.CrafterCharacter}**", $"from **{order.RequesterCharacter}** to **{order.CrafterCharacter}**")) +
					(order.Status == CraftOrderStatus.Open && offers > 0 ? l.T($" · 🙋 {offers} offre(s)", $" · 🙋 {offers} offer(s)") : "") +
					(order.Status == CraftOrderStatus.Open && order.ExpiresUtc is { } expires ? $" · ⏳ <t:{new DateTimeOffset(expires, TimeSpan.Zero).ToUnixTimeSeconds()}:R>" : "") +
					(string.IsNullOrEmpty(order.Note) ? "" : $"\n_{order.Note}_"));
			}
			embed.WithFooter(l.T("Détail et offres : /gkeep commande voir", "Details and offers: /gkeep order show"));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("show", "An order's details and the crafters' offers")]
		public async Task ShowAsync([Summary("id", "The order"), Autocomplete(typeof(OrderAutocompleteHandler))] int id)
		{
			await DeferAsync(ephemeral: true);
			var order = await orders.GetAsync(id);
			if (order is null)
			{
				await FollowupAsync(l.T($"Commande #{id} introuvable.", $"Order #{id} not found."), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder().WithColor(Amber)
				.WithTitle($"#{order.Id} · {order.Quantity}× {order.Item}")
				.AddField(l.T("État", "Status"), CraftOrderService.Label(order.Status, l), true)
				.AddField(l.T("Demandeur", "Requester"), order.RequesterCharacter, true)
				.AddField(l.T("Artisan", "Crafter"), order.CrafterCharacter ?? l.T("à choisir", "to be chosen"), true);
			if (!string.IsNullOrEmpty(order.Note))
			{
				embed.WithDescription($"_{order.Note}_");
			}
			var offers = order.Offers.Where(f => !f.Withdrawn).Select(f => $"🙋 **{f.Crafter}**" + (f.Note is null ? "" : $" — {f.Note}")).ToList();
			if (offers.Count > 0)
			{
				embed.AddField(l.T("Offres", "Offers"), string.Join("\n", offers));
			}
			embed.WithFooter(order.Status switch
			{
				CraftOrderStatus.Open => l.T("Proposer : /gkeep commande proposer · choisir une offre : /gkeep commande accepter", "Offer: /gkeep order offer · pick an offer: /gkeep order accept"),
				CraftOrderStatus.Accepted or CraftOrderStatus.Ready => l.T("Artisan : prete ou envoyee · demandeur : recu", "Crafter: ready or mailed · requester: received"),
				CraftOrderStatus.Mailed => l.T("Demandeur : /gkeep commande recu", "Requester: /gkeep order received"),
				_ => "",
			});
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("offer", "Offer to make an open request (crafter)")]
		public async Task OfferAsync(
			[Summary("id", "The request"), Autocomplete(typeof(OrderAutocompleteHandler))] int id,
			[Summary("note", "A word for the requester (price, delay...)")] string? note = null,
			[Summary("character", "Your crafting character (default: your main)"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))] string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await orders.OfferFromDiscordAsync(DiscordId, id, false, note, character);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("withdraw", "Withdraw your offer")]
		public async Task WithdrawAsync(
			[Summary("id", "The request"), Autocomplete(typeof(OrderAutocompleteHandler))] int id,
			[Summary("character", "Your crafting character (default: your main)"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))] string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await orders.OfferFromDiscordAsync(DiscordId, id, true, null, character);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("accept", "Accept an order (crafter), or pick a crafter's offer (requester)")]
		public async Task AcceptAsync(
			[Summary("id", "The order"), Autocomplete(typeof(OrderAutocompleteHandler))] int id,
			[Summary("crafter", "Requester: the crafter whose offer you pick"), Autocomplete(typeof(OfferAutocompleteHandler))] string? crafter = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await orders.UpdateStatusFromDiscordAsync(DiscordId, id, CraftOrderStatus.Accepted, chosenCrafter: crafter);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("ready", "Crafted and ready to trade in person (crafter)")]
		public Task ReadyAsync([Summary("id", "The order"), Autocomplete(typeof(OrderAutocompleteHandler))] int id) =>
			UpdateAsync(id, CraftOrderStatus.Ready);

		[SlashCommand("mailed", "Sent by mail (crafter)")]
		public Task MailedAsync([Summary("id", "The order"), Autocomplete(typeof(OrderAutocompleteHandler))] int id) =>
			UpdateAsync(id, CraftOrderStatus.Mailed);

		[SlashCommand("received", "Confirm you received the item (requester)")]
		public Task ReceivedAsync([Summary("id", "The order"), Autocomplete(typeof(OrderAutocompleteHandler))] int id) =>
			UpdateAsync(id, CraftOrderStatus.Done);

		[SlashCommand("cancel", "Cancel an order (crafter or requester)")]
		public Task CancelAsync([Summary("id", "The order"), Autocomplete(typeof(OrderAutocompleteHandler))] int id) =>
			UpdateAsync(id, CraftOrderStatus.Cancelled);

		[SlashCommand("catalog", "Search the guild's recipe catalog, with who can make each recipe")]
		public async Task CatalogAsync(
			[Summary("search", "Text to find in recipe names")] string search,
			[Summary("profession", "Only this profession"), Autocomplete(typeof(ProfessionAutocompleteHandler))] string? profession = null)
		{
			await DeferAsync(ephemeral: true);
			var hits = await catalog.SearchAsync(search, profession);
			if (hits.Count == 0)
			{
				await FollowupAsync(l.T(
					"Rien dans le catalogue. Il se remplit quand les membres ouvrent leurs fenêtres de métier en jeu.",
					"Nothing in the catalog. It fills up as members open their profession windows in game."), ephemeral: true);
				return;
			}
			var lines = hits.Select(h => $"**{h.Name}** · {h.Profession} — " +
				(h.Crafters.Count > 0 ? string.Join(", ", h.Crafters.Take(5)) + (h.Crafters.Count > 5 ? "…" : "") : l.T("_personne ne la connaît encore_", "_nobody knows it yet_")));
			await FollowupAsync(embed: new EmbedBuilder().WithColor(Amber)
				.WithTitle(l.T($"Catalogue · « {search} »", $"Catalog · \"{search}\""))
				.WithDescription(Clip(string.Join("\n", lines), 4000))
				.WithFooter(l.T("Demander : /gkeep commande nouvelle", "Ask for one: /gkeep order new")).Build(), ephemeral: true);
		}

		private async Task UpdateAsync(int id, CraftOrderStatus status)
		{
			await DeferAsync(ephemeral: true);
			var result = await orders.UpdateStatusFromDiscordAsync(DiscordId, id, status);
			await FollowupAsync(result.Message, ephemeral: true);
		}
	}
}
