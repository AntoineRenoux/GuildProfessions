using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Presence.Application;
using GuildProfessions.Server.Modules.Presence.Presentation;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

// Commandes Discord du module Présence : /gkeep-raid loot (butin épique et mieux reçu en raid).
// Le groupe reste imbriqué dans GkeepModule (Discord ne connaît les sous-commandes que
// par imbrication) et garde son namespace : même hiérarchie /gkeep <groupe> <commande>,
// mêmes clés de traduction dans commands.fr.json. Il ne dépend que de son module.
public sealed partial class GkeepRaidModule
{
	// ---- Butin ------------------------------------------------------------------------------

	[Group("loot", "Epic and better loot received in raids")]
	[RequireRegisteredGuild]
	public sealed class LootGroup(AttendanceService attendance, BotLocalizer l)
		: GuildkeepGroup
	{
		[SlashCommand("list", "Loot history, for everyone or one player")]
		public async Task ListAsync(
			[Summary("character", "Only this character"), Autocomplete(typeof(CharacterAutocompleteHandler))] string? character = null,
			[Summary("days", "Period")] [Choice("30 days / 30 jours", 30), Choice("60 days / 60 jours", 60), Choice("90 days / 90 jours", 90)] int days = 30)
		{
			await DeferAsync(ephemeral: true);
			var sessions = await attendance.RecentAsync(TimeSpan.FromDays(days));
			var rows = sessions
				.SelectMany(s => s.Loot.Select(x => (s, x)))
				.Where(r => character is null || string.Equals(r.x.Character, character.Trim(), StringComparison.OrdinalIgnoreCase))
				.OrderByDescending(r => r.x.At)
				.Take(40)
				.Select(r => $"{(r.x.Voided ? "~~" : "")}#{r.x.Id} **{r.x.ItemName ?? $"objet {r.x.ItemId}"}** → {r.x.Character} · {r.s.Day} · {r.s.Instance}{(r.x.Voided ? "~~" : "")}")
				.ToList();
			await FollowupAsync(embed: new EmbedBuilder().WithColor(Green)
				.WithTitle(l.T($"Butin · {days} jours", $"Loot · {days} days") + (character is null ? "" : $" · {character}"))
				.WithDescription(rows.Count == 0 ? l.T("Aucun objet relevé.", "No loot recorded.") : Clip(string.Join("\n", rows), 4000))
				.Build(), ephemeral: true);
		}

		[SlashCommand("void", "Void a recorded item: disenchanted, mistake... (officer)")]
		public async Task VoidAsync(
			[Summary("item", "The recorded item"), Autocomplete(typeof(LootAutocompleteHandler))] int item,
			[Summary("note", "Why (optional)")] string? note = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await attendance.VoidLootFromDiscordAsync(DiscordId, IsServerAdmin, item, true, note);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("restore", "Restore a voided item (officer)")]
		public async Task RestoreAsync([Summary("item", "The recorded item"), Autocomplete(typeof(LootAutocompleteHandler))] int item)
		{
			await DeferAsync(ephemeral: true);
			var result = await attendance.VoidLootFromDiscordAsync(DiscordId, IsServerAdmin, item, false, null);
			await FollowupAsync(result.Message, ephemeral: true);
		}
	}
}
