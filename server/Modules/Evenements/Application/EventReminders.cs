using GuildProfessions.Server.Data;
using GuildProfessions.Server.Shared;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Evenements.Application;

/// <summary>Personnes à rappeler pour un événement : celles qui ont répondu présent ou peut-être, hors banc.</summary>
public sealed record ReminderTargets(GuildEvent Event, IReadOnlyList<string> Characters);

public sealed partial class EventService
{
	/// <summary>
	/// Rappel d'un événement aux inscrits (équivalent du rappel en jeu dans le
	/// canal de guilde) : réservé à l'organisateur et aux officiers, sur un
	/// événement à venir et non annulé.
	/// </summary>
	public async Task<(Result Result, ReminderTargets? Targets)> ReminderTargetsAsync(string discordId, bool isServerAdmin, int eventId,
		CancellationToken ct = default)
	{
		var guildEvent = await db.Events.AsNoTracking().Include(e => e.Signups).SingleOrDefaultAsync(e => e.Id == eventId, ct);
		if (guildEvent is null)
		{
			return (NotFound(eventId), null);
		}
		if (!CanManage(guildEvent, await ActorAsync(discordId, isServerAdmin, ct)))
		{
			return (NotManager(guildEvent), null);
		}
		if (guildEvent.Cancelled || guildEvent.StartsAt < DateTimeOffset.UtcNow.AddHours(-4).ToUnixTimeSeconds())
		{
			return (Result.Conflict(L.T("Cet événement est annulé ou déjà passé.", "This event is cancelled or already over.")), null);
		}
		var characters = guildEvent.Signups
			.Where(s => (s.Response is "yes" or "tentative") && s.Placement != "bench")
			.Select(s => s.Character)
			.OrderBy(c => c)
			.ToList();
		if (characters.Count == 0)
		{
			return (Result.Conflict(L.T("Personne n'est inscrit à rappeler.", "Nobody signed up to remind.")), null);
		}
		return (Result.Success(""), new ReminderTargets(guildEvent, characters));
	}
}
