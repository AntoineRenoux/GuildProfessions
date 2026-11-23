using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Evenements.Contracts;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Evenements.Application;

/// <summary>Implémentation du contrat public <see cref="IEvenements"/>.</summary>
public sealed class EvenementsQueries(AppDbContext db) : IEvenements
{
	/// <summary>Sortes d'événements auxquelles un raid relevé peut se rattacher.</summary>
	private static readonly string[] RaidKinds = ["raid", "dungeon"];

	public async Task<EventBrief?> EventBriefAsync(int eventId, CancellationToken ct = default) =>
		await db.Events.AsNoTracking()
			.Where(e => e.Id == eventId)
			.Select(e => new EventBrief(e.Id, e.Title, e.StartsAt, e.Cancelled))
			.SingleOrDefaultAsync(ct);

	public async Task<IReadOnlyList<EventBrief>> UpcomingBriefsAsync(int max, CancellationToken ct = default)
	{
		var since = DateTimeOffset.UtcNow.AddHours(-4).ToUnixTimeSeconds();
		return await db.Events.AsNoTracking()
			.Where(e => !e.Cancelled && e.StartsAt >= since)
			.OrderBy(e => e.StartsAt)
			.Take(max)
			.Select(e => new EventBrief(e.Id, e.Title, e.StartsAt, e.Cancelled))
			.ToListAsync(ct);
	}

	public async Task<int?> EventOfRaidAsync(long startedAt, TimeSpan window, IReadOnlyCollection<string> participants, CancellationToken ct = default)
	{
		var seconds = (long)window.TotalSeconds;
		var candidates = await db.Events.AsNoTracking()
			.Where(e => !e.Cancelled && RaidKinds.Contains(e.Kind) && e.StartsAt >= startedAt - seconds && e.StartsAt <= startedAt + seconds)
			.Select(e => new
			{
				e.Id,
				e.StartsAt,
				Players = e.Signups.Where(s => s.Response == "yes" || s.Placement == "confirmed").Select(s => s.Character).ToList(),
			})
			.ToListAsync(ct);
		return candidates
			.OrderByDescending(e => e.Players.Count(participants.Contains))
			.ThenBy(e => Math.Abs(e.StartsAt - startedAt))
			.Select(e => (int?)e.Id)
			.FirstOrDefault();
	}
}
