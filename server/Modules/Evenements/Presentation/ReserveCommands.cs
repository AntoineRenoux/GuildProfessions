using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Evenements.Application;
using GuildProfessions.Server.Modules.Evenements.Presentation;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

// Commandes Discord du module Événements : /gkeep reserve (réservations de butin).
// Le groupe reste imbriqué dans GkeepModule (Discord ne connaît les sous-commandes que
// par imbrication) et garde son namespace : même hiérarchie /gkeep <groupe> <commande>,
// mêmes clés de traduction dans commands.fr.json. Il ne dépend que de son module.
public sealed partial class GkeepModule
{
	// ---- Réservations de butin --------------------------------------------------------------

	[Group("reserve", "Loot reserves for an event: high, normal and low priority")]
	[RequireRegisteredGuild]
	public sealed class ReserveGroup(EventService events, BotLocalizer l)
		: GuildkeepGroup
	{
		[SlashCommand("set", "Reserve an item for an event (one per priority)")]
		public async Task SetAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event,
			[Summary("priority", "Reserve priority")]
			[Choice("High / Haute", "high"), Choice("Normal / Normale", "normal"), Choice("Low / Basse", "low")]
			string priority,
			[Summary("item", "Item number (e.g. 19019), a link containing item=19019, or a suggestion"), Autocomplete(typeof(ItemAutocompleteHandler))]
			string item,
			[Summary("character", "For another player (organizer or officer); default: your main"), Autocomplete(typeof(SignupAutocompleteHandler))]
			string? character = null)
		{
			await DeferAsync(ephemeral: true);
			if (EventService.ParseItemId(item) is not { } itemId)
			{
				await FollowupAsync(l.T(
					$"Objet illisible (« {item} ») : donne son numéro, par exemple `19019`, ou choisis une suggestion.",
					$"Unreadable item (\"{item}\"): give its number, e.g. `19019`, or pick a suggestion."), ephemeral: true);
				return;
			}
			var result = await events.ReserveFromDiscordAsync(DiscordId, IsServerAdmin, @event, priority, itemId, null, character);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("clear", "Remove one of your reserves")]
		public async Task ClearAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event,
			[Summary("priority", "Reserve priority")]
			[Choice("High / Haute", "high"), Choice("Normal / Normale", "normal"), Choice("Low / Basse", "low")]
			string priority,
			[Summary("character", "For another player (organizer or officer); default: your main"), Autocomplete(typeof(SignupAutocompleteHandler))]
			string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await events.ReserveFromDiscordAsync(DiscordId, IsServerAdmin, @event, priority, 0, null, character);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("list", "All reserves of an event, item by item, with conflicts")]
		public async Task ListAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event)
		{
			await DeferAsync(ephemeral: true);
			var e = await events.GetAsync(@event);
			if (e is null)
			{
				await FollowupAsync(l.T($"Événement #{@event} introuvable.", $"Event #{@event} not found."), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder()
				.WithColor(Green)
				.WithTitle(l.T($"Réservations · {e.Title}", $"Reserves · {e.Title}") + (e.ReservesLocked ? " 🔒" : ""));
			var hard = e.HardReserves.Where(h => h.Active).ToList();
			if (hard.Count > 0)
			{
				embed.AddField(l.T("Réservés d'office", "Hard reserves"), string.Join("\n", hard.Select(h =>
					$"**{h.ItemName ?? $"#{h.ItemId}"}**" + (h.Holder is null ? "" : l.T($" → {h.Holder}", $" → {h.Holder}")))));
			}
			var groups = EventService.EffectiveReserves(e).GroupBy(r => r.ItemId).ToList();
			foreach (var group in groups.Take(20))
			{
				var name = group.First(r => true).ItemName ?? $"#{group.Key}";
				var who = string.Join(", ", group.Select(r => $"{r.Character} ({EventLabels.PriorityLabel(r.Priority, l)})"));
				embed.AddField((group.Count() > 1 ? "⚠️ " : "") + $"{name} · #{group.Key}", who.Length <= 1024 ? who : who[..1021] + "...");
			}
			if (groups.Count > 20)
			{
				embed.WithFooter(l.T($"… et {groups.Count - 20} autres objets (détail complet en jeu)", $"… and {groups.Count - 20} more items (full list in game)"));
			}
			if (groups.Count == 0 && hard.Count == 0)
			{
				embed.WithDescription(l.T("Aucune réservation pour l'instant : `/gkeep reserve poser`.", "No reserves yet: `/gkeep reserve set`."));
			}
			else if (e.ReservesLocked)
			{
				embed.WithDescription(l.T("Verrouillées : seules comptent les réservations des joueurs confirmés.", "Locked: only confirmed players' reserves count."));
			}
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("hard", "Hard-reserve an item: players can no longer reserve it (organizer or officer)")]
		public async Task HardAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event,
			[Summary("item", "Item number (e.g. 19019) or a suggestion"), Autocomplete(typeof(ItemAutocompleteHandler))] string item,
			[Summary("holder", "Who gets it (optional)"), Autocomplete(typeof(SignupAutocompleteHandler))] string? holder = null)
		{
			await DeferAsync(ephemeral: true);
			if (EventService.ParseItemId(item) is not { } itemId)
			{
				await FollowupAsync(l.T($"Objet illisible (« {item} »).", $"Unreadable item (\"{item}\")."), ephemeral: true);
				return;
			}
			var result = await events.HardReserveFromDiscordAsync(DiscordId, IsServerAdmin, @event, itemId, null, holder, true);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("unhard", "Lift a hard reserve (organizer or officer)")]
		public async Task UnhardAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event,
			[Summary("item", "Item number or a suggestion"), Autocomplete(typeof(ItemAutocompleteHandler))] string item)
		{
			await DeferAsync(ephemeral: true);
			if (EventService.ParseItemId(item) is not { } itemId)
			{
				await FollowupAsync(l.T($"Objet illisible (« {item} »).", $"Unreadable item (\"{item}\")."), ephemeral: true);
				return;
			}
			var result = await events.HardReserveFromDiscordAsync(DiscordId, IsServerAdmin, @event, itemId, null, null, false);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("lock", "Lock reserves: only confirmed players keep theirs (organizer or officer)")]
		public Task LockAsync([Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event) =>
			SetLockedAsync(@event, true);

		[SlashCommand("unlock", "Unlock reserves (organizer or officer)")]
		public Task UnlockAsync([Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event) =>
			SetLockedAsync(@event, false);

		private async Task SetLockedAsync(int eventId, bool locked)
		{
			await DeferAsync(ephemeral: true);
			var result = await events.EditFromDiscordAsync(DiscordId, IsServerAdmin, eventId, new EventService.EventFields(Locked: locked));
			await FollowupAsync(result.IsSuccess
				? (locked
					? l.T("🔒 Réservations verrouillées : seules comptent celles des confirmés. Le maître du butin se règle en jeu.",
						"🔒 Reserves locked: only confirmed players' reserves count. Set master looter in game.")
					: l.T("🔓 Réservations déverrouillées.", "🔓 Reserves unlocked."))
				: result.Message, ephemeral: true);
		}
	}
}
