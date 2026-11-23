using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Presence.Application;

/// <summary>Carte du tableau de bord : ta présence en raid sur 30 jours et ton butin sur 90 jours.</summary>
public sealed class PresenceHomeCard(AttendanceService attendance, IAnnuaire annuaire, BotLocalizer l) : IHomeContributor
{
	public string Module => "Presence";

	public int Order => 40;

	public async Task<HomeCard?> BuildAsync(HomeViewer viewer, CancellationToken ct)
	{
		var main = (await annuaire.DiscordActorAsync(viewer.DiscordId, viewer.IsServerAdmin, ct)).Main?.Name;
		if (main is null)
		{
			return null;
		}
		var sessions = await attendance.RecentAsync(TimeSpan.FromDays(90), ct);
		var stats = AttendanceStats.StatsFor(sessions, main, DateTimeOffset.UtcNow);
		if (stats.Last90.Eligible == 0)
		{
			return null; // pas encore de raid de guilde relevé pour ce joueur
		}
		var lines = new List<string>
		{
			l.T($"**{main}** : {AttendanceStats.Percent(stats.Last30)} sur 30 jours ({stats.Last30.Attended}/{stats.Last30.Eligible} raids)",
				$"**{main}**: {AttendanceStats.Percent(stats.Last30)} over 30 days ({stats.Last30.Attended}/{stats.Last30.Eligible} raids)"),
			l.T($"Butin épique sur 90 jours : {stats.Loot90}", $"Epic loot over 90 days: {stats.Loot90}"),
		};
		return new HomeCard(l.T("Présence en raid", "Raid attendance"), lines, l.T("/gkeep presence joueur", "/gkeep attendance player"));
	}
}
