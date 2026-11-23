using System.Globalization;
using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Modules.Evenements.Contracts;
using GuildProfessions.Server.Shared;
using static GuildProfessions.Server.Shared.Text;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Presence.Application;

/// <summary>
/// Présence et butin : raids réellement joués, relevés en jeu par les membres
/// présents (une inscription ne vaut jamais présence), objets épiques ou
/// mieux reçus, et corrections d'officiers gardées à part du relevé.
/// Plusieurs postes relèvent le même raid : on fusionne (plus long temps de
/// présence, premier relevé de chaque objet).
/// </summary>
public sealed class AttendanceService(AppDbContext db, IAnnuaire annuaire, IEvenements evenements, IDataVersion dataVersion,
	BotLocalizer localizer, ILogger<AttendanceService> logger)
{
	private BotLocalizer L => localizer;

	/// <summary>Écart maximal entre le début d'un raid joué et l'événement prévu auquel on le rattache.</summary>
	public static readonly TimeSpan EventWindow = TimeSpan.FromHours(3);

	public static readonly string[] Statuses = ["present", "absent", "excused"];

	public const int EpicQuality = 4;
	public static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(90);

	public async Task<List<string>> ApplyUploadAsync(UploadContext context, CancellationToken ct = default)
	{
		var payload = context.Payload;
		var warnings = new List<string>();
		if (payload.Raids is not { Count: > 0 } && payload.Loot is not { Count: > 0 } &&
			payload.AttendanceCorrections is not { Count: > 0 } && payload.LootCorrections is not { Count: > 0 })
		{
			return warnings;
		}
		// Règle d'identité commune à tous les modules, tenue par l'annuaire.
		var identity = await annuaire.UploadIdentityAsync(context, ct);
		var changed = false;

		foreach (var raid in payload.Raids ?? [])
		{
			// System.Text.Json laisse passer null dans un champ non nullable : on valide ici.
			if (!IsComplete(raid))
			{
				warnings.Add(L.T("Raid relevé incomplet ignoré (clé, jour, observateur ou participants manquants).",
					"Incomplete raid record ignored (missing key, day, observer or participants)."));
				continue;
			}
			if (!identity.Owns(raid.Observer) || !raid.Participants.ContainsKey(raid.Observer) ||
				raid.Key.Length is 0 or > 120 || !DateOnly.TryParseExact(raid.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
			{
				continue; // un poste ne relève que les raids où il était
			}
			changed |= await MergeRaidAsync(raid, ct);
		}

		foreach (var loot in payload.Loot ?? [])
		{
			if (loot.Raid is null || loot.Character is null || !identity.Owns(loot.Observer) || loot.Quality < EpicQuality || loot.ItemId <= 0)
			{
				continue;
			}
			var session = await FindAsync(loot.Raid, ct);
			if (session is null || !session.Participants.Any(p => p.Character == loot.Observer))
			{
				continue;
			}
			var ordinal = Math.Max(1, loot.Ordinal);
			if (session.Loot.Any(l => l.Character == loot.Character && l.ItemId == loot.ItemId && l.Ordinal == ordinal))
			{
				continue; // déjà relevé par un autre poste
			}
			session.Loot.Add(new RaidLoot
			{
				Character = loot.Character,
				ItemId = loot.ItemId,
				ItemName = Clip(loot.ItemName, 100),
				Quality = loot.Quality,
				Ordinal = ordinal,
				At = loot.At,
			});
			changed = true;
		}

		foreach (var correction in payload.AttendanceCorrections ?? [])
		{
			if (correction.Raid is null || correction.Character is null || correction.Status is null)
			{
				continue;
			}
			if (!identity.IsOfficer(correction.By))
			{
				warnings.Add(L.T($"Correction de présence de {correction.Character} ignorée : réservée aux officiers.",
					$"Attendance correction for {correction.Character} ignored: officers only."));
				continue;
			}
			var session = await FindAsync(correction.Raid, ct);
			if (session is not null && Statuses.Contains(correction.Status))
			{
				changed |= ApplyCorrection(session, correction.Character, correction.Status, correction.Note, correction.By, correction.At);
			}
		}

		foreach (var correction in payload.LootCorrections ?? [])
		{
			if (correction.Raid is null || correction.Character is null)
			{
				continue;
			}
			if (!identity.IsOfficer(correction.By))
			{
				warnings.Add(L.T("Correction de butin ignorée : réservée aux officiers.", "Loot correction ignored: officers only."));
				continue;
			}
			var session = await FindAsync(correction.Raid, ct);
			var loot = session?.Loot.SingleOrDefault(l => l.Character == correction.Character && l.ItemId == correction.ItemId && l.Ordinal == correction.Ordinal);
			if (loot is not null)
			{
				changed |= ApplyVoid(loot, correction.Void, correction.Note, correction.By, correction.At);
			}
		}

		if (changed)
		{
			await dataVersion.SaveAndBumpAsync(ct);
		}
		return warnings;
	}

	private static bool IsComplete(UploadRaid raid) =>
		raid.Key is not null && raid.Day is not null && raid.Observer is not null && raid.Participants is not null;

	private async Task<bool> MergeRaidAsync(UploadRaid raid, CancellationToken ct)
	{
		var session = await FindAsync(raid.Key, ct);
		var changed = false;
		if (session is null)
		{
			session = new RaidSession
			{
				Key = raid.Key,
				Instance = Clip(raid.Instance, 80) ?? "?",
				Day = raid.Day,
				Leader = Clip(raid.Leader, 64),
				StartedAt = raid.StartedAt,
				EndedAt = raid.EndedAt,
			};
			db.RaidSessions.Add(session);
			changed = true;
		}
		if (raid.StartedAt > 0 && (session.StartedAt == 0 || raid.StartedAt < session.StartedAt))
		{
			session.StartedAt = raid.StartedAt;
			changed = true;
		}
		if (raid.EndedAt > session.EndedAt)
		{
			session.EndedAt = raid.EndedAt;
			changed = true;
		}
		foreach (var (character, minutes) in raid.Participants.Take(80))
		{
			if (character.Length is 0 or > 64)
			{
				continue;
			}
			var row = session.Participants.SingleOrDefault(p => p.Character == character);
			var classFile = raid.Classes?.GetValueOrDefault(character);
			if (row is null)
			{
				session.Participants.Add(new RaidParticipant { Character = character, Minutes = Math.Clamp(minutes, 0, 24 * 60), ClassFile = classFile });
				changed = true;
			}
			else if (minutes > row.Minutes || (row.ClassFile is null && classFile is not null))
			{
				row.Minutes = Math.Clamp(Math.Max(row.Minutes, minutes), 0, 24 * 60);
				row.ClassFile ??= classFile;
				changed = true;
			}
		}
		// Le raid du soir prévu dans l'agenda, s'il y en a un à moins de trois heures (lu par le contrat du module Événements).
		if (session.EventId is null && session.StartedAt > 0 &&
			await EventOfRaidAsync(session) is { } eventId)
		{
			session.EventId = eventId;
			changed = true;
		}
		await db.SaveChangesAsync(ct);
		return changed;
	}

	/// <summary>
	/// Rattachement facultatif à l'événement du soir : une panne du module
	/// Événements ne doit pas faire perdre le relevé de présence. On garde le
	/// raid sans événement, un prochain envoi du même raid le rattachera.
	/// </summary>
	private async Task<int?> EventOfRaidAsync(RaidSession session, CancellationToken ct = default)
	{
		var participants = session.Participants.Select(p => p.Character).ToList();
		try
		{
			return await evenements.EventOfRaidAsync(session.StartedAt, EventWindow, participants, ct);
		}
		catch (Exception exception) when (!ct.IsCancellationRequested)
		{
			logger.LogWarning(exception, "Présence : rattachement du raid {Key} à un événement impossible, raid gardé sans événement.", session.Key);
			return null;
		}
	}

	private static bool ApplyCorrection(RaidSession session, string character, string status, string? note, string by, long at)
	{
		var row = session.Corrections.SingleOrDefault(c => c.Character == character);
		if (row is not null && at <= row.At)
		{
			return false;
		}
		if (row is null)
		{
			row = new AttendanceCorrection { Character = character, Status = status };
			session.Corrections.Add(row);
		}
		row.Status = status;
		row.Note = Clip(note, 120);
		row.By = by;
		row.At = at;
		return true;
	}

	private static bool ApplyVoid(RaidLoot loot, bool voided, string? note, string by, long at)
	{
		if (at <= loot.VoidAt)
		{
			return false;
		}
		loot.Voided = voided;
		loot.VoidNote = Clip(note, 120);
		loot.VoidBy = by;
		loot.VoidAt = at;
		return true;
	}

	private async Task<RaidSession?> FindAsync(string key, CancellationToken ct) =>
		db.RaidSessions.Local.FirstOrDefault(s => s.Key == key) ??
		await WithDetails(db.RaidSessions).SingleOrDefaultAsync(s => s.Key == key, ct);

	private static IQueryable<RaidSession> WithDetails(IQueryable<RaidSession> query) => query
		.Include(s => s.Participants)
		.Include(s => s.Loot)
		.Include(s => s.Corrections)
		.AsSplitQuery();

	// ---- Lecture ------------------------------------------------------------------------------

	public async Task<List<RaidSession>> RecentAsync(TimeSpan window, CancellationToken ct = default)
	{
		var since = DateTimeOffset.UtcNow.Add(-window).ToUnixTimeSeconds();
		return await WithDetails(db.RaidSessions.AsNoTracking())
			.Where(s => s.StartedAt >= since)
			.OrderByDescending(s => s.StartedAt)
			.ToListAsync(ct);
	}

	public async Task<RaidSession?> GetAsync(int id, CancellationToken ct = default) =>
		await WithDetails(db.RaidSessions.AsNoTracking()).SingleOrDefaultAsync(s => s.Id == id, ct);

	public async Task<List<ExportRaidDto>> BuildExportAsync(CancellationToken ct = default) =>
		(await RecentAsync(HistoryWindow, ct)).Select(s => new ExportRaidDto(
			s.Id, s.Key, s.Instance, s.Day, s.Leader, s.StartedAt, s.EndedAt, s.EventId,
			s.Participants.OrderBy(p => p.Character).Select(p => new ExportRaidParticipantDto(p.Character, p.ClassFile, p.Minutes)).ToList(),
			s.Loot.OrderBy(l => l.At).Select(l => new ExportRaidLootDto(l.Id, l.Character, l.ItemId, l.ItemName, l.Quality, l.Ordinal, l.At,
				l.Voided, l.VoidNote, l.VoidBy, l.VoidAt)).ToList(),
			s.Corrections.OrderBy(c => c.Character).Select(c => new ExportAttendanceCorrectionDto(c.Character, c.Status, c.Note, c.By, c.At)).ToList()))
			.ToList();

	// ---- Depuis Discord ---------------------------------------------------------------------

	private async Task<(bool Officer, string By)> OfficerAsync(string discordId, bool isServerAdmin, CancellationToken ct)
	{
		var actor = await annuaire.DiscordActorAsync(discordId, isServerAdmin, ct);
		var by = actor.Characters.FirstOrDefault(c => c.IsOfficer)?.Name ?? actor.Characters.FirstOrDefault()?.Name ?? "Discord";
		return (actor.IsOfficer, by);
	}

	public async Task<Result> CorrectFromDiscordAsync(string discordId, bool isServerAdmin, int raidId,
		string character, string status, string? note, CancellationToken ct = default)
	{
		var (officer, by) = await OfficerAsync(discordId, isServerAdmin, ct);
		if (!officer)
		{
			return Result.Forbidden(L.T("Seuls les officiers corrigent la présence.", "Only officers correct attendance."));
		}
		var session = await WithDetails(db.RaidSessions).SingleOrDefaultAsync(s => s.Id == raidId, ct);
		if (session is null || !Statuses.Contains(status))
		{
			return Result.NotFound(L.T($"Raid #{raidId} introuvable.", $"Raid #{raidId} not found."));
		}
		var name = session.Participants.FirstOrDefault(p => string.Equals(p.Character, character.Trim(), StringComparison.OrdinalIgnoreCase))?.Character
			?? await annuaire.FindCharacterNameAsync(character, ct)
			?? character.Trim();
		var previous = session.Corrections.SingleOrDefault(c => c.Character == name)?.At ?? 0;
		ApplyCorrection(session, name, status, note, by, Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), previous + 1));
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T(
			$"**{name}** : {StatusLabel(status, L)} au raid #{raidId} ({session.Instance}, {session.Day}).",
			$"**{name}**: {StatusLabel(status, L)} for raid #{raidId} ({session.Instance}, {session.Day})."));
	}

	public async Task<Result> VoidLootFromDiscordAsync(string discordId, bool isServerAdmin, int lootId,
		bool voided, string? note, CancellationToken ct = default)
	{
		var (officer, by) = await OfficerAsync(discordId, isServerAdmin, ct);
		if (!officer)
		{
			return Result.Forbidden(L.T("Seuls les officiers corrigent le butin.", "Only officers correct loot."));
		}
		var loot = await db.RaidLoot.SingleOrDefaultAsync(l => l.Id == lootId, ct);
		if (loot is null)
		{
			return Result.NotFound(L.T($"Objet #{lootId} introuvable.", $"Loot #{lootId} not found."));
		}
		ApplyVoid(loot, voided, note, by, Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), loot.VoidAt + 1));
		await dataVersion.SaveAndBumpAsync(ct);
		var label = loot.ItemName ?? $"#{loot.ItemId}";
		return Result.Success(voided
			? L.T($"**{label}** de {loot.Character} annulé dans l'historique.", $"**{label}** for {loot.Character} voided in the history.")
			: L.T($"**{label}** de {loot.Character} rétabli.", $"**{label}** for {loot.Character} restored."));
	}

	public static string StatusLabel(string status, BotLocalizer l) => status switch
	{
		"present" => l.T("présent", "present"),
		"absent" => l.T("absent", "absent"),
		"excused" => l.T("excusé", "excused"),
		_ => status,
	};

}
