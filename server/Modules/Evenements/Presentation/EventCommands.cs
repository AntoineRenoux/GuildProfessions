using GuildProfessions.Server.Modules.Annuaire.Contracts;
using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Evenements.Application;
using GuildProfessions.Server.Modules.Evenements.Presentation;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

// Commandes Discord du module Événements : /gkeep-raid event (événements, roster, soirée) ; /gkeep-raid reserve est dans ReserveCommands.cs.
// Le groupe reste imbriqué dans GkeepModule (Discord ne connaît les sous-commandes que
// par imbrication) et garde son namespace : même hiérarchie /gkeep <groupe> <commande>,
// mêmes clés de traduction dans commands.fr.json. Il ne dépend que de son module.
public sealed partial class GkeepRaidModule
{
	// ---- Événements -------------------------------------------------------------------------

	[Group("event", "Guild events: raids, dungeons and sign-ups")]
	[RequireRegisteredGuild]
	public sealed class EventGroup(EventService events, GuildContext guildContext, IAnnuaire annuaire, BotLocalizer l)
		: GuildkeepGroup
	{
		[SlashCommand("list", "Upcoming guild events and who signed up")]
		public async Task ListAsync()
		{
			await DeferAsync(ephemeral: true);
			var list = await events.UpcomingAsync();
			if (list.Count == 0)
			{
				await FollowupAsync(l.T(
					"Aucun événement à venir. Crées-en un avec `/gkeep-raid evenement creer` ou en jeu (`/gkeep events`).",
					"No upcoming events. Create one with `/gkeep-raid event create` or in game (`/gkeep events`)."), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder().WithColor(Green).WithTitle(l.T("Événements à venir", "Upcoming events"));
			foreach (var e in list)
			{
				embed.AddField($"#{e.Id} · {e.Title}", Describe(e, l));
			}
			embed.WithFooter(l.T("Détail et roster : /gkeep-raid evenement voir · s'inscrire : /gkeep-raid evenement inscription",
				"Details and roster: /gkeep-raid event show · sign up: /gkeep-raid event signup"));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("show", "An event's details, composition and roster")]
		public async Task ShowAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event)
		{
			await DeferAsync(ephemeral: true);
			var e = await events.GetAsync(@event);
			if (e is null)
			{
				await FollowupAsync(l.T($"Événement #{@event} introuvable.", $"Event #{@event} not found."), ephemeral: true);
				return;
			}
			await FollowupAsync(embed: Roster(e, l).Build(), ephemeral: true);
		}

		[SlashCommand("signup", "Answer an event with one of your characters")]
		public async Task SignupAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event,
			[Summary("response", "Your answer")]
			[Choice("Attending / Présent", "yes"), Choice("Tentative / Peut-être", "tentative"), Choice("Not attending / Absent", "no")]
			string response,
			[Summary("role", "Role you will play (default: your previous one)")]
			[Choice("Tank", "tank"), Choice("Healer / Soigneur", "healer"), Choice("Melee DPS / DPS mêlée", "melee"), Choice("Ranged DPS / DPS distance", "ranged")]
			string? role = null,
			[Summary("character", "One of your linked characters (default: your main)"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))]
			string? character = null,
			[Summary("note", "A word for the organizer")] string? note = null,
			[Summary("spec", "Your specialization (e.g. Protection)")] string? spec = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await events.RespondFromDiscordAsync(DiscordId, @event, response, role, character, note, spec);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("create", "Create a guild event (raid, dungeon, PvP...)")]
		public async Task CreateAsync(
			[Summary("title", "Event name"), MaxLength(EventService.TitleMax)] string title,
			[Summary("date", "Day: 29/11, 29/11/2026 or 2026-11-29")] string date,
			[Summary("time", "Start time: 20:45 or 20h45")] string time,
			[Summary("kind", "Kind of event")]
			[Choice("Raid", "raid"), Choice("Dungeon / Donjon", "dungeon"), Choice("PvP / JcJ", "pvp"), Choice("Other / Autre", "other")]
			string kind = "raid",
			[Summary("tanks", "Target number of tanks"), MinValue(0), MaxValue(EventService.TargetMax)] int tanks = 0,
			[Summary("healers", "Target number of healers"), MinValue(0), MaxValue(EventService.TargetMax)] int healers = 0,
			[Summary("melee", "Target number of melee DPS"), MinValue(0), MaxValue(EventService.TargetMax)] int melee = 0,
			[Summary("ranged", "Target number of ranged DPS"), MinValue(0), MaxValue(EventService.TargetMax)] int ranged = 0,
			[Summary("details", "Details (consumables, meeting point...)"), MaxLength(EventService.DescriptionMax)] string? details = null,
			[Summary("organizer", "Organizing character (default: your main)"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))]
			string? organizer = null,
			[Summary("timezone", "Time zone of the date and time (default: the guild's)")]
			[Choice("Paris / Bruxelles", "Europe/Paris"), Choice("London", "Europe/London"), Choice("New York", "America/New_York"),
				Choice("Chicago", "America/Chicago"), Choice("Denver", "America/Denver"), Choice("Los Angeles", "America/Los_Angeles"),
				Choice("Sydney", "Australia/Sydney"), Choice("UTC", "UTC")]
			string? timezone = null)
		{
			await DeferAsync(ephemeral: true);
			var zone = timezone ?? EventSchedule.DefaultTimeZone(guildContext.Guild?.Locale);
			if (!EventSchedule.TryParseStart(date, time, zone, DateTimeOffset.UtcNow, out var startsAt))
			{
				await FollowupAsync(BadDate(date, time), ephemeral: true);
				return;
			}
			var result = await events.CreateFromDiscordAsync(DiscordId, IsServerAdmin,
				new EventService.EventFields(title, kind, startsAt, details, tanks, healers, melee, ranged), organizer);
			await FollowupAsync(result.Message + (result.IsSuccess ? ZoneNote(zone) : ""), ephemeral: true);
		}

		[SlashCommand("edit", "Change an event (organizer or officer)")]
		public async Task EditAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event,
			[Summary("title", "New name"), MaxLength(EventService.TitleMax)] string? title = null,
			[Summary("date", "New day: 29/11, 29/11/2026 or 2026-11-29")] string? date = null,
			[Summary("time", "New start time: 20:45 or 20h45")] string? time = null,
			[Summary("kind", "Kind of event")]
			[Choice("Raid", "raid"), Choice("Dungeon / Donjon", "dungeon"), Choice("PvP / JcJ", "pvp"), Choice("Other / Autre", "other")]
			string? kind = null,
			[Summary("tanks", "Target number of tanks"), MinValue(0), MaxValue(EventService.TargetMax)] int? tanks = null,
			[Summary("healers", "Target number of healers"), MinValue(0), MaxValue(EventService.TargetMax)] int? healers = null,
			[Summary("melee", "Target number of melee DPS"), MinValue(0), MaxValue(EventService.TargetMax)] int? melee = null,
			[Summary("ranged", "Target number of ranged DPS"), MinValue(0), MaxValue(EventService.TargetMax)] int? ranged = null,
			[Summary("details", "New details (a single - clears them)"), MaxLength(EventService.DescriptionMax)] string? details = null,
			[Summary("timezone", "Time zone of the date and time (default: the guild's)")]
			[Choice("Paris / Bruxelles", "Europe/Paris"), Choice("London", "Europe/London"), Choice("New York", "America/New_York"),
				Choice("Chicago", "America/Chicago"), Choice("Denver", "America/Denver"), Choice("Los Angeles", "America/Los_Angeles"),
				Choice("Sydney", "Australia/Sydney"), Choice("UTC", "UTC")]
			string? timezone = null)
		{
			await DeferAsync(ephemeral: true);
			var zone = timezone ?? EventSchedule.DefaultTimeZone(guildContext.Guild?.Locale);
			long? startsAt = null;
			if (date is not null || time is not null)
			{
				var existing = await events.GetAsync(@event);
				if (existing is null)
				{
					await FollowupAsync(l.T($"Événement #{@event} introuvable.", $"Event #{@event} not found."), ephemeral: true);
					return;
				}
				// Seule la date ou seule l'heure change : l'autre reste celle de l'événement.
				var current = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeSeconds(existing.StartsAt), TimeZoneInfo.FindSystemTimeZoneById(zone));
				date ??= current.ToString("dd/MM/yyyy");
				time ??= current.ToString("HH:mm");
				if (!EventSchedule.TryParseStart(date, time, zone, DateTimeOffset.UtcNow, out var parsed))
				{
					await FollowupAsync(BadDate(date, time), ephemeral: true);
					return;
				}
				startsAt = parsed;
			}
			var result = await events.EditFromDiscordAsync(DiscordId, IsServerAdmin, @event,
				new EventService.EventFields(title, kind, startsAt, details, tanks, healers, melee, ranged));
			await FollowupAsync(result.Message + (result.IsSuccess && startsAt is not null ? ZoneNote(zone) : ""), ephemeral: true);
		}

		[SlashCommand("cancel", "Cancel an event (organizer or officer)")]
		public async Task CancelAsync([Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event)
		{
			await DeferAsync(ephemeral: true);
			var result = await events.SetCancelledFromDiscordAsync(DiscordId, IsServerAdmin, @event, true);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("restore", "Bring a cancelled event back (organizer or officer)")]
		public async Task RestoreAsync([Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event)
		{
			await DeferAsync(ephemeral: true);
			var result = await events.SetCancelledFromDiscordAsync(DiscordId, IsServerAdmin, @event, false);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("place", "Put a player in the roster: confirmed, standby, bench (organizer or officer)")]
		public async Task PlaceAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event,
			[Summary("character", "The player's character"), Autocomplete(typeof(SignupAutocompleteHandler))] string character,
			[Summary("placement", "Place in the roster")]
			[Choice("Confirmed / Confirmé", "confirmed"), Choice("Standby / Remplaçant", "standby"), Choice("Bench / Banc", "bench"), Choice("Remove / Retirer", "pending")]
			string placement)
		{
			await DeferAsync(ephemeral: true);
			var result = await events.PlaceFromDiscordAsync(DiscordId, IsServerAdmin, @event, character, placement);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("duty", "Give a support duty to a player, or free it (organizer or officer)")]
		public async Task DutyAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event,
			[Summary("duty", "The support duty")]
			[Choice("Raid lead / Chef de raid", "lead"), Choice("Summoner / Invocateur", "summoner"),
				Choice("Consumables / Consommables", "consumables"), Choice("Repair bot / Robot de réparation", "repair")]
			string duty,
			[Summary("character", "Who takes it (leave empty to free it)"), Autocomplete(typeof(SignupAutocompleteHandler))]
			string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await events.AssignDutyFromDiscordAsync(DiscordId, IsServerAdmin, @event, duty, character);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("checklist", "What is still missing before the raid")]
		public async Task ChecklistAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event)
		{
			await DeferAsync(ephemeral: true);
			var e = await events.GetAsync(@event);
			if (e is null)
			{
				await FollowupAsync(l.T($"Événement #{@event} introuvable.", $"Event #{@event} not found."), ephemeral: true);
				return;
			}
			var items = EventService.Checklist(e, l);
			var embed = new EmbedBuilder()
				.WithColor(items.Any(i => i.Blocking) ? Amber : Green)
				.WithTitle(l.T($"Avant le raid · {e.Title}", $"Before the raid · {e.Title}"))
				.WithDescription(items.Count == 0
					? l.T("✅ Tout est prêt.", "✅ Everything is ready.")
					: string.Join("\n", items.Select(i => (i.Blocking ? "⛔ " : "⚠️ ") + i.Text)));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("remind", "Remind signed-up players in this channel (organizer or officer)")]
		public async Task RemindAsync(
			[Summary("event", "The event"), Autocomplete(typeof(EventAutocompleteHandler))] int @event,
			[Summary("message", "A word to add (optional)")] string? message = null)
		{
			await DeferAsync(ephemeral: true);
			var (result, targets) = await events.ReminderTargetsAsync(DiscordId, IsServerAdmin, @event);
			if (targets is null)
			{
				await FollowupAsync(result.Message, ephemeral: true);
				return;
			}
			// Les inscrits liés à Discord sont mentionnés, les autres cités par leur nom.
			var names = new List<string>();
			foreach (var character in targets.Characters)
			{
				var discordId = await annuaire.DiscordIdOfCharacterAsync(character);
				names.Add(discordId is null ? character : $"<@{discordId}>");
			}
			var e = targets.Event;
			var text = l.T(
				$"⏰ **{e.Title}** — <t:{e.StartsAt}:F> (<t:{e.StartsAt}:R>)\nInscrits : {string.Join(", ", names.Distinct())}",
				$"⏰ **{e.Title}** — <t:{e.StartsAt}:F> (<t:{e.StartsAt}:R>)\nSigned up: {string.Join(", ", names.Distinct())}") +
				(string.IsNullOrWhiteSpace(message) ? "" : $"\n> {message.Trim()}");
			await Context.Channel.SendMessageAsync(Text.Ellipsize(text, 2000), allowedMentions: AllowedMentions.All);
			await FollowupAsync(l.T($"Rappel envoyé à {targets.Characters.Count} inscrit(s).", $"Reminder sent to {targets.Characters.Count} player(s)."), ephemeral: true);
		}

		private string BadDate(string date, string time) => l.T(
			$"Date ou heure illisible (« {date} », « {time} »). Exemples : `29/11` et `20:45`, ou `2026-11-29` et `20h45`.",
			$"Unreadable date or time (\"{date}\", \"{time}\"). Examples: `29/11` and `20:45`, or `2026-11-29` and `20h45`.");

		private string ZoneNote(string zone) => l.T(
			$"\n_Heure lue dans le fuseau {zone} ; l'heure ci-dessus s'affiche dans le tien. Autre fuseau : option `fuseau`._",
			$"\n_Time read in the {zone} time zone; the time above shows in yours. Another zone: the `timezone` option._");

		/// <summary>Détail complet : composition par rôle puis roster par place (l'équivalent de « Copier le roster »).</summary>
		internal static EmbedBuilder Roster(GuildEvent e, BotLocalizer l)
		{
			var embed = new EmbedBuilder()
				.WithColor(e.Cancelled ? Amber : Green)
				.WithTitle($"#{e.Id} · {e.Title}" + (e.Cancelled ? l.T(" (annulé)", " (cancelled)") : ""))
				.WithDescription(Describe(e, l));
			string Line(EventSignup s) =>
				$"{RoleEmoji(s.Role)} {s.Character}" + (s.Spec is null ? "" : $" ({s.Spec})") + (s.Note is null ? "" : $" — _{s.Note}_");
			void Section(string title, IEnumerable<EventSignup> rows)
			{
				var list = rows.OrderBy(s => s.Role).ThenBy(s => s.Character).ToList();
				if (list.Count == 0)
				{
					return;
				}
				var text = string.Join("\n", list.Select(Line));
				embed.AddField($"{title} ({list.Count})", text.Length <= 1024 ? text : text[..1021] + "...");
			}
			Section(l.T("Confirmés", "Confirmed"), e.Signups.Where(s => s.Placement == "confirmed" && s.Response != "no"));
			Section(l.T("Remplaçants", "Standby"), e.Signups.Where(s => s.Placement == "standby" && s.Response != "no"));
			Section(l.T("Banc", "Bench"), e.Signups.Where(s => s.Placement == "bench" && s.Response != "no"));
			Section(l.T("Présents, à placer", "Attending, not placed"), e.Signups.Where(s => s.Placement == "pending" && s.Response == "yes"));
			Section(l.T("Peut-être", "Tentative"), e.Signups.Where(s => s.Placement == "pending" && s.Response == "tentative"));
			Section(l.T("Absents", "Not attending"), e.Signups.Where(s => s.Response == "no"));
			var duties = EventService.Duties
				.Select(d => (d, e.Duties.SingleOrDefault(x => x.Duty == d)?.Character))
				.Where(x => x.Character is not null)
				.Select(x => $"{EventLabels.DutyLabel(x.d, l)} : **{x.Character}**")
				.ToList();
			if (duties.Count > 0)
			{
				embed.AddField(l.T("Soutien", "Support"), string.Join("\n", duties));
			}
			var reserves = EventService.EffectiveReserves(e);
			if (reserves.Count > 0 || e.HardReserves.Any(h => h.Active))
			{
				embed.AddField(l.T("Réservations", "Reserves"), l.T(
					$"{reserves.Count} réservation(s){(e.ReservesLocked ? " · 🔒 verrouillées" : "")} — détail : /gkeep-raid reserve liste",
					$"{reserves.Count} reserve(s){(e.ReservesLocked ? " · 🔒 locked" : "")} — details: /gkeep-raid reserve list"));
			}
			embed.WithFooter(l.T($"{EventLabels.KindLabel(e.Kind, l)} · placer : /gkeep-raid evenement placer · inviter le raid : en jeu",
				$"{EventLabels.KindLabel(e.Kind, l)} · place players: /gkeep-raid event place · invite the raid: in game"));
			return embed;
		}

		private static string RoleEmoji(string role) => role switch
		{
			"tank" => "🛡️",
			"healer" => "✚",
			"ranged" => "🏹",
			_ => "⚔️",
		};

		internal static string Describe(GuildEvent e, BotLocalizer l)
		{
			var counts = EventService.Roles.ToDictionary(r => r, r => e.Signups.Count(s => s.Role == r && s.Response == "yes"));
			string Count(string role, int target) =>
				target > 0 ? $"{counts[role]}/{target}" : counts[role].ToString();
			var lines = new List<string>
			{
				$"<t:{e.StartsAt}:F> (<t:{e.StartsAt}:R>) · " + l.T($"par {e.Organizer}", $"by {e.Organizer}"),
				$"🛡️ {Count("tank", e.TargetTank)} · ✚ {Count("healer", e.TargetHealer)} · ⚔️ {Count("melee", e.TargetMelee)} · 🏹 {Count("ranged", e.TargetRanged)}" +
					l.T($" · {e.Signups.Count(s => s.Response == "tentative")} peut-être", $" · {e.Signups.Count(s => s.Response == "tentative")} tentative"),
			};
			var confirmed = e.Signups.Where(s => s.Placement == "confirmed" && s.Response != "no").Select(s => s.Character).ToList();
			if (confirmed.Count > 0)
			{
				lines.Add(l.T("Confirmés : ", "Confirmed: ") + string.Join(", ", confirmed));
			}
			if (!string.IsNullOrEmpty(e.Description))
			{
				lines.Add($"_{e.Description}_");
			}
			var text = string.Join("\n", lines);
			return text.Length <= 1024 ? text : text[..1021] + "...";
		}
	}
}
