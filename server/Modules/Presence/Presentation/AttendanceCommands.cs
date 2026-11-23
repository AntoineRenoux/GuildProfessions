using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Presence.Application;
using GuildProfessions.Server.Modules.Presence.Presentation;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

// Commandes Discord du module Présence : /gkeep-raid attendance (présence observée) ; /gkeep-raid loot est dans LootCommands.cs.
// Le groupe reste imbriqué dans GkeepModule (Discord ne connaît les sous-commandes que
// par imbrication) et garde son namespace : même hiérarchie /gkeep <groupe> <commande>,
// mêmes clés de traduction dans commands.fr.json. Il ne dépend que de son module.
public sealed partial class GkeepRaidModule
{
	// ---- Présence et butin ------------------------------------------------------------------

	[Group("attendance", "Raid attendance actually observed in game (sign-ups don't count)")]
	[RequireRegisteredGuild]
	public sealed class AttendanceGroup(AttendanceService attendance, BotLocalizer l)
		: GuildkeepGroup
	{
		[SlashCommand("summary", "Attendance rate of every raider over 30, 60 or 90 days")]
		public async Task SummaryAsync(
			[Summary("days", "Period")] [Choice("30 days / 30 jours", 30), Choice("60 days / 60 jours", 60), Choice("90 days / 90 jours", 90)] int days = 30)
		{
			await DeferAsync(ephemeral: true);
			var sessions = await attendance.RecentAsync(AttendanceService.HistoryWindow);
			var stats = AttendanceStats.Summary(sessions, DateTimeOffset.UtcNow, days);
			if (stats.Count == 0)
			{
				await FollowupAsync(NoRaids(), ephemeral: true);
				return;
			}
			var lines = stats.Take(40).Select(p =>
			{
				var rate = AttendanceStats.Pick(p, days);
				return $"`{AttendanceStats.Percent(rate),4}` **{p.Character}** ({rate.Attended}/{rate.Eligible}){Trend(p)}";
			});
			var raids = sessions.Count(s => AttendanceStats.IsGuildRaid(s) && s.StartedAt >= DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds());
			var embed = new EmbedBuilder()
				.WithColor(Green)
				.WithTitle(l.T($"Présence sur {days} jours · {raids} raid(s)", $"Attendance over {days} days · {raids} raid(s)"))
				.WithDescription(Clip(string.Join("\n", lines), 4000))
				.WithFooter(l.T("Flèche : 30 derniers jours comparés aux 30 d'avant · détail : /gkeep-raid presence joueur",
					"Arrow: last 30 days compared with the 30 before · details: /gkeep-raid attendance player"));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("player", "A player's attendance trends, recent raids and loot")]
		public async Task PlayerAsync(
			[Summary("character", "The character"), Autocomplete(typeof(CharacterAutocompleteHandler))] string character)
		{
			await DeferAsync(ephemeral: true);
			var sessions = await attendance.RecentAsync(AttendanceService.HistoryWindow);
			var name = sessions.SelectMany(s => s.Participants).Select(p => p.Character)
				.FirstOrDefault(n => string.Equals(n, character.Trim(), StringComparison.OrdinalIgnoreCase)) ?? character.Trim();
			var stats = AttendanceStats.StatsFor(sessions, name, DateTimeOffset.UtcNow);
			var embed = new EmbedBuilder()
				.WithColor(Green)
				.WithTitle(l.T($"Présence · {name}", $"Attendance · {name}"))
				.AddField("30 j / 30 d", $"{AttendanceStats.Percent(stats.Last30)} ({stats.Last30.Attended}/{stats.Last30.Eligible}){Trend(stats)}", true)
				.AddField("60 j / 60 d", $"{AttendanceStats.Percent(stats.Last60)} ({stats.Last60.Attended}/{stats.Last60.Eligible})", true)
				.AddField("90 j / 90 d", $"{AttendanceStats.Percent(stats.Last90)} ({stats.Last90.Attended}/{stats.Last90.Eligible})", true);
			var recent = sessions.Where(AttendanceStats.IsGuildRaid).Take(10)
				.Select(s => $"{Mark(AttendanceStats.Attended(s, name))} #{s.Id} · {s.Day} · {s.Instance}");
			embed.AddField(l.T("Derniers raids", "Recent raids"), Clip(string.Join("\n", recent), 1024) is { Length: > 0 } r ? r : "—");
			var loot = sessions.SelectMany(s => s.Loot.Where(x => x.Character == name).Select(x => (s, x))).Take(15)
				.Select(x => $"{(x.x.Voided ? "~~" : "")}{x.x.ItemName ?? $"#{x.x.ItemId}"}{(x.x.Voided ? "~~" : "")} · {x.s.Day}");
			embed.AddField(l.T($"Butin (90 j) : {stats.Loot90}", $"Loot (90 d): {stats.Loot90}"), Clip(string.Join("\n", loot), 1024) is { Length: > 0 } t ? t : "—");
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("raids", "Raids observed in game, most recent first")]
		public async Task RaidsAsync()
		{
			await DeferAsync(ephemeral: true);
			var sessions = await attendance.RecentAsync(AttendanceService.HistoryWindow);
			if (sessions.Count == 0)
			{
				await FollowupAsync(NoRaids(), ephemeral: true);
				return;
			}
			var lines = sessions.Take(25).Select(s =>
				$"**#{s.Id}** · {s.Day} · {s.Instance} · {s.Participants.Count(p => p.Minutes >= AttendanceStats.MinMinutes)} " +
				l.T("présents", "present") + $" · {s.Loot.Count(x => !x.Voided)} " + l.T("objets", "items") +
				(AttendanceStats.IsGuildRaid(s) ? "" : l.T(" · _hors décompte_", " · _not counted_")));
			await FollowupAsync(embed: new EmbedBuilder().WithColor(Green)
				.WithTitle(l.T("Raids relevés", "Observed raids"))
				.WithDescription(Clip(string.Join("\n", lines), 4000))
				.WithFooter(l.T("Détail : /gkeep-raid presence raid", "Details: /gkeep-raid attendance raid")).Build(), ephemeral: true);
		}

		[SlashCommand("raid", "Who was there and what dropped")]
		public async Task RaidAsync([Summary("raid", "The raid"), Autocomplete(typeof(RaidAutocompleteHandler))] int raid)
		{
			await DeferAsync(ephemeral: true);
			var s = await attendance.GetAsync(raid);
			if (s is null)
			{
				await FollowupAsync(l.T($"Raid #{raid} introuvable.", $"Raid #{raid} not found."), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder().WithColor(Green)
				.WithTitle($"#{s.Id} · {s.Instance} · {s.Day}")
				.WithDescription(l.T($"Chef de raid : {s.Leader ?? "?"} · <t:{s.StartedAt}:t> → <t:{s.EndedAt}:t>",
					$"Raid lead: {s.Leader ?? "?"} · <t:{s.StartedAt}:t> → <t:{s.EndedAt}:t>"));
			var names = s.Participants.Select(p => p.Character).Concat(s.Corrections.Select(c => c.Character)).Distinct().OrderBy(n => n);
			var who = names.Select(n =>
			{
				var minutes = s.Participants.SingleOrDefault(p => p.Character == n)?.Minutes ?? 0;
				var correction = s.Corrections.SingleOrDefault(c => c.Character == n);
				return $"{Mark(AttendanceStats.Attended(s, n))} {n} ({minutes} min)" +
					(correction is null ? "" : $" — _{AttendanceService.StatusLabel(correction.Status, l)}{(correction.Note is null ? "" : $" : {correction.Note}")}_");
			});
			embed.AddField(l.T("Présence", "Attendance"), Clip(string.Join("\n", who), 1024) is { Length: > 0 } w ? w : "—");
			var loot = s.Loot.OrderBy(x => x.At).Select(x =>
				$"{(x.Voided ? "~~" : "")}#{x.Id} {x.ItemName ?? $"objet {x.ItemId}"} → {x.Character}{(x.Voided ? "~~" : "")}" +
				(x.Voided && x.VoidNote is not null ? $" _({x.VoidNote})_" : ""));
			embed.AddField(l.T("Butin", "Loot"), Clip(string.Join("\n", loot), 1024) is { Length: > 0 } t ? t : "—");
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("correct", "Correct a player's attendance on a raid (officer)")]
		public async Task CorrectAsync(
			[Summary("raid", "The raid"), Autocomplete(typeof(RaidAutocompleteHandler))] int raid,
			[Summary("character", "The character"), Autocomplete(typeof(CharacterAutocompleteHandler))] string character,
			[Summary("status", "What really happened")]
			[Choice("Present / Présent", "present"), Choice("Absent", "absent"), Choice("Excused (not counted) / Excusé (hors calcul)", "excused")]
			string status,
			[Summary("note", "Why (optional)")] string? note = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await attendance.CorrectFromDiscordAsync(DiscordId, IsServerAdmin, raid, character, status, note);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		private string NoRaids() => l.T(
			"Aucun raid relevé pour l'instant : l'addon note la présence quand au moins 5 membres de la guilde sont ensemble dans un raid.",
			"No raid observed yet: the addon records attendance when at least 5 guild members are together in a raid.");

		private static string Mark(bool? attended) => attended switch { true => "✅", false => "❌", null => "➖" };

		internal static string Trend(AttendanceStats.PlayerStats p)
		{
			if (p.Last30.Percent is not { } now || p.Previous30.Percent is not { } before)
			{
				return "";
			}
			var delta = now - before;
			return Math.Abs(delta) < 5 ? " →" : delta > 0 ? $" ↗ +{delta:0}" : $" ↘ {delta:0}";
		}
	}
}
