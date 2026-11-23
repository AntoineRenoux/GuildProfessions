using System.Text.Json;
using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.EntityFrameworkCore;
using static GuildProfessions.Server.Shared.Text;

namespace GuildProfessions.Server.Modules.Profils.Application;

/// <summary>
/// Profils des joueurs et accueil des nouveaux. Règles, identiques en jeu et
/// sur Discord :
/// - un profil (principal, rerolls, rôles, disponibilités, note) ne se
///   déclare que pour des personnages du même compte (ceux de l'envoi) ;
/// - talents et version de l'addon : pour ses propres personnages ;
/// - arrivées et départs : relevés par n'importe quel membre, dédoublonnés ;
/// - étapes d'accueil : le joueur pour lui-même, ou un officier.
/// </summary>
public sealed class ProfileService(AppDbContext db, IAnnuaire annuaire, IDataVersion dataVersion, BotLocalizer localizer)
{
	private BotLocalizer L => localizer;

	public static readonly string[] Roles = ["tank", "healer", "melee", "ranged"];
	public static readonly string[] Steps = ["discord", "main", "profile", "handbook", "intro"];
	public static readonly string[] Kinds = ["join", "leave"];

	public const int AvailabilityMax = 120;
	public const int NoteMax = 200;
	public const int MaxAlts = 20;

	/// <summary>Arrivées et départs exportés, et fenêtre où un nouveau est suivi par l'accueil.</summary>
	public static readonly TimeSpan JournalWindow = TimeSpan.FromDays(90);
	public static readonly TimeSpan NewcomerWindow = TimeSpan.FromDays(60);

	/// <summary>Un même mouvement relevé par plusieurs membres dans cet intervalle n'est noté qu'une fois.</summary>
	private const long DuplicateSeconds = 12 * 3600;

	private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	// ---- Envoi depuis le jeu -----------------------------------------------------------------

	public async Task<List<string>> ApplyUploadAsync(UploadContext context, CancellationToken ct = default)
	{
		var warnings = new List<string>();
		if (context.Payload.Profiles is not { } upload)
		{
			return warnings;
		}
		var identity = await annuaire.UploadIdentityAsync(context, ct);
		var changed = false;

		foreach (var profile in upload.Profiles ?? [])
		{
			var alts = (profile.Alts ?? []).Where(a => a != profile.Main).Distinct().Take(MaxAlts).ToList();
			if (!identity.Owns(profile.Main) || alts.Any(a => !identity.Owns(a)))
			{
				warnings.Add(L.T($"Profil de {profile.Main} ignoré : principal et rerolls doivent être des personnages de cet envoi.",
					$"Profile of {profile.Main} ignored: main and alts must be characters of this upload."));
				continue;
			}
			changed |= await ApplyProfileAsync(profile.Main, alts, profile.Roles, profile.Availability, profile.Note, profile.At, ct);
		}
		foreach (var talents in upload.Talents ?? [])
		{
			if (identity.Owns(talents.Character) && Clip(talents.Summary, 40) is { } summary)
			{
				changed |= await ApplyTalentsAsync(talents.Character, Clip(talents.Spec, 40), summary, talents.At, ct);
			}
		}
		foreach (var version in upload.Versions ?? [])
		{
			if (identity.Owns(version.Character) && Clip(version.Version, 20) is { } value)
			{
				changed |= await ApplyVersionAsync(version.Character, value, ct);
			}
		}
		foreach (var change in upload.RosterChanges ?? [])
		{
			// Relevé par n'importe quel personnage de l'envoi (le premier, à défaut de le savoir).
			var reporter = identity.Owns(change.By) ? change.By : identity.Characters.Order().FirstOrDefault();
			if (reporter is not null)
			{
				changed |= await ApplyRosterChangeAsync(change, reporter, ct);
			}
		}
		foreach (var step in upload.Onboarding ?? [])
		{
			var allowed = identity.Owns(step.By) && (step.By == step.Character || identity.IsOfficer(step.By) || identity.Owns(step.Character));
			if (!allowed)
			{
				warnings.Add(L.T($"Étape d'accueil de {step.Character} ignorée : le joueur lui-même ou un officier seulement.",
					$"Onboarding step for {step.Character} ignored: the player or an officer only."));
				continue;
			}
			changed |= await ApplyStepAsync(step.Character, step.Step, step.Done, step.By, step.At, ct);
		}

		if (changed)
		{
			await dataVersion.SaveAndBumpAsync(ct);
		}
		return warnings;
	}

	private async Task<bool> ApplyProfileAsync(string main, List<string> alts, List<string>? roles, string? availability, string? note,
		long at, CancellationToken ct)
	{
		var profile = db.PlayerProfiles.Local.FirstOrDefault(p => p.Main == main) ??
			await db.PlayerProfiles.SingleOrDefaultAsync(p => p.Main == main, ct);
		if (profile is not null && at <= profile.UpdatedAt)
		{
			return false;
		}
		if (profile is null)
		{
			profile = new PlayerProfile { Main = main };
			db.PlayerProfiles.Add(profile);
		}
		profile.AltsJson = JsonSerializer.Serialize(alts);
		profile.Roles = string.Join(",", (roles ?? []).Where(Roles.Contains).Distinct());
		profile.Availability = Clip(availability, AvailabilityMax);
		profile.Note = Clip(note, NoteMax);
		profile.UpdatedAt = at;

		// Un personnage n'a qu'un profil : il quitte celui d'un autre principal (changement de principal).
		var mine = alts.Append(main).ToHashSet();
		var others = await db.PlayerProfiles.Where(p => p.Main != main).ToListAsync(ct);
		foreach (var other in others)
		{
			if (mine.Contains(other.Main))
			{
				db.PlayerProfiles.Remove(other);
				continue;
			}
			var otherAlts = AltsOf(other);
			if (otherAlts.Any(mine.Contains))
			{
				other.AltsJson = JsonSerializer.Serialize(otherAlts.Where(a => !mine.Contains(a)).ToList());
			}
		}
		return true;
	}

	private async Task<bool> ApplyTalentsAsync(string character, string? spec, string summary, long at, CancellationToken ct)
	{
		var row = await db.CharacterTalents.SingleOrDefaultAsync(t => t.Character == character, ct);
		if (row is not null && at <= row.At)
		{
			return false;
		}
		if (row is null)
		{
			row = new CharacterTalents { Character = character, Summary = summary };
			db.CharacterTalents.Add(row);
		}
		if (row.Summary == summary && row.Spec == spec && row.At != 0)
		{
			row.At = at;
			return false; // rien de neuf : pas de nouvelle version des données
		}
		row.Summary = summary;
		row.Spec = spec;
		row.At = at;
		return true;
	}

	private async Task<bool> ApplyVersionAsync(string character, string version, CancellationToken ct)
	{
		var row = await db.AddonVersionReports.SingleOrDefaultAsync(v => v.Character == character, ct);
		if (row is null)
		{
			db.AddonVersionReports.Add(new AddonVersionReport { Character = character, Version = version, At = Now() });
			return true;
		}
		if (row.Version == version)
		{
			return false;
		}
		row.Version = version;
		row.At = Now();
		return true;
	}

	private async Task<bool> ApplyRosterChangeAsync(UploadRosterChange change, string reporter, CancellationToken ct)
	{
		var character = Clip(change.Character, 64);
		if (character is null || !Kinds.Contains(change.Kind) || change.At <= 0 || change.At > Now() + 600 ||
			change.At < DateTimeOffset.UtcNow.Add(-JournalWindow).ToUnixTimeSeconds())
		{
			return false;
		}
		var from = change.At - DuplicateSeconds;
		var to = change.At + DuplicateSeconds;
		var known = db.RosterChanges.Local.Any(r => r.Character == character && r.Kind == change.Kind && r.At >= from && r.At <= to) ||
			await db.RosterChanges.AnyAsync(r => r.Character == character && r.Kind == change.Kind && r.At >= from && r.At <= to, ct);
		if (known)
		{
			return false;
		}
		db.RosterChanges.Add(new RosterChange { Character = character, Kind = change.Kind, At = change.At, ReportedBy = reporter });
		return true;
	}

	private async Task<bool> ApplyStepAsync(string character, string step, bool done, string by, long at, CancellationToken ct)
	{
		if (!Steps.Contains(step))
		{
			return false;
		}
		var row = db.OnboardingSteps.Local.FirstOrDefault(o => o.Character == character && o.Step == step) ??
			await db.OnboardingSteps.SingleOrDefaultAsync(o => o.Character == character && o.Step == step, ct);
		if (row is not null && at <= row.At)
		{
			return false;
		}
		if (row is null)
		{
			row = new OnboardingStep { Character = character, Step = step };
			db.OnboardingSteps.Add(row);
		}
		row.Done = done;
		row.By = by;
		row.At = at;
		return true;
	}

	public static List<string> AltsOf(PlayerProfile profile)
	{
		try
		{
			return JsonSerializer.Deserialize<List<string>>(profile.AltsJson) ?? [];
		}
		catch (JsonException)
		{
			return [];
		}
	}

	// ---- Lecture et export ----------------------------------------------------------------------

	/// <summary>Étapes faites, y compris celles déduites : compte Discord lié, principal choisi, profil rempli.</summary>
	public async Task<Dictionary<string, (HashSet<string> Done, long UpdatedAt)>> OnboardingAsync(CancellationToken ct = default)
	{
		var since = DateTimeOffset.UtcNow.Add(-NewcomerWindow).ToUnixTimeSeconds();
		var steps = await db.OnboardingSteps.AsNoTracking().ToListAsync(ct);
		var newcomers = await db.RosterChanges.AsNoTracking().Where(r => r.Kind == "join" && r.At >= since).Select(r => r.Character).ToListAsync(ct);
		var profiles = await db.PlayerProfiles.AsNoTracking().ToListAsync(ct);
		var result = new Dictionary<string, (HashSet<string> Done, long UpdatedAt)>();
		foreach (var name in steps.Select(s => s.Character).Concat(newcomers).Distinct())
		{
			var done = steps.Where(s => s.Character == name && s.Done).Select(s => s.Step).ToHashSet();
			var updated = steps.Where(s => s.Character == name).Select(s => s.At).DefaultIfEmpty(0).Max();
			var profile = profiles.FirstOrDefault(p => p.Main == name || AltsOf(p).Contains(name));
			if (profile is not null)
			{
				done.Add("main");
				if (profile.Roles.Length > 0 || profile.Availability is not null)
				{
					done.Add("profile");
				}
			}
			if (await annuaire.DiscordIdOfCharacterAsync(profile?.Main ?? name, ct) is not null)
			{
				done.Add("discord");
			}
			result[name] = (done, updated);
		}
		return result;
	}

	public async Task<ExportProfilesDto> BuildExportAsync(CancellationToken ct = default)
	{
		var since = DateTimeOffset.UtcNow.Add(-JournalWindow).ToUnixTimeSeconds();
		var profiles = await db.PlayerProfiles.AsNoTracking().OrderBy(p => p.Main).ToListAsync(ct);
		var talents = await db.CharacterTalents.AsNoTracking().OrderBy(t => t.Character)
			.Select(t => new ExportTalentsDto(t.Character, t.Spec, t.Summary, t.At)).ToListAsync(ct);
		var changes = await db.RosterChanges.AsNoTracking().Where(r => r.At >= since).OrderByDescending(r => r.At)
			.Select(r => new ExportRosterChangeDto(r.Character, r.Kind, r.At)).ToListAsync(ct);
		var versions = await db.AddonVersionReports.AsNoTracking().OrderBy(v => v.Character)
			.Select(v => new ExportVersionDto(v.Character, v.Version, v.At)).ToListAsync(ct);
		var onboarding = (await OnboardingAsync(ct))
			.OrderBy(o => o.Key)
			.Select(o => new ExportOnboardingDto(o.Key, Steps.Where(o.Value.Done.Contains).ToList(), o.Value.UpdatedAt))
			.ToList();
		return new ExportProfilesDto(
			profiles.Select(p => new ExportProfileDto(p.Main, AltsOf(p),
				p.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(), p.Availability, p.Note, p.UpdatedAt)).ToList(),
			talents, changes, onboarding, versions);
	}

	/// <summary>Profil où figure ce personnage (principal ou reroll), sans tenir compte de la casse.</summary>
	public async Task<PlayerProfile?> FindProfileAsync(string character, CancellationToken ct = default)
	{
		var profiles = await db.PlayerProfiles.AsNoTracking().ToListAsync(ct);
		return profiles.FirstOrDefault(p => string.Equals(p.Main, character.Trim(), StringComparison.OrdinalIgnoreCase)) ??
			profiles.FirstOrDefault(p => AltsOf(p).Any(a => string.Equals(a, character.Trim(), StringComparison.OrdinalIgnoreCase)));
	}

	public async Task<CharacterTalents?> TalentsAsync(string character, CancellationToken ct = default) =>
		await db.CharacterTalents.AsNoTracking().SingleOrDefaultAsync(t => t.Character == character, ct);

	public async Task<List<RosterChange>> JournalAsync(int days, CancellationToken ct = default)
	{
		var since = DateTimeOffset.UtcNow.AddDays(-days).ToUnixTimeSeconds();
		return await db.RosterChanges.AsNoTracking().Where(r => r.At >= since).OrderByDescending(r => r.At).ToListAsync(ct);
	}

	public async Task<List<AddonVersionReport>> VersionsAsync(CancellationToken ct = default) =>
		await db.AddonVersionReports.AsNoTracking().OrderByDescending(v => v.At).ToListAsync(ct);

	// ---- Actions depuis Discord -------------------------------------------------------------------

	/// <summary>
	/// Profil depuis Discord : le principal est celui du membre (ou un de ses
	/// personnages liés), les rerolls sont ses autres personnages liés.
	/// </summary>
	public async Task<Result> EditFromDiscordAsync(string discordId, List<string>? roles, string? availability, string? note,
		string? main, CancellationToken ct = default)
	{
		var actor = await annuaire.DiscordActorAsync(discordId, false, ct);
		var chosen = actor.Choose(main);
		if (chosen is null)
		{
			return Result.Invalid(main is null ? SharedMessages.NoLinkedCharacter(L) : SharedMessages.NotYourCharacter(L, main));
		}
		var existing = await FindProfileAsync(chosen.Name, ct);
		var alts = actor.Characters.Select(c => c.Name).Where(n => n != chosen.Name).ToList();
		var keepRoles = roles ?? (existing is null ? [] : existing.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());
		await ApplyProfileAsync(chosen.Name, alts, keepRoles, availability ?? existing?.Availability, note ?? existing?.Note,
			Math.Max(Now(), (existing?.UpdatedAt ?? 0) + 1), ct);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T($"Profil de **{chosen.Name}** enregistré{(alts.Count > 0 ? $" (rerolls : {string.Join(", ", alts)})" : "")}.",
			$"**{chosen.Name}**'s profile saved{(alts.Count > 0 ? $" (alts: {string.Join(", ", alts)})" : "")}."));
	}

	/// <summary>Coche ou décoche une étape d'accueil : la sienne, ou celle d'un autre pour un officier.</summary>
	public async Task<Result> SetStepFromDiscordAsync(string discordId, bool isServerAdmin, string step, bool done, string? character,
		CancellationToken ct = default)
	{
		if (!Steps.Contains(step))
		{
			return Result.Invalid(L.T("Étape inconnue.", "Unknown step."));
		}
		var actor = await annuaire.DiscordActorAsync(discordId, isServerAdmin, ct);
		string? target;
		if (character is null)
		{
			target = actor.Main?.Name;
			if (target is null)
			{
				return Result.Invalid(SharedMessages.NoLinkedCharacter(L));
			}
		}
		else
		{
			var own = actor.Find(character);
			if (own is null && !actor.IsOfficer)
			{
				return Result.Forbidden(L.T("Seuls les officiers cochent l'accueil d'un autre joueur.", "Only officers tick another player's onboarding."));
			}
			target = own?.Name ?? await annuaire.FindCharacterNameAsync(character, ct) ?? character.Trim();
		}
		var existing = await db.OnboardingSteps.AsNoTracking().SingleOrDefaultAsync(o => o.Character == target && o.Step == step, ct);
		await ApplyStepAsync(target, step, done, actor.Main?.Name ?? actor.DiscordName ?? "Discord", Math.Max(Now(), (existing?.At ?? 0) + 1), ct);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T($"**{target}** : étape « {StepLabel(step, L)} » {(done ? "faite" : "à refaire")}.",
			$"**{target}**: step \"{StepLabel(step, L)}\" {(done ? "done" : "to do again")}."));
	}

	/// <summary>L'appelant Discord est officier (rang en jeu ou gestionnaire du serveur).</summary>
	public async Task<bool> IsOfficerAsync(string discordId, bool isServerAdmin, CancellationToken ct = default) =>
		(await annuaire.DiscordActorAsync(discordId, isServerAdmin, ct)).IsOfficer;

	/// <summary>Personnage visé sur Discord : celui nommé (de l'annuaire), sinon le principal de l'appelant.</summary>
	public async Task<string?> TargetAsync(string discordId, string? character, CancellationToken ct = default)
	{
		if (character is not null)
		{
			return await annuaire.FindCharacterNameAsync(character, ct) ?? character.Trim();
		}
		return (await annuaire.DiscordActorAsync(discordId, false, ct)).Main?.Name;
	}

	/// <summary>Nouveaux dont l'accueil n'est pas fini, avec leur compte Discord s'il est lié (pour les mentionner).</summary>
	public async Task<List<(string Character, List<string> Missing, string? DiscordId)>> IncompleteAsync(CancellationToken ct = default)
	{
		var result = new List<(string, List<string>, string?)>();
		foreach (var (name, state) in (await OnboardingAsync(ct)).OrderBy(o => o.Key))
		{
			var missing = Steps.Where(s => !state.Done.Contains(s)).ToList();
			if (missing.Count > 0)
			{
				result.Add((name, missing, await annuaire.DiscordIdOfCharacterAsync(name, ct)));
			}
		}
		return result;
	}

	public static string StepLabel(string step, BotLocalizer l) => step switch
	{
		"discord" => l.T("rejoindre le Discord et lier son personnage", "join the Discord and link your character"),
		"main" => l.T("choisir son personnage principal", "pick your main character"),
		"profile" => l.T("remplir son profil", "fill in your profile"),
		"handbook" => l.T("lire la charte et les règles de butin", "read the handbook and loot rules"),
		"intro" => l.T("se présenter à la guilde", "introduce yourself to the guild"),
		_ => step,
	};

	public static string RoleLabel(string role, BotLocalizer l) => role switch
	{
		"tank" => "tank",
		"healer" => l.T("soigneur", "healer"),
		"melee" => l.T("DPS mêlée", "melee DPS"),
		"ranged" => l.T("DPS distance", "ranged DPS"),
		_ => role,
	};
}
