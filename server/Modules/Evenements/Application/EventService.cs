using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;
using static GuildProfessions.Server.Shared.Text;
using static GuildProfessions.Server.Modules.Evenements.Application.EventLabels;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Evenements.Application;

/// <summary>
/// Événements de guilde et leurs inscriptions. Trois sortes de changements
/// arrivent du jeu, chacun horodaté (secondes Unix de GetServerTime) : la
/// modification la plus récente gagne, ce qui rend l'ordre d'arrivée des
/// uploads indifférent.
/// - l'événement lui-même : son créateur (organisateur) ou un officier ;
/// - la réponse d'un joueur : seulement pour ses propres personnages ;
/// - la place dans le roster : l'organisateur ou un officier.
/// </summary>
public sealed partial class EventService(AppDbContext db, IAnnuaire annuaire, IDataVersion dataVersion, BotLocalizer localizer)
{
	private BotLocalizer L => localizer;

	public static readonly string[] Kinds = ["raid", "dungeon", "pvp", "other"];
	public static readonly string[] Roles = ["tank", "healer", "melee", "ranged"];
	public static readonly string[] Responses = ["yes", "tentative", "no"];
	public static readonly string[] Placements = ["pending", "confirmed", "standby", "bench"];

	public const int TitleMax = 80;
	public const int DescriptionMax = 500;
	public const int NoteMax = 120;
	public const int TargetMax = 40;

	/// <summary>Les événements passés restent exportés ce temps-là (affichage, futur suivi de présence).</summary>
	public static readonly TimeSpan HistoryWindow = TimeSpan.FromDays(14);

	public async Task<List<string>> ApplyUploadAsync(UploadContext context, CancellationToken ct = default)
	{
		var payload = context.Payload;
		var warnings = new List<string>();
		if (payload.Events is not { Count: > 0 } && payload.Signups is not { Count: > 0 } && payload.Placements is not { Count: > 0 } &&
			payload.Reserves is not { Count: > 0 } && payload.HardReserves is not { Count: > 0 } && payload.Duties is not { Count: > 0 })
		{
			return warnings;
		}

		// Règle d'identité commune à tous les modules, tenue par l'annuaire.
		var uploadIdentity = await annuaire.UploadIdentityAsync(context, ct);
		var identity = uploadIdentity.Characters;
		var officers = uploadIdentity.Officers;
		var changed = false;

		foreach (var uploaded in payload.Events ?? [])
		{
			changed |= ApplyEvent(uploaded, identity, officers, warnings, await FindEventAsync(uploaded.ServerId, uploaded.ClientId, ct));
			await db.SaveChangesAsync(ct); // un événement créé ici peut être visé par une inscription plus bas
		}

		foreach (var signup in payload.Signups ?? [])
		{
			var guildEvent = await FindEventAsync(signup.EventId, signup.EventClientId, ct);
			if (guildEvent is null)
			{
				continue;
			}
			if (!identity.Contains(signup.Character))
			{
				warnings.Add(L.T($"Inscription de {signup.Character} ignorée : personnage non lié à cet envoi.",
					$"Sign-up for {signup.Character} ignored: character not part of this upload."));
				continue;
			}
			changed |= ApplySignup(guildEvent, signup);
		}

		foreach (var placement in payload.Placements ?? [])
		{
			var guildEvent = await FindEventAsync(placement.EventId, placement.EventClientId, ct);
			if (guildEvent is null)
			{
				continue;
			}
			if (!CanManage(guildEvent, placement.By, identity, officers))
			{
				warnings.Add(L.T($"Roster de « {guildEvent.Title} » : seul l'organisateur ou un officier peut placer les joueurs.",
					$"Roster of \"{guildEvent.Title}\": only the organizer or an officer can place players."));
				continue;
			}
			changed |= ApplyPlacement(guildEvent, placement);
		}

		changed |= await ApplyRaidNightAsync(payload, identity, officers, warnings, ct);

		if (changed)
		{
			await dataVersion.SaveAndBumpAsync(ct);
		}
		return warnings;
	}

	private bool ApplyEvent(UploadEvent uploaded, IReadOnlySet<string> identity, IReadOnlySet<string> officers,
		List<string> warnings, GuildEvent? existing)
	{
		var title = Clip(uploaded.Title, TitleMax);
		if (title is null || uploaded.StartsAt <= 0)
		{
			return false;
		}
		if (existing is null)
		{
			// Un id serveur inconnu vise un événement supprimé ou d'une autre guilde.
			if (uploaded.ServerId is not null || uploaded.ClientId is null)
			{
				return false;
			}
			if (!identity.Contains(uploaded.By))
			{
				warnings.Add(L.T($"Événement « {title} » ignoré : {uploaded.By} n'est pas lié à cet envoi.",
					$"Event \"{title}\" ignored: {uploaded.By} is not part of this upload."));
				return false;
			}
			existing = new GuildEvent
			{
				ClientId = uploaded.ClientId,
				Title = title,
				Organizer = uploaded.By,
				CreatedUtc = DateTime.UtcNow,
				Source = DataSource.Addon,
			};
			db.Events.Add(existing);
		}
		else if (!CanManage(existing, uploaded.By, identity, officers))
		{
			warnings.Add(L.T($"Modification de « {existing.Title} » ignorée : réservée à l'organisateur et aux officiers.",
				$"Change to \"{existing.Title}\" ignored: only the organizer and officers can edit it."));
			return false;
		}
		else if (uploaded.At <= existing.UpdatedAt)
		{
			return false; // une version plus récente est déjà là
		}

		existing.Title = title;
		existing.Kind = Kinds.Contains(uploaded.Kind) ? uploaded.Kind : "other";
		existing.StartsAt = uploaded.StartsAt;
		existing.Description = Clip(uploaded.Description, DescriptionMax);
		existing.TargetTank = Math.Clamp(uploaded.Tank, 0, TargetMax);
		existing.TargetHealer = Math.Clamp(uploaded.Healer, 0, TargetMax);
		existing.TargetMelee = Math.Clamp(uploaded.Melee, 0, TargetMax);
		existing.TargetRanged = Math.Clamp(uploaded.Ranged, 0, TargetMax);
		existing.Cancelled = uploaded.Cancelled;
		existing.ReservesLocked = uploaded.Locked;
		existing.UpdatedAt = uploaded.At;
		return true;
	}

	private static bool ApplySignup(GuildEvent guildEvent, UploadSignup signup)
	{
		if (!Responses.Contains(signup.Response))
		{
			return false;
		}
		var row = guildEvent.Signups.SingleOrDefault(s => s.Character == signup.Character);
		if (row is not null && signup.At <= row.ResponseAt)
		{
			return false;
		}
		if (row is null)
		{
			row = new EventSignup { Character = signup.Character };
			guildEvent.Signups.Add(row);
		}
		row.ClassFile = signup.ClassFile ?? row.ClassFile;
		row.Role = Roles.Contains(signup.Role) ? signup.Role : row.Role;
		row.Spec = Clip(signup.Spec, 40);
		row.Response = signup.Response;
		row.Note = Clip(signup.Note, NoteMax);
		row.ResponseAt = signup.At;
		return true;
	}

	private static bool ApplyPlacement(GuildEvent guildEvent, UploadPlacement placement)
	{
		if (!Placements.Contains(placement.Placement))
		{
			return false;
		}
		var row = guildEvent.Signups.SingleOrDefault(s => s.Character == placement.Character);
		if (row is not null && placement.At <= row.PlacementAt)
		{
			return false;
		}
		if (row is null)
		{
			// Placé avant que sa réponse n'arrive : la réponse complètera la ligne.
			row = new EventSignup { Character = placement.Character, Response = "none" };
			guildEvent.Signups.Add(row);
		}
		row.Placement = placement.Placement;
		row.PlacementAt = placement.At;
		return true;
	}

	private static bool CanManage(GuildEvent guildEvent, string by, IReadOnlySet<string> identity, IReadOnlySet<string> officers) =>
		identity.Contains(by) && (by == guildEvent.Organizer || officers.Contains(by));

	private async Task<GuildEvent?> FindEventAsync(int? serverId, string? clientId, CancellationToken ct)
	{
		if (serverId is not null)
		{
			return await WithDetails(db.Events).SingleOrDefaultAsync(e => e.Id == serverId, ct);
		}
		if (clientId is null)
		{
			return null;
		}
		// Créé plus haut dans ce même envoi, pas encore relu depuis la base.
		return db.Events.Local.FirstOrDefault(e => e.ClientId == clientId) ??
			await WithDetails(db.Events).SingleOrDefaultAsync(e => e.ClientId == clientId, ct);
	}

	// ---- Export et Discord ------------------------------------------------------------------

	public async Task<List<ExportEventDto>> BuildExportAsync(CancellationToken ct = default)
	{
		var since = DateTimeOffset.UtcNow.Add(-HistoryWindow).ToUnixTimeSeconds();
		var events = await WithDetails(db.Events.AsNoTracking())
			.Where(e => e.StartsAt >= since)
			.OrderBy(e => e.StartsAt).ThenBy(e => e.Id)
			.ToListAsync(ct);
		return events.Select(e => new ExportEventDto(
			e.Id, e.ClientId, e.Title, e.Kind, e.StartsAt, e.Description, e.Organizer,
			e.TargetTank, e.TargetHealer, e.TargetMelee, e.TargetRanged, e.Cancelled, e.UpdatedAt,
			e.Signups.OrderBy(s => s.Character).Select(s => new ExportSignupDto(
				s.Character, s.ClassFile, s.Role, s.Spec, s.Response, s.Note, s.ResponseAt, s.Placement, s.PlacementAt)).ToList(),
			e.ReservesLocked,
			e.Reserves.OrderBy(r => r.Character).ThenBy(r => r.Priority)
				.Select(r => new ExportReserveDto(r.Character, r.Priority, r.ItemId, r.ItemName, r.SetBy, r.At)).ToList(),
			e.HardReserves.OrderBy(r => r.ItemId)
				.Select(r => new ExportHardReserveDto(r.ItemId, r.ItemName, r.Holder, r.Active, r.At)).ToList(),
			e.Duties.OrderBy(d => d.Duty).Select(d => new ExportDutyDto(d.Duty, d.Character, d.At)).ToList()))
			.ToList();
	}

	public async Task<List<GuildEvent>> UpcomingAsync(int limit = 10, CancellationToken ct = default)
	{
		// Encore visibles quelques heures après le début : le raid peut être en cours.
		var since = DateTimeOffset.UtcNow.AddHours(-4).ToUnixTimeSeconds();
		return await WithDetails(db.Events.AsNoTracking())
			.Where(e => e.StartsAt >= since && !e.Cancelled)
			.OrderBy(e => e.StartsAt)
			.Take(limit)
			.ToListAsync(ct);
	}

	/// <summary>Réponse depuis Discord, pour un personnage lié au compte (le principal par défaut).</summary>
	public async Task<Result> RespondFromDiscordAsync(string discordId, int eventId, string response,
		string? role, string? character, string? note, string? spec = null, CancellationToken ct = default)
	{
		var guildEvent = await db.Events.Include(e => e.Signups).SingleOrDefaultAsync(e => e.Id == eventId, ct);
		if (guildEvent is null || guildEvent.Cancelled)
		{
			return Result.NotFound(L.T($"Événement #{eventId} introuvable ou annulé.", $"Event #{eventId} not found or cancelled."));
		}
		var actor = await annuaire.DiscordActorAsync(discordId, false, ct);
		var chosen = actor.Choose(character);
		if (chosen is null)
		{
			return Result.Invalid(character is null
				? SharedMessages.NoLinkedCharacter(L)
				: SharedMessages.NotYourCharacter(L, character));
		}

		var existing = guildEvent.Signups.SingleOrDefault(s => s.Character == chosen.Name);
		var resolvedRole = role is not null && Roles.Contains(role) ? role : existing?.Role ?? DefaultRole(chosen.ClassFile);
		ApplySignup(guildEvent, new UploadSignup(guildEvent.Id, null, chosen.Name, chosen.ClassFile, resolvedRole,
			spec ?? existing?.Spec, response, note ?? existing?.Note, Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), (existing?.ResponseAt ?? 0) + 1)));
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T(
			$"**{chosen.Name}** — {ResponseLabel(response, L)} pour **{guildEvent.Title}** (<t:{guildEvent.StartsAt}:F>), rôle {RoleLabel(resolvedRole, L)}.",
			$"**{chosen.Name}** — {ResponseLabel(response, L)} for **{guildEvent.Title}** (<t:{guildEvent.StartsAt}:F>), role {RoleLabel(resolvedRole, L)}."));
	}

	// ---- Mêmes actions qu'en jeu, depuis Discord -----------------------------------------

	/// <summary>Qui agit sur Discord : règle commune tenue par l'annuaire (personnages liés, principal, officier).</summary>
	private Task<DiscordActor> ActorAsync(string discordId, bool isServerAdmin, CancellationToken ct) =>
		annuaire.DiscordActorAsync(discordId, isServerAdmin, ct);

	private static bool CanManage(GuildEvent guildEvent, DiscordActor actor) => actor.IsOfficer || actor.Owns(guildEvent.Organizer);

	private Result NotManager(GuildEvent guildEvent) => Result.Forbidden(L.T(
		$"Seuls l'organisateur ({guildEvent.Organizer}) et les officiers peuvent faire ça sur « {guildEvent.Title} ».",
		$"Only the organizer ({guildEvent.Organizer}) and officers can do this on \"{guildEvent.Title}\"."));

	private Result NotFound(int eventId) =>
		Result.NotFound(L.T($"Événement #{eventId} introuvable.", $"Event #{eventId} not found."));

	private static long NextAt(long previous) => Math.Max(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), previous + 1);

	/// <summary>Champs d'un événement ; null = inchangé (modification) ou valeur par défaut (création).</summary>
	public sealed record EventFields(
		string? Title = null, string? Kind = null, long? StartsAt = null, string? Description = null,
		int? Tank = null, int? Healer = null, int? Melee = null, int? Ranged = null, bool? Locked = null);

	private static void Apply(GuildEvent e, EventFields f)
	{
		if (Clip(f.Title, TitleMax) is { } title)
		{
			e.Title = title;
		}
		if (f.Kind is not null)
		{
			e.Kind = Kinds.Contains(f.Kind) ? f.Kind : "other";
		}
		if (f.StartsAt is { } startsAt)
		{
			e.StartsAt = startsAt;
		}
		if (f.Description is not null)
		{
			// Un tiret seul efface la description.
			e.Description = f.Description.Trim() == "-" ? null : Clip(f.Description, DescriptionMax);
		}
		e.TargetTank = f.Tank is { } tank ? Math.Clamp(tank, 0, TargetMax) : e.TargetTank;
		e.TargetHealer = f.Healer is { } healer ? Math.Clamp(healer, 0, TargetMax) : e.TargetHealer;
		e.TargetMelee = f.Melee is { } melee ? Math.Clamp(melee, 0, TargetMax) : e.TargetMelee;
		e.TargetRanged = f.Ranged is { } ranged ? Math.Clamp(ranged, 0, TargetMax) : e.TargetRanged;
		e.ReservesLocked = f.Locked ?? e.ReservesLocked;
	}

	public async Task<Result> CreateFromDiscordAsync(string discordId, bool isServerAdmin, EventFields fields,
		string? character, CancellationToken ct = default)
	{
		var actor = await ActorAsync(discordId, isServerAdmin, ct);
		var organizer = actor.Choose(character);
		if (organizer is null)
		{
			return Result.Invalid(character is null
				? SharedMessages.NoLinkedCharacter(L)
				: SharedMessages.NotYourCharacter(L, character));
		}
		if (Clip(fields.Title, TitleMax) is null || fields.StartsAt is null)
		{
			return Result.Invalid(L.T("Il faut un titre et une date.", "A title and a date are required."));
		}
		var guildEvent = new GuildEvent
		{
			Title = fields.Title!,
			Organizer = organizer.Name,
			CreatedUtc = DateTime.UtcNow,
			Source = DataSource.Discord,
			Kind = "raid",
		};
		Apply(guildEvent, fields);
		guildEvent.UpdatedAt = NextAt(0);
		db.Events.Add(guildEvent);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T(
			$"Événement **#{guildEvent.Id} · {guildEvent.Title}** créé pour <t:{guildEvent.StartsAt}:F> (<t:{guildEvent.StartsAt}:R>), organisé par **{organizer.Name}**.",
			$"Event **#{guildEvent.Id} · {guildEvent.Title}** created for <t:{guildEvent.StartsAt}:F> (<t:{guildEvent.StartsAt}:R>), organized by **{organizer.Name}**."));
	}

	public async Task<Result> EditFromDiscordAsync(string discordId, bool isServerAdmin, int eventId,
		EventFields fields, CancellationToken ct = default)
	{
		var guildEvent = await db.Events.SingleOrDefaultAsync(e => e.Id == eventId, ct);
		if (guildEvent is null)
		{
			return NotFound(eventId);
		}
		if (!CanManage(guildEvent, await ActorAsync(discordId, isServerAdmin, ct)))
		{
			return NotManager(guildEvent);
		}
		if (fields == new EventFields())
		{
			return Result.Invalid(L.T("Rien à modifier : précise au moins un champ.", "Nothing to change: give at least one field."));
		}
		Apply(guildEvent, fields);
		guildEvent.UpdatedAt = NextAt(guildEvent.UpdatedAt);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T(
			$"**#{guildEvent.Id} · {guildEvent.Title}** modifié : <t:{guildEvent.StartsAt}:F>.",
			$"**#{guildEvent.Id} · {guildEvent.Title}** updated: <t:{guildEvent.StartsAt}:F>."));
	}

	public async Task<Result> SetCancelledFromDiscordAsync(string discordId, bool isServerAdmin, int eventId,
		bool cancelled, CancellationToken ct = default)
	{
		var guildEvent = await db.Events.SingleOrDefaultAsync(e => e.Id == eventId, ct);
		if (guildEvent is null)
		{
			return NotFound(eventId);
		}
		if (!CanManage(guildEvent, await ActorAsync(discordId, isServerAdmin, ct)))
		{
			return NotManager(guildEvent);
		}
		if (guildEvent.Cancelled == cancelled)
		{
			return Result.Conflict(cancelled
				? L.T($"« {guildEvent.Title} » est déjà annulé.", $"\"{guildEvent.Title}\" is already cancelled.")
				: L.T($"« {guildEvent.Title} » n'est pas annulé.", $"\"{guildEvent.Title}\" is not cancelled."));
		}
		guildEvent.Cancelled = cancelled;
		guildEvent.UpdatedAt = NextAt(guildEvent.UpdatedAt);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(cancelled
			? L.T($"**#{guildEvent.Id} · {guildEvent.Title}** est annulé.", $"**#{guildEvent.Id} · {guildEvent.Title}** is cancelled.")
			: L.T($"**#{guildEvent.Id} · {guildEvent.Title}** est rétabli.", $"**#{guildEvent.Id} · {guildEvent.Title}** is back on."));
	}

	public async Task<Result> PlaceFromDiscordAsync(string discordId, bool isServerAdmin, int eventId,
		string character, string placement, CancellationToken ct = default)
	{
		var guildEvent = await db.Events.Include(e => e.Signups).SingleOrDefaultAsync(e => e.Id == eventId, ct);
		if (guildEvent is null)
		{
			return NotFound(eventId);
		}
		if (!CanManage(guildEvent, await ActorAsync(discordId, isServerAdmin, ct)))
		{
			return NotManager(guildEvent);
		}
		if (!Placements.Contains(placement))
		{
			return Result.Invalid(L.T("Place inconnue.", "Unknown placement."));
		}
		// Nom exact de l'inscrit si on le retrouve, sinon un membre connu de la guilde.
		var name = guildEvent.Signups.FirstOrDefault(s => string.Equals(s.Character, character.Trim(), StringComparison.OrdinalIgnoreCase))?.Character
			?? await annuaire.FindCharacterNameAsync(character, ct);
		if (name is null)
		{
			return Result.NotFound(L.T($"**{character}** n'est ni inscrit ni connu de la guilde.", $"**{character}** is neither signed up nor known to the guild."));
		}
		var row = guildEvent.Signups.SingleOrDefault(s => s.Character == name);
		ApplyPlacement(guildEvent, new UploadPlacement(eventId, null, name, placement, "", NextAt(row?.PlacementAt ?? 0)));
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T(
			$"**{name}** : {PlacementLabel(placement, L)} pour **{guildEvent.Title}**.",
			$"**{name}**: {PlacementLabel(placement, L)} for **{guildEvent.Title}**."));
	}

	public async Task<GuildEvent?> GetAsync(int eventId, CancellationToken ct = default) =>
		await WithDetails(db.Events.AsNoTracking()).SingleOrDefaultAsync(e => e.Id == eventId, ct);

	private static IQueryable<GuildEvent> WithDetails(IQueryable<GuildEvent> query) => query
		.Include(e => e.Signups)
		.Include(e => e.Reserves)
		.Include(e => e.HardReserves)
		.Include(e => e.Duties)
		.AsSplitQuery();

	/// <summary>Rôle probable d'une classe, faute de mieux (le joueur le corrige en s'inscrivant).</summary>
	public static string DefaultRole(string? classFile) => classFile switch
	{
		"MAGE" or "WARLOCK" or "HUNTER" => "ranged",
		"PRIEST" => "healer",
		_ => "melee",
	};
}
