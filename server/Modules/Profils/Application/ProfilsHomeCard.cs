using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Profils.Application;

/// <summary>Carte du tableau de bord : ton profil et ton accueil ; pour les officiers, les nouveaux à accueillir.</summary>
public sealed class ProfilsHomeCard(ProfileService profiles, BotLocalizer l) : IHomeContributor
{
	public string Module => "Profils";

	public int Order => 50;

	public async Task<HomeCard?> BuildAsync(HomeViewer viewer, CancellationToken ct)
	{
		var lines = new List<string>();
		var me = await profiles.TargetAsync(viewer.DiscordId, null, ct);
		if (me is not null && await profiles.FindProfileAsync(me, ct) is null)
		{
			lines.Add(l.T("Ton profil est vide : `/gkeep profil modifier`.", "Your profile is empty: `/gkeep profile edit`."));
		}
		var incomplete = await profiles.IncompleteAsync(ct);
		if (me is not null && incomplete.FirstOrDefault(i => i.Character == me) is { Character: not null } mine)
		{
			lines.Add(l.T($"Ton accueil : {ProfileService.Steps.Length - mine.Missing.Count}/{ProfileService.Steps.Length} étapes",
				$"Your onboarding: {ProfileService.Steps.Length - mine.Missing.Count}/{ProfileService.Steps.Length} steps"));
		}
		var joins = (await profiles.JournalAsync(7, ct)).Count(r => r.Kind == "join");
		if (joins > 0)
		{
			lines.Add(l.T($"Arrivées cette semaine : {joins}", $"Arrivals this week: {joins}"));
		}
		if (incomplete.Count > 0 && await profiles.IsOfficerAsync(viewer.DiscordId, viewer.IsServerAdmin, ct))
		{
			lines.Add(l.T($"Nouveaux à accueillir : **{incomplete.Count}**", $"Newcomers to welcome: **{incomplete.Count}**"));
		}
		if (lines.Count == 0)
		{
			return null;
		}
		return new HomeCard(l.T("Profils et accueil", "Profiles and onboarding"), lines, l.T("/gkeep accueil etat", "/gkeep welcome status"));
	}
}
