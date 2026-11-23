using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Modules.Evenements.Contracts;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.EntityFrameworkCore;
using static GuildProfessions.Server.Shared.Text;

namespace GuildProfessions.Server.Modules.Communication.Application;

/// <summary>
/// Communication de la guilde : annonces (avec modèles), charte (règlement,
/// règles de butin…) et objectifs chiffrés auxquels les membres contribuent.
/// Règles, identiques en jeu et sur Discord :
/// - annonces, modèles, charte et objectifs : officiers seulement ;
/// - contributions : chacun pour ses propres personnages, sur un objectif ouvert.
/// Chaque changement venu du jeu est horodaté : le plus récent gagne.
/// </summary>
public sealed class CommunicationService(
	AppDbContext db, IAnnuaire annuaire, IEvenements evenements, IDataVersion dataVersion,
	IAnnouncementPublisher publisher, GuildContext guildContext, BotLocalizer localizer,
	ILogger<CommunicationService> logger)
{
	private BotLocalizer L => localizer;

	public const int TitleMax = 100;
	public const int BodyMax = 2000;
	public const int HandbookBodyMax = 4000;
	public const int KeyMax = 40;
	public const int NoteMax = 120;
	public const int UnitMax = 20;
	public const int TargetMax = 1_000_000;

	/// <summary>Annonces encore exportées vers le jeu.</summary>
	public static readonly TimeSpan AnnouncementWindow = TimeSpan.FromDays(60);

	/// <summary>Objectifs clos encore exportés.</summary>
	public static readonly TimeSpan ClosedGoalWindow = TimeSpan.FromDays(30);

	// ---- Envoi depuis le jeu -----------------------------------------------------------------

	public async Task<List<string>> ApplyUploadAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct = default)
	{
		var warnings = new List<string>();
		if (context.Payload.Communication is not { } upload)
		{
			return warnings;
		}
		var identity = await annuaire.UploadIdentityAsync(context, ct);
		var changed = false;

		foreach (var announcement in upload.Announcements ?? [])
		{
			if (!identity.IsOfficer(announcement.By))
			{
				warnings.Add(OfficersOnly(L.T("Annonce", "Announcement"), announcement.Title));
				continue;
			}
			changed |= await ApplyAnnouncementAsync(announcement, afterCommit, ct);
		}
		foreach (var template in upload.Templates ?? [])
		{
			if (!identity.IsOfficer(template.By))
			{
				warnings.Add(OfficersOnly(L.T("Modèle", "Template"), template.Key));
				continue;
			}
			changed |= await ApplyTemplateAsync(template, ct);
		}
		foreach (var section in upload.Handbook ?? [])
		{
			if (!identity.IsOfficer(section.By))
			{
				warnings.Add(OfficersOnly(L.T("Charte", "Handbook"), section.Title));
				continue;
			}
			changed |= await ApplyHandbookAsync(section, ct);
		}
		foreach (var goal in upload.Goals ?? [])
		{
			if (!identity.IsOfficer(goal.By))
			{
				warnings.Add(OfficersOnly(L.T("Objectif", "Goal"), goal.Title));
				continue;
			}
			changed |= await ApplyGoalAsync(goal, ct);
			await db.SaveChangesAsync(ct); // un objectif créé ici peut recevoir une contribution plus bas
		}
		foreach (var contribution in upload.Contributions ?? [])
		{
			if (!identity.Owns(contribution.Character))
			{
				warnings.Add(L.T($"Contribution de {contribution.Character} ignorée : personnage non lié à cet envoi.",
					$"Contribution from {contribution.Character} ignored: character not part of this upload."));
				continue;
			}
			var goal = await FindGoalAsync(contribution.GoalId, contribution.GoalClientId, ct);
			if (goal is null)
			{
				continue;
			}
			if (ContributionProblem(goal, contribution.Amount) is { } problem)
			{
				warnings.Add(problem);
				continue;
			}
			changed |= AddContribution(goal, contribution.ClientId, contribution.Character, contribution.Amount, contribution.Note, contribution.At);
		}

		if (changed)
		{
			await dataVersion.SaveAndBumpAsync(ct);
		}
		return warnings;
	}

	private string OfficersOnly(string what, string name) => L.T(
		$"{what} « {name} » ignoré(e) : réservé aux officiers.",
		$"{what} \"{name}\" ignored: officers only.");

	private async Task<bool> ApplyAnnouncementAsync(UploadAnnouncement uploaded, ICollection<Func<Task>> afterCommit, CancellationToken ct)
	{
		var existing = uploaded.ServerId is { } id
			? await db.Announcements.SingleOrDefaultAsync(a => a.Id == id, ct)
			: db.Announcements.Local.FirstOrDefault(a => a.ClientId == uploaded.ClientId) ??
				await db.Announcements.SingleOrDefaultAsync(a => a.ClientId == uploaded.ClientId, ct);
		var title = Clip(uploaded.Title, TitleMax);
		var body = Clip(uploaded.Body, BodyMax);
		if (title is null || body is null)
		{
			return false;
		}
		if (existing is null)
		{
			if (uploaded.ServerId is not null || uploaded.Deleted)
			{
				return false; // annonce inconnue (supprimée ou d'une autre guilde)
			}
			existing = new Announcement
			{
				ClientId = uploaded.ClientId,
				Title = title,
				Body = body,
				Author = uploaded.By,
				EventId = uploaded.EventId,
				CreatedAt = uploaded.At,
				UpdatedAt = uploaded.At,
				Source = DataSource.Addon,
			};
			db.Announcements.Add(existing);
			// Publiée sur Discord une fois l'envoi validé, sans jamais le faire échouer.
			afterCommit.Add(() => PublishSafelyAsync(existing.Title, existing.Body, existing.Author, CancellationToken.None));
			return true;
		}
		if (uploaded.At <= existing.UpdatedAt)
		{
			return false;
		}
		existing.Title = title;
		existing.Body = body;
		existing.EventId = uploaded.EventId;
		existing.Deleted = uploaded.Deleted;
		existing.UpdatedAt = uploaded.At;
		return true;
	}

	private async Task<bool> ApplyTemplateAsync(UploadTemplate uploaded, CancellationToken ct)
	{
		var key = NormalizeKey(uploaded.Key);
		var title = Clip(uploaded.Title, TitleMax);
		var body = Clip(uploaded.Body, BodyMax);
		if (key is null || title is null || body is null)
		{
			return false;
		}
		// Même clé plus haut dans ce même envoi : la ligne n'est pas encore en base.
		var existing = db.AnnouncementTemplates.Local.FirstOrDefault(t => t.Key == key) ??
			await db.AnnouncementTemplates.SingleOrDefaultAsync(t => t.Key == key, ct);
		if (existing is not null && uploaded.At <= existing.UpdatedAt)
		{
			return false;
		}
		if (existing is null)
		{
			existing = new AnnouncementTemplate { Key = key, Title = title, Body = body };
			db.AnnouncementTemplates.Add(existing);
		}
		existing.Title = title;
		existing.Body = body;
		existing.Deleted = uploaded.Deleted;
		existing.UpdatedAt = uploaded.At;
		return true;
	}

	private async Task<bool> ApplyHandbookAsync(UploadHandbookSection uploaded, CancellationToken ct)
	{
		var key = NormalizeKey(uploaded.Key);
		var title = Clip(uploaded.Title, TitleMax);
		var body = Clip(uploaded.Body, HandbookBodyMax);
		if (key is null || title is null || (body is null && !uploaded.Deleted))
		{
			return false;
		}
		var existing = db.HandbookSections.Local.FirstOrDefault(h => h.Key == key) ??
			await db.HandbookSections.SingleOrDefaultAsync(h => h.Key == key, ct);
		if (existing is not null && uploaded.At <= existing.UpdatedAt)
		{
			return false;
		}
		if (existing is null)
		{
			existing = new HandbookSection { Key = key, Title = title, Body = body ?? "" };
			db.HandbookSections.Add(existing);
		}
		existing.Title = title;
		existing.Body = body ?? existing.Body;
		existing.Order = Math.Clamp(uploaded.Order, 0, 100);
		existing.Deleted = uploaded.Deleted;
		existing.UpdatedBy = uploaded.By;
		existing.UpdatedAt = uploaded.At;
		return true;
	}

	private async Task<bool> ApplyGoalAsync(UploadGoal uploaded, CancellationToken ct)
	{
		var title = Clip(uploaded.Title, TitleMax);
		if (title is null || uploaded.Target <= 0)
		{
			return false;
		}
		var existing = await FindGoalAsync(uploaded.ServerId, uploaded.ClientId, ct);
		if (existing is null)
		{
			if (uploaded.ServerId is not null)
			{
				return false;
			}
			existing = new GuildGoal { ClientId = uploaded.ClientId, Title = title, CreatedBy = uploaded.By };
			db.GuildGoals.Add(existing);
		}
		else if (uploaded.At <= existing.UpdatedAt)
		{
			return false;
		}
		existing.Title = title;
		existing.Description = Clip(uploaded.Description, BodyMax);
		existing.Target = Math.Clamp(uploaded.Target, 1, TargetMax);
		existing.Unit = Clip(uploaded.Unit, UnitMax);
		existing.Deadline = uploaded.Deadline is > 0 ? uploaded.Deadline : null;
		existing.Closed = uploaded.Closed;
		existing.UpdatedAt = uploaded.At;
		return true;
	}

	private string? ContributionProblem(GuildGoal goal, int amount)
	{
		if (goal.Closed)
		{
			return L.T($"Objectif « {goal.Title} » clos : contribution refusée.", $"Goal \"{goal.Title}\" is closed: contribution refused.");
		}
		if (amount is <= 0 or > TargetMax)
		{
			return L.T("Contribution invalide : la quantité doit être positive.", "Invalid contribution: the amount must be positive.");
		}
		return null;
	}

	private static bool AddContribution(GuildGoal goal, string clientId, string character, int amount, string? note, long at)
	{
		if (goal.Contributions.Any(c => c.ClientId == clientId))
		{
			return false; // déjà reçue par un envoi précédent
		}
		goal.Contributions.Add(new GoalContribution
		{
			ClientId = clientId,
			Character = character,
			Amount = amount,
			Note = Clip(note, NoteMax),
			At = at,
		});
		return true;
	}

	private async Task<GuildGoal?> FindGoalAsync(int? serverId, string? clientId, CancellationToken ct)
	{
		if (serverId is not null)
		{
			return await db.GuildGoals.Include(g => g.Contributions).SingleOrDefaultAsync(g => g.Id == serverId, ct);
		}
		if (clientId is null)
		{
			return null;
		}
		return db.GuildGoals.Local.FirstOrDefault(g => g.ClientId == clientId) ??
			await db.GuildGoals.Include(g => g.Contributions).SingleOrDefaultAsync(g => g.ClientId == clientId, ct);
	}

	/// <summary>Clé de modèle ou de section : minuscules, sans espaces autour.</summary>
	public static string? NormalizeKey(string? key) => Clip(key, KeyMax)?.ToLowerInvariant();

	// ---- Export ----------------------------------------------------------------------------------

	public async Task<ExportCommunicationDto> BuildExportAsync(CancellationToken ct = default)
	{
		var announcementsSince = DateTimeOffset.UtcNow.Add(-AnnouncementWindow).ToUnixTimeSeconds();
		var closedSince = DateTimeOffset.UtcNow.Add(-ClosedGoalWindow).ToUnixTimeSeconds();
		var announcements = await db.Announcements.AsNoTracking()
			.Where(a => !a.Deleted && a.CreatedAt >= announcementsSince)
			.OrderByDescending(a => a.CreatedAt)
			.Take(50)
			.Select(a => new ExportAnnouncementDto(a.Id, a.ClientId, a.Title, a.Body, a.Author, a.EventId, a.CreatedAt, a.UpdatedAt))
			.ToListAsync(ct);
		var templates = await db.AnnouncementTemplates.AsNoTracking()
			.Where(t => !t.Deleted)
			.OrderBy(t => t.Key)
			.Select(t => new ExportTemplateDto(t.Key, t.Title, t.Body, t.UpdatedAt))
			.ToListAsync(ct);
		var handbook = await db.HandbookSections.AsNoTracking()
			.Where(h => !h.Deleted)
			.OrderBy(h => h.Order).ThenBy(h => h.Key)
			.Select(h => new ExportHandbookSectionDto(h.Key, h.Title, h.Body, h.Order, h.UpdatedBy, h.UpdatedAt))
			.ToListAsync(ct);
		var goals = await db.GuildGoals.AsNoTracking()
			.Include(g => g.Contributions)
			.Where(g => !g.Closed || g.UpdatedAt >= closedSince)
			.OrderBy(g => g.Closed).ThenBy(g => g.Deadline ?? long.MaxValue).ThenBy(g => g.Id)
			.ToListAsync(ct);
		return new ExportCommunicationDto(announcements, templates, handbook, goals.Select(ToExport).ToList());
	}

	private static ExportGoalDto ToExport(GuildGoal g) => new(
		g.Id, g.ClientId, g.Title, g.Description, g.Target, g.Unit, g.Deadline, g.Closed, g.CreatedBy, g.UpdatedAt,
		g.Contributions.Sum(c => c.Amount),
		g.Contributions.OrderByDescending(c => c.At).Take(50)
			.Select(c => new ExportContributionDto(c.ClientId, c.Character, c.Amount, c.Note, c.At)).ToList());

	// ---- Lecture pour Discord ---------------------------------------------------------------------

	public async Task<List<Announcement>> RecentAnnouncementsAsync(int limit = 10, CancellationToken ct = default) =>
		await db.Announcements.AsNoTracking().Where(a => !a.Deleted)
			.OrderByDescending(a => a.CreatedAt).Take(limit).ToListAsync(ct);

	public async Task<List<AnnouncementTemplate>> TemplatesAsync(CancellationToken ct = default) =>
		await db.AnnouncementTemplates.AsNoTracking().Where(t => !t.Deleted).OrderBy(t => t.Key).ToListAsync(ct);

	public async Task<List<HandbookSection>> HandbookAsync(CancellationToken ct = default) =>
		await db.HandbookSections.AsNoTracking().Where(h => !h.Deleted).OrderBy(h => h.Order).ThenBy(h => h.Key).ToListAsync(ct);

	public async Task<List<GuildGoal>> GoalsAsync(bool includeClosed, CancellationToken ct = default) =>
		await db.GuildGoals.AsNoTracking().Include(g => g.Contributions)
			.Where(g => includeClosed || !g.Closed)
			.OrderBy(g => g.Closed).ThenBy(g => g.Deadline ?? long.MaxValue).ThenBy(g => g.Id)
			.ToListAsync(ct);

	public async Task<GuildGoal?> GoalAsync(int id, CancellationToken ct = default) =>
		await db.GuildGoals.AsNoTracking().Include(g => g.Contributions).SingleOrDefaultAsync(g => g.Id == id, ct);

	// ---- Actions depuis Discord ---------------------------------------------------------------------

	private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	private static long NextAt(long previous) => Math.Max(Now(), previous + 1);

	private Result NotOfficer() => Result.Forbidden(L.T("Réservé aux officiers.", "Officers only."));

	private static string AuthorOf(DiscordActor actor) => actor.Main?.Name ?? actor.DiscordName ?? "Discord";

	/// <summary>
	/// Remplace {event}, {date} et {guild} dans un texte d'annonce. Sur
	/// Discord, la date devient un horodatage affiché dans le fuseau de chacun.
	/// </summary>
	public static string Render(string text, EventBrief? brief, string guildName) => text
		.Replace("{event}", brief?.Title ?? "", StringComparison.OrdinalIgnoreCase)
		.Replace("{date}", brief is null ? "" : $"<t:{brief.StartsAt}:F>", StringComparison.OrdinalIgnoreCase)
		.Replace("{guild}", guildName, StringComparison.OrdinalIgnoreCase);

	public async Task<Result> PostFromDiscordAsync(string discordId, bool isServerAdmin, string? title, string? body,
		string? templateKey, int? eventId, CancellationToken ct = default)
	{
		var actor = await annuaire.DiscordActorAsync(discordId, isServerAdmin, ct);
		if (!actor.IsOfficer)
		{
			return NotOfficer();
		}
		if (templateKey is not null)
		{
			var key = NormalizeKey(templateKey);
			var template = await db.AnnouncementTemplates.AsNoTracking().SingleOrDefaultAsync(t => t.Key == key && !t.Deleted, ct);
			if (template is null)
			{
				return Result.NotFound(L.T($"Modèle « {templateKey} » introuvable.", $"Template \"{templateKey}\" not found."));
			}
			title ??= template.Title;
			body ??= template.Body;
		}
		EventBrief? brief = null;
		if (eventId is { } id)
		{
			brief = await evenements.EventBriefAsync(id, ct);
			if (brief is null)
			{
				return Result.NotFound(L.T($"Événement #{id} introuvable.", $"Event #{id} not found."));
			}
		}
		var guildName = guildContext.Required.Name;
		var finalTitle = Clip(title is null ? null : Render(title, brief, guildName), TitleMax);
		var finalBody = Clip(body is null ? null : Render(body, brief, guildName), BodyMax);
		if (finalTitle is null || finalBody is null)
		{
			return Result.Invalid(L.T("Il faut un titre et un texte (ou un modèle).", "A title and a text (or a template) are required."));
		}
		var announcement = new Announcement
		{
			Title = finalTitle,
			Body = finalBody,
			Author = AuthorOf(actor),
			EventId = eventId,
			CreatedAt = Now(),
			UpdatedAt = Now(),
			Source = DataSource.Discord,
		};
		db.Announcements.Add(announcement);
		await dataVersion.SaveAndBumpAsync(ct);
		var channel = await AnnouncementsChannelAsync(ct);
		await PublishSafelyAsync(announcement.Title, announcement.Body, announcement.Author, ct);
		return Result.Success(L.T(
			$"Annonce **#{announcement.Id} · {announcement.Title}** publiée{(channel is null ? " (aucun salon d'annonces réglé : `/gkeep-guilde annonce salon`)" : $" dans <#{channel}>")}. Elle s'affichera en jeu à la prochaine synchro.",
			$"Announcement **#{announcement.Id} · {announcement.Title}** posted{(channel is null ? " (no announcements channel set: `/gkeep-guild announce channel`)" : $" in <#{channel}>")}. It will show in game at the next sync."));
	}

	public async Task<Result> DeleteAnnouncementFromDiscordAsync(string discordId, bool isServerAdmin, int id, CancellationToken ct = default)
	{
		var actor = await annuaire.DiscordActorAsync(discordId, isServerAdmin, ct);
		if (!actor.IsOfficer)
		{
			return NotOfficer();
		}
		var announcement = await db.Announcements.SingleOrDefaultAsync(a => a.Id == id && !a.Deleted, ct);
		if (announcement is null)
		{
			return Result.NotFound(L.T($"Annonce #{id} introuvable.", $"Announcement #{id} not found."));
		}
		announcement.Deleted = true;
		announcement.UpdatedAt = NextAt(announcement.UpdatedAt);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T($"Annonce #{id} retirée.", $"Announcement #{id} removed."));
	}

	public async Task<Result> SaveTemplateFromDiscordAsync(string discordId, bool isServerAdmin, string key, string? title, string? body,
		bool delete, CancellationToken ct = default)
	{
		var actor = await annuaire.DiscordActorAsync(discordId, isServerAdmin, ct);
		if (!actor.IsOfficer)
		{
			return NotOfficer();
		}
		var normalized = NormalizeKey(key);
		if (normalized is null)
		{
			return Result.Invalid(L.T("Nom de modèle vide.", "Empty template name."));
		}
		var existing = await db.AnnouncementTemplates.SingleOrDefaultAsync(t => t.Key == normalized, ct);
		if (delete)
		{
			if (existing is null || existing.Deleted)
			{
				return Result.NotFound(L.T($"Modèle « {normalized} » introuvable.", $"Template \"{normalized}\" not found."));
			}
			existing.Deleted = true;
			existing.UpdatedAt = NextAt(existing.UpdatedAt);
			await dataVersion.SaveAndBumpAsync(ct);
			return Result.Success(L.T($"Modèle « {normalized} » supprimé.", $"Template \"{normalized}\" deleted."));
		}
		var cleanTitle = Clip(title, TitleMax);
		var cleanBody = Clip(body, BodyMax);
		if (cleanTitle is null || cleanBody is null)
		{
			return Result.Invalid(L.T("Il faut un titre et un texte.", "A title and a text are required."));
		}
		if (existing is null)
		{
			existing = new AnnouncementTemplate { Key = normalized, Title = cleanTitle, Body = cleanBody };
			db.AnnouncementTemplates.Add(existing);
		}
		existing.Title = cleanTitle;
		existing.Body = cleanBody;
		existing.Deleted = false;
		existing.UpdatedAt = NextAt(existing.UpdatedAt);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T(
			$"Modèle « {normalized} » enregistré. {{event}}, {{date}} et {{guild}} seront remplacés à la publication.",
			$"Template \"{normalized}\" saved. {{event}}, {{date}} and {{guild}} are replaced when posting."));
	}

	public async Task<Result> SetChannelFromDiscordAsync(bool isServerAdmin, string? channelId, CancellationToken ct = default)
	{
		if (!isServerAdmin)
		{
			return Result.Forbidden(L.T("Réservé à qui gère le serveur Discord.", "Only server managers can do this."));
		}
		var settings = await db.CommunicationSettings.SingleOrDefaultAsync(ct);
		if (settings is null)
		{
			settings = new CommunicationSettings();
			db.CommunicationSettings.Add(settings);
		}
		settings.AnnouncementsChannelId = channelId;
		await db.SaveChangesAsync(ct);
		return Result.Success(channelId is null
			? L.T("Les annonces ne seront plus publiées sur Discord.", "Announcements will no longer be posted on Discord.")
			: L.T($"Les annonces seront publiées dans <#{channelId}>.", $"Announcements will be posted in <#{channelId}>."));
	}

	public async Task<Result> EditHandbookFromDiscordAsync(string discordId, bool isServerAdmin, string key, string? title, string? body,
		int? order, bool delete, CancellationToken ct = default)
	{
		var actor = await annuaire.DiscordActorAsync(discordId, isServerAdmin, ct);
		if (!actor.IsOfficer)
		{
			return NotOfficer();
		}
		var normalized = NormalizeKey(key);
		if (normalized is null)
		{
			return Result.Invalid(L.T("Nom de section vide.", "Empty section name."));
		}
		var existing = await db.HandbookSections.SingleOrDefaultAsync(h => h.Key == normalized, ct);
		if (delete)
		{
			if (existing is null || existing.Deleted)
			{
				return Result.NotFound(L.T($"Section « {normalized} » introuvable.", $"Section \"{normalized}\" not found."));
			}
			existing.Deleted = true;
			existing.UpdatedAt = NextAt(existing.UpdatedAt);
			existing.UpdatedBy = AuthorOf(actor);
			await dataVersion.SaveAndBumpAsync(ct);
			return Result.Success(L.T($"Section « {existing.Title} » retirée de la charte.", $"Section \"{existing.Title}\" removed from the handbook."));
		}
		var cleanBody = Clip(body, HandbookBodyMax);
		if (cleanBody is null)
		{
			return Result.Invalid(L.T("Le texte de la section est vide.", "The section text is empty."));
		}
		if (existing is null)
		{
			existing = new HandbookSection { Key = normalized, Title = Clip(title, TitleMax) ?? normalized, Body = cleanBody };
			db.HandbookSections.Add(existing);
		}
		existing.Title = Clip(title, TitleMax) ?? existing.Title;
		existing.Body = cleanBody;
		existing.Order = order is { } o ? Math.Clamp(o, 0, 100) : existing.Order;
		existing.Deleted = false;
		existing.UpdatedBy = AuthorOf(actor);
		existing.UpdatedAt = NextAt(existing.UpdatedAt);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T($"Section « {existing.Title} » de la charte enregistrée.", $"Handbook section \"{existing.Title}\" saved."));
	}

	public async Task<Result> CreateGoalFromDiscordAsync(string discordId, bool isServerAdmin, string title, int target, string? unit,
		long? deadline, string? description, CancellationToken ct = default)
	{
		var actor = await annuaire.DiscordActorAsync(discordId, isServerAdmin, ct);
		if (!actor.IsOfficer)
		{
			return NotOfficer();
		}
		var cleanTitle = Clip(title, TitleMax);
		if (cleanTitle is null || target <= 0)
		{
			return Result.Invalid(L.T("Il faut un titre et une cible positive.", "A title and a positive target are required."));
		}
		var goal = new GuildGoal
		{
			Title = cleanTitle,
			Description = Clip(description, BodyMax),
			Target = Math.Min(target, TargetMax),
			Unit = Clip(unit, UnitMax),
			Deadline = deadline,
			CreatedBy = AuthorOf(actor),
			UpdatedAt = Now(),
		};
		db.GuildGoals.Add(goal);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(L.T(
			$"Objectif **#{goal.Id} · {goal.Title}** créé : {goal.Target} {goal.Unit}. Contributions : `/gkeep-guilde objectif contribuer`.",
			$"Goal **#{goal.Id} · {goal.Title}** created: {goal.Target} {goal.Unit}. Contribute with `/gkeep-guild goal contribute`."));
	}

	public async Task<Result> ContributeFromDiscordAsync(string discordId, int goalId, int amount, string? note, string? character,
		CancellationToken ct = default)
	{
		var actor = await annuaire.DiscordActorAsync(discordId, false, ct);
		var chosen = actor.Choose(character);
		if (chosen is null)
		{
			return Result.Invalid(character is null ? SharedMessages.NoLinkedCharacter(L) : SharedMessages.NotYourCharacter(L, character));
		}
		var goal = await db.GuildGoals.Include(g => g.Contributions).SingleOrDefaultAsync(g => g.Id == goalId, ct);
		if (goal is null)
		{
			return Result.NotFound(L.T($"Objectif #{goalId} introuvable.", $"Goal #{goalId} not found."));
		}
		if (ContributionProblem(goal, amount) is { } problem)
		{
			return Result.Conflict(problem);
		}
		AddContribution(goal, $"discord-{Guid.NewGuid():N}", chosen.Name, amount, note, Now());
		await dataVersion.SaveAndBumpAsync(ct);
		var progress = goal.Contributions.Sum(c => c.Amount);
		return Result.Success(L.T(
			$"**{chosen.Name}** contribue {amount} {goal.Unit} à **{goal.Title}** : {progress}/{goal.Target}.",
			$"**{chosen.Name}** contributes {amount} {goal.Unit} to **{goal.Title}**: {progress}/{goal.Target}."));
	}

	public async Task<Result> SetGoalClosedFromDiscordAsync(string discordId, bool isServerAdmin, int goalId, bool closed, CancellationToken ct = default)
	{
		var actor = await annuaire.DiscordActorAsync(discordId, isServerAdmin, ct);
		if (!actor.IsOfficer)
		{
			return NotOfficer();
		}
		var goal = await db.GuildGoals.SingleOrDefaultAsync(g => g.Id == goalId, ct);
		if (goal is null)
		{
			return Result.NotFound(L.T($"Objectif #{goalId} introuvable.", $"Goal #{goalId} not found."));
		}
		if (goal.Closed == closed)
		{
			return Result.Conflict(closed
				? L.T("Cet objectif est déjà clos.", "This goal is already closed.")
				: L.T("Cet objectif est déjà ouvert.", "This goal is already open."));
		}
		goal.Closed = closed;
		goal.UpdatedAt = NextAt(goal.UpdatedAt);
		await dataVersion.SaveAndBumpAsync(ct);
		return Result.Success(closed
			? L.T($"Objectif **{goal.Title}** clos.", $"Goal **{goal.Title}** closed.")
			: L.T($"Objectif **{goal.Title}** rouvert.", $"Goal **{goal.Title}** reopened."));
	}

	// ---- Publication Discord ------------------------------------------------------------------

	private async Task<string?> AnnouncementsChannelAsync(CancellationToken ct) =>
		await db.CommunicationSettings.AsNoTracking().Select(s => s.AnnouncementsChannelId).SingleOrDefaultAsync(ct);

	private async Task PublishSafelyAsync(string title, string body, string author, CancellationToken ct)
	{
		try
		{
			if (await AnnouncementsChannelAsync(ct) is { } channel)
			{
				await publisher.PublishAsync(channel, title, body, author, ct);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			// Salon supprimé, bot absent… l'annonce reste enregistrée et visible en jeu.
			logger.LogWarning(exception, "Publication Discord de l'annonce « {Title} » impossible.", title);
		}
	}

	/// <summary>Barre de progression texte (Discord) : ▰▰▰▱▱ 60 %.</summary>
	public static string ProgressBar(int progress, int target)
	{
		var ratio = target <= 0 ? 0 : Math.Clamp((double)progress / target, 0, 1);
		var filled = (int)Math.Round(ratio * 10);
		return new string('▰', filled) + new string('▱', 10 - filled) + $" {ratio * 100:0} %";
	}
}
