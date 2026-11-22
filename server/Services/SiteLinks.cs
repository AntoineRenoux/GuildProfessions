using GuildProfessions.Server.Data;

namespace GuildProfessions.Server.Services;

/// <summary>Adresses publiques du site (Site:BaseUrl), pour les liens donnés par le bot.</summary>
public sealed class SiteLinks(IConfiguration config)
{
	public string BaseUrl => (config["Site:BaseUrl"] is { Length: > 0 } url ? url : "https://gp.warpvault.com").TrimEnd('/');

	/// <summary>Page d'installation d'une guilde, à partager à ses joueurs (contient le pack personnalisé).</summary>
	public string Join(Guild guild) => $"{BaseUrl}/rejoindre/{guild.InviteCode}";
}
