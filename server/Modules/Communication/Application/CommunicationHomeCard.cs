using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Communication.Application;

/// <summary>Carte du tableau de bord : dernières annonces et objectifs en cours.</summary>
public sealed class CommunicationHomeCard(CommunicationService communication, BotLocalizer l) : IHomeContributor
{
	public string Module => "Communication";

	public int Order => 20;

	public async Task<HomeCard?> BuildAsync(HomeViewer viewer, CancellationToken ct)
	{
		var lines = new List<string>();
		foreach (var a in await communication.RecentAnnouncementsAsync(2, ct))
		{
			lines.Add($"📣 **{Text.Ellipsize(a.Title, 80)}** · {a.Author} · <t:{a.CreatedAt}:R>");
		}
		foreach (var g in (await communication.GoalsAsync(includeClosed: false, ct)).Take(2))
		{
			var progress = g.Contributions.Sum(c => c.Amount);
			var percent = g.Target > 0 ? Math.Min(100, 100 * progress / g.Target) : 0;
			lines.Add($"🎯 **{Text.Ellipsize(g.Title, 60)}** · {progress}/{g.Target}{(g.Unit is null ? "" : " " + g.Unit)} ({percent} %)");
		}
		if (lines.Count == 0)
		{
			return null;
		}
		return new HomeCard(l.T("Vie de la guilde", "Guild news"), lines, l.T("/gkeep annonce liste", "/gkeep announce list"));
	}
}
