using System.Text.RegularExpressions;
using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Shared;
using static GuildProfessions.Server.Shared.Text;
using static GuildProfessions.Server.Modules.Evenements.Application.EventLabels;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Evenements.Application;

/// <summary>
/// Soirée de raid : réservations de butin (une par priorité et par joueur),
/// réservations fermes, verrouillage, rôles de soutien et liste de contrôle.
/// Mêmes règles qu'en jeu et sur Discord :
/// - un joueur réserve pour lui tant que ce n'est pas verrouillé ;
/// - l'organisateur ou un officier réserve pour n'importe qui (joueur sans
///   addon), verrouillé ou non, et seul il pose les réservations fermes et
///   les rôles de soutien ;
/// - une fois verrouillé, seules comptent les réservations des confirmés.
/// </summary>
public sealed partial class EventService
{
	public static readonly string[] Priorities = ["high", "normal", "low"];
	public static readonly string[] Duties = ["lead", "summoner", "consumables", "repair"];

	private async Task<bool> ApplyRaidNightAsync(UploadPayload payload, IReadOnlySet<string> identity, IReadOnlySet<string> officers,
		List<string> warnings, CancellationToken ct)
	{
		var changed = false;
		foreach (var reserve in payload.Reserves ?? [])
		{
			var guildEvent = await FindEventAsync(reserve.EventId, reserve.EventClientId, ct);
			if (guildEvent is null || !identity.Contains(reserve.By))
			{
				continue;
			}
			var manager = CanManage(guildEvent, reserve.By, identity, officers);
			if (reserve.By != reserve.Character && !manager)
			{
				warnings.Add(L.T($"Réservation pour {reserve.Character} ignorée : seuls l'organisateur et les officiers réservent pour un autre.",
					$"Reserve for {reserve.Character} ignored: only the organizer and officers reserve for someone else."));
				continue;
			}
			if (guildEvent.ReservesLocked && !manager)
			{
				warnings.Add(L.T($"Réservations de « {guildEvent.Title} » verrouillées : changement de {reserve.Character} ignoré.",
					$"Reserves for \"{guildEvent.Title}\" are locked: change by {reserve.Character} ignored."));
				continue;
			}
			if (ReserveProblem(guildEvent, reserve.Character, reserve.Priority, reserve.ItemId) is { } problem)
			{
				warnings.Add(problem);
				continue;
			}
			changed |= ApplyReserve(guildEvent, reserve.Character, reserve.Priority, reserve.ItemId, reserve.ItemName, reserve.By, reserve.At);
		}

		foreach (var hard in payload.HardReserves ?? [])
		{
			var guildEvent = await FindEventAsync(hard.EventId, hard.EventClientId, ct);
			if (guildEvent is null || hard.ItemId <= 0)
			{
				continue;
			}
			if (!CanManage(guildEvent, hard.By, identity, officers))
			{
				warnings.Add(L.T($"Réservation ferme sur « {guildEvent.Title} » ignorée : réservée à l'organisateur et aux officiers.",
					$"Hard reserve on \"{guildEvent.Title}\" ignored: only the organizer and officers can set one."));
				continue;
			}
			changed |= ApplyHardReserve(guildEvent, hard.ItemId, hard.ItemName, hard.Holder, hard.Active, hard.By, hard.At);
		}

		foreach (var duty in payload.Duties ?? [])
		{
			var guildEvent = await FindEventAsync(duty.EventId, duty.EventClientId, ct);
			if (guildEvent is null || !Duties.Contains(duty.Duty))
			{
				continue;
			}
			if (!CanManage(guildEvent, duty.By, identity, officers))
			{
				warnings.Add(L.T($"Rôle de soutien sur « {guildEvent.Title} » ignoré : réservé à l'organisateur et aux officiers.",
					$"Support duty on \"{guildEvent.Title}\" ignored: only the organizer and officers can assign one."));
				continue;
			}
			changed |= ApplyDuty(guildEvent, duty.Duty, duty.Character, duty.By, duty.At);
		}
		return changed;
	}

	/// <summary>Raison de refuser une réservation (null = acceptable). ItemId 0 = retrait, toujours permis.</summary>
	private string? ReserveProblem(GuildEvent guildEvent, string character, string priority, int itemId)
	{
		if (!Priorities.Contains(priority) || itemId < 0)
		{
			return L.T("Priorité de réservation inconnue.", "Unknown reserve priority.");
		}
		if (itemId == 0)
		{
			return null;
		}
		if (guildEvent.HardReserves.Any(h => h.Active && h.ItemId == itemId))
		{
			return L.T($"Objet #{itemId} réservé d'office sur « {guildEvent.Title} » : réservation de {character} refusée.",
				$"Item #{itemId} is hard-reserved on \"{guildEvent.Title}\": {character}'s reserve refused.");
		}
		if (guildEvent.Reserves.Any(r => r.Character == character && r.Priority != priority && r.ItemId == itemId))
		{
			return L.T($"{character} réserve déjà l'objet #{itemId} à une autre priorité.",
				$"{character} already reserves item #{itemId} at another priority.");
		}
		return null;
	}

	private static bool ApplyReserve(GuildEvent guildEvent, string character, string priority, int itemId, string? itemName, string by, long at)
	{
		var row = guildEvent.Reserves.SingleOrDefault(r => r.Character == character && r.Priority == priority);
		if (row is not null && at <= row.At)
		{
			return false;
		}
		if (row is null)
		{
			row = new EventReserve { Character = character, Priority = priority };
			guildEvent.Reserves.Add(row);
		}
		// Même objet sans nom fourni : on garde le nom déjà connu.
		var keptName = row.ItemId == itemId ? row.ItemName : null;
		row.ItemId = itemId;
		row.ItemName = itemId == 0 ? null : Clip(itemName, 100) ?? keptName;
		row.SetBy = by;
		row.At = at;
		return true;
	}

	private static bool ApplyHardReserve(GuildEvent guildEvent, int itemId, string? itemName, string? holder, bool active, string by, long at)
	{
		var row = guildEvent.HardReserves.SingleOrDefault(h => h.ItemId == itemId);
		if (row is not null && at <= row.At)
		{
			return false;
		}
		if (row is null)
		{
			row = new EventHardReserve { ItemId = itemId };
			guildEvent.HardReserves.Add(row);
		}
		row.ItemName = Clip(itemName, 100) ?? row.ItemName;
		row.Holder = Clip(holder, 64);
		row.Active = active;
		row.SetBy = by;
		row.At = at;
		return true;
	}

	private static bool ApplyDuty(GuildEvent guildEvent, string duty, string? character, string by, long at)
	{
		var row = guildEvent.Duties.SingleOrDefault(d => d.Duty == duty);
		if (row is not null && at <= row.At)
		{
			return false;
		}
		if (row is null)
		{
			row = new EventDuty { Duty = duty };
			guildEvent.Duties.Add(row);
		}
		row.Character = Clip(character, 64);
		row.SetBy = by;
		row.At = at;
		return true;
	}

	// ---- Lecture : réservations effectives et liste de contrôle --------------------------

	/// <summary>Le joueur garde-t-il sa place ? Une réponse « absent » l'annule.</summary>
	public static bool IsPlaced(EventSignup s, string placement) => s.Placement == placement && s.Response != "no";

	/// <summary>
	/// Réservations qui comptent : posées, hors objets réservés d'office, et
	/// une fois verrouillé, seulement celles des joueurs confirmés.
	/// </summary>
	public static List<EventReserve> EffectiveReserves(GuildEvent e)
	{
		var hard = e.HardReserves.Where(h => h.Active).Select(h => h.ItemId).ToHashSet();
		var confirmed = e.Signups.Where(s => IsPlaced(s, "confirmed")).Select(s => s.Character).ToHashSet();
		return e.Reserves
			.Where(r => r.ItemId > 0 && !hard.Contains(r.ItemId) && (!e.ReservesLocked || confirmed.Contains(r.Character)))
			.OrderBy(r => r.ItemId).ThenBy(r => Array.IndexOf(Priorities, r.Priority)).ThenBy(r => r.Character)
			.ToList();
	}

	public sealed record ChecklistItem(bool Blocking, string Text);

	/// <summary>Ce qui manque avant le raid : composition, réponses en suspens, réservations, rôles de soutien.</summary>
	public static List<ChecklistItem> Checklist(GuildEvent e, BotLocalizer l)
	{
		var items = new List<ChecklistItem>();
		var confirmed = e.Signups.Where(s => IsPlaced(s, "confirmed")).ToList();
		foreach (var (role, target) in new[] { ("tank", e.TargetTank), ("healer", e.TargetHealer), ("melee", e.TargetMelee), ("ranged", e.TargetRanged) })
		{
			var count = confirmed.Count(s => s.Role == role);
			if (target > 0 && count < target)
			{
				items.Add(new(true, l.T($"Il manque {target - count} {RoleLabel(role, l)} confirmé(s) ({count}/{target}).",
					$"{target - count} more confirmed {RoleLabel(role, l)} needed ({count}/{target}).")));
			}
		}
		var unplaced = e.Signups.Count(s => s.Placement == "pending" && s.Response == "yes");
		if (unplaced > 0)
		{
			items.Add(new(false, l.T($"{unplaced} présent(s) pas encore placé(s) dans le roster.", $"{unplaced} attending player(s) not placed yet.")));
		}
		var tentative = e.Signups.Where(s => s.Response == "tentative").Select(s => s.Character).ToList();
		if (tentative.Count > 0)
		{
			items.Add(new(false, l.T($"Encore « peut-être » : {string.Join(", ", tentative)}.", $"Still tentative: {string.Join(", ", tentative)}.")));
		}
		foreach (var conflict in EffectiveReserves(e).GroupBy(r => r.ItemId).Where(g => g.Count() > 1))
		{
			var name = conflict.First().ItemName ?? $"#{conflict.Key}";
			items.Add(new(false, l.T($"{name} réservé par {conflict.Count()} joueurs : {string.Join(", ", conflict.Select(r => r.Character))}.",
				$"{name} reserved by {conflict.Count()} players: {string.Join(", ", conflict.Select(r => r.Character))}.")));
		}
		var confirmedNames = confirmed.Select(s => s.Character).ToHashSet();
		foreach (var duty in Duties)
		{
			var holder = e.Duties.SingleOrDefault(d => d.Duty == duty)?.Character;
			if (holder is null)
			{
				items.Add(new(duty == "lead", l.T($"Personne n'est {DutyLabel(duty, l)}.", $"Nobody is {DutyLabel(duty, l)}.")));
			}
			else if (!confirmedNames.Contains(holder))
			{
				items.Add(new(true, l.T($"{holder} est {DutyLabel(duty, l)} mais n'est plus confirmé.",
					$"{holder} is {DutyLabel(duty, l)} but no longer confirmed.")));
			}
		}
		if (!e.ReservesLocked && e.Reserves.Any(r => r.ItemId > 0))
		{
			items.Add(new(false, l.T("Réservations pas encore verrouillées.", "Reserves not locked yet.")));
		}
		return items;
	}

	/// <summary>Numéro d'objet dans « 19019 », « item=19019 », « item:19019 » ou un lien d'objet du jeu.</summary>
	public static int? ParseItemId(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}
		var match = ItemIdPattern().Match(text.Trim());
		return match.Success && int.TryParse(match.Groups[1].Value, out var id) && id > 0 ? id : null;
	}

	[GeneratedRegex(@"^(?:.*?item[=:])?(\d{1,7})\b")]
	private static partial Regex ItemIdPattern();

	// ---- Depuis Discord ---------------------------------------------------------------------

	private async Task<GuildEvent?> LoadForChangeAsync(int eventId, CancellationToken ct) =>
		await WithDetails(db.Events).SingleOrDefaultAsync(e => e.Id == eventId, ct);

	public async Task<Result> ReserveFromDiscordAsync(string discordId, bool isServerAdmin, int eventId,
		string priority, int itemId, string? itemName, string? character, CancellationToken ct = default)
	{
		var guildEvent = await LoadForChangeAsync(eventId, ct);
		if (guildEvent is null)
		{
			return NotFound(eventId);
		}
		var actor = await ActorAsync(discordId, isServerAdmin, ct);
		var manager = CanManage(guildEvent, actor);
		string name;
		if (character is null)
		{
			if (actor.Main is null)
			{
				return Result.Invalid(SharedMessages.NoLinkedCharacter(L));
			}
			name = actor.Main.Name;
		}
		else
		{
			var own = actor.Find(character);
			if (own is null && !manager)
			{
				return Result.Forbidden(L.T("Seuls l'organisateur et les officiers réservent pour un autre joueur.", "Only the organizer and officers reserve for another player."));
			}
			name = own?.Name ?? guildEvent.Signups.FirstOrDefault(s => string.Equals(s.Character, character.Trim(), StringComparison.OrdinalIgnoreCase))?.Character
				?? character.Trim();
		}
		if (guildEvent.ReservesLocked && !manager)
		{
			return Result.Conflict(L.T($"Les réservations de « {guildEvent.Title} » sont verrouillées.", $"Reserves for \"{guildEvent.Title}\" are locked."));
		}
		if (ReserveProblem(guildEvent, name, priority, itemId) is { } problem)
		{
			return Result.Conflict(problem);
		}
		var existing = guildEvent.Reserves.SingleOrDefault(r => r.Character == name && r.Priority == priority);
		itemName ??= KnownItemName(guildEvent, itemId);
		ApplyReserve(guildEvent, name, priority, itemId, itemName, actor.Main?.Name ?? name, NextAt(existing?.At ?? 0));
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(itemId == 0
			? L.T($"Réservation {PriorityLabel(priority, L)} de **{name}** retirée sur **{guildEvent.Title}**.",
				$"**{name}**'s {PriorityLabel(priority, L)} reserve removed on **{guildEvent.Title}**.")
			: L.T($"**{name}** réserve **{itemName ?? $"l'objet #{itemId}"}** en priorité {PriorityLabel(priority, L)} sur **{guildEvent.Title}**.",
				$"**{name}** reserves **{itemName ?? $"item #{itemId}"}** at {PriorityLabel(priority, L)} priority on **{guildEvent.Title}**."));
	}

	public async Task<Result> HardReserveFromDiscordAsync(string discordId, bool isServerAdmin, int eventId,
		int itemId, string? itemName, string? holder, bool active, CancellationToken ct = default)
	{
		var guildEvent = await LoadForChangeAsync(eventId, ct);
		if (guildEvent is null)
		{
			return NotFound(eventId);
		}
		var actor = await ActorAsync(discordId, isServerAdmin, ct);
		if (!CanManage(guildEvent, actor))
		{
			return NotManager(guildEvent);
		}
		var existing = guildEvent.HardReserves.SingleOrDefault(h => h.ItemId == itemId);
		if (!active && existing is not { Active: true })
		{
			return Result.NotFound(L.T($"L'objet #{itemId} n'est pas réservé d'office.", $"Item #{itemId} is not hard-reserved."));
		}
		itemName ??= KnownItemName(guildEvent, itemId);
		ApplyHardReserve(guildEvent, itemId, itemName, holder, active, actor.Main?.Name ?? "Discord", NextAt(existing?.At ?? 0));
		await dataVersion.SaveAndBumpAsync(ct);
		var label = itemName ?? $"#{itemId}";
		return Result.Success(active
			? L.T($"**{label}** réservé d'office sur **{guildEvent.Title}**{(holder is null ? "" : $" pour **{holder}**")} : il sort des réservations des joueurs.",
				$"**{label}** hard-reserved on **{guildEvent.Title}**{(holder is null ? "" : $" for **{holder}**")}: players can no longer reserve it.")
			: L.T($"**{label}** n'est plus réservé d'office sur **{guildEvent.Title}**.", $"**{label}** is no longer hard-reserved on **{guildEvent.Title}**."));
	}

	public async Task<Result> AssignDutyFromDiscordAsync(string discordId, bool isServerAdmin, int eventId,
		string duty, string? character, CancellationToken ct = default)
	{
		var guildEvent = await LoadForChangeAsync(eventId, ct);
		if (guildEvent is null)
		{
			return NotFound(eventId);
		}
		var actor = await ActorAsync(discordId, isServerAdmin, ct);
		if (!CanManage(guildEvent, actor))
		{
			return NotManager(guildEvent);
		}
		if (!Duties.Contains(duty))
		{
			return Result.Invalid(L.T("Rôle de soutien inconnu.", "Unknown support duty."));
		}
		var name = character is null ? null
			: guildEvent.Signups.FirstOrDefault(s => string.Equals(s.Character, character.Trim(), StringComparison.OrdinalIgnoreCase))?.Character ?? character.Trim();
		var existing = guildEvent.Duties.SingleOrDefault(d => d.Duty == duty);
		ApplyDuty(guildEvent, duty, name, actor.Main?.Name ?? "Discord", NextAt(existing?.At ?? 0));
		await dataVersion.SaveAndBumpAsync(ct);
		var warning = name is not null && !guildEvent.Signups.Any(s => s.Character == name && IsPlaced(s, "confirmed"))
			? L.T($"\n_Attention : {name} n'est pas confirmé sur cet événement._", $"\n_Note: {name} is not confirmed on this event._")
			: "";
		return Result.Success((name is null
			? L.T($"Plus de {DutyLabel(duty, L)} sur **{guildEvent.Title}**.", $"No {DutyLabel(duty, L)} anymore on **{guildEvent.Title}**.")
			: L.T($"**{name}** est {DutyLabel(duty, L)} sur **{guildEvent.Title}**.", $"**{name}** is {DutyLabel(duty, L)} on **{guildEvent.Title}**.")) + warning);
	}

	/// <summary>Nom déjà connu d'un objet dans cet événement (réservé par quelqu'un d'autre en jeu).</summary>
	private static string? KnownItemName(GuildEvent e, int itemId) =>
		e.Reserves.FirstOrDefault(r => r.ItemId == itemId && r.ItemName is not null)?.ItemName ??
		e.HardReserves.FirstOrDefault(h => h.ItemId == itemId && h.ItemName is not null)?.ItemName;

	/// <summary>Objets déjà nommés dans les réservations de la guilde : suggestions sur Discord.</summary>
	public async Task<List<(int Id, string Name)>> KnownItemsAsync(CancellationToken ct = default)
	{
		var reserved = await db.EventReserves.AsNoTracking().Where(r => r.ItemId > 0 && r.ItemName != null)
			.Select(r => new { r.ItemId, r.ItemName }).Distinct().Take(500).ToListAsync(ct);
		var hard = await db.EventHardReserves.AsNoTracking().Where(h => h.ItemName != null)
			.Select(h => new { h.ItemId, h.ItemName }).Distinct().Take(200).ToListAsync(ct);
		return reserved.Concat(hard)
			.GroupBy(i => i.ItemId)
			.Select(g => (g.Key, g.First().ItemName!))
			.OrderBy(i => i.Item2)
			.ToList();
	}
}
