using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;
using static GuildProfessions.Server.Modules.Evenements.Application.EventLabels;

namespace GuildProfessions.Server.Modules.Evenements.Application;

/// <summary>Carte du tableau de bord : les trois prochains événements et ta réponse.</summary>
public sealed class EvenementsHomeCard(EventService events, IAnnuaire annuaire, BotLocalizer l) : IHomeContributor
{
	public string Module => "Evenements";

	public int Order => 10;

	public async Task<HomeCard?> BuildAsync(HomeViewer viewer, CancellationToken ct)
	{
		var mine = (await annuaire.CharacterNamesOfMemberAsync(viewer.DiscordId, ct)).ToHashSet();
		var upcoming = await events.UpcomingAsync(3, ct);
		var lines = upcoming.Select(e =>
		{
			var present = e.Signups.Count(s => s.Response == "yes");
			var answer = e.Signups.FirstOrDefault(s => mine.Contains(s.Character));
			var status = answer is null ? l.T("**pas encore répondu**", "**not answered yet**") : ResponseLabel(answer.Response, l);
			return $"<t:{e.StartsAt}:f> · **{Text.Ellipsize(e.Title, 60)}** · {present} {l.T("présents", "attending")} · {status}";
		}).ToList();
		if (lines.Count == 0)
		{
			lines.Add(l.T("Aucun événement à venir.", "No upcoming event."));
		}
		return new HomeCard(l.T("Prochains événements", "Upcoming events"), lines, l.T("/gkeep evenement inscription", "/gkeep event signup"));
	}
}
