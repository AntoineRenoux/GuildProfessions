using GuildProfessions.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Reprise de la guilde configurée avant le multi-guildes (FORMER HOPE) : si
/// la config porte encore Guild:Name et Api:GuildToken, la guilde est créée
/// (ou la « guilde historique » de la migration complétée) avec ce même
/// token. Les packs déjà installés continuent donc de fonctionner.
/// Guild:DiscordServerId rattache son serveur Discord (sinon : /setup).
/// </summary>
public static class LegacyGuildBootstrap
{
	public const string HistoricSlug = "guilde-historique";

	public static async Task RunAsync(AppDbContext db, IConfiguration config, ILogger logger, CancellationToken ct = default)
	{
		var token = config["Api:GuildToken"];
		var name = config["Guild:Name"];
		var serverId = config["Guild:DiscordServerId"];
		if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		var guild = await db.Guilds.SingleOrDefaultAsync(g => g.ApiToken == token, ct)
			?? await db.Guilds.SingleOrDefaultAsync(g => g.Slug == HistoricSlug, ct);
		if (guild is null)
		{
			guild = new Guild
			{
				Name = name.Trim(),
				Slug = GuildDirectory.Slugify(name),
				ApiToken = token,
				InviteCode = GuildDirectory.NewInviteCode(),
				// Comportement d'avant le multi-guildes : les pseudos étaient renommés.
				SyncNicknames = true,
				CreatedUtc = DateTime.UtcNow,
			};
			db.Guilds.Add(guild);
			logger.LogInformation("Guilde historique « {Name} » créée depuis la configuration.", guild.Name);
		}
		else if (guild.Slug == HistoricSlug)
		{
			guild.Slug = GuildDirectory.Slugify(name);
			logger.LogInformation("Guilde historique renommée « {Name} ».", name);
		}

		guild.Name = name.Trim();
		guild.ApiToken = token;
		if (!string.IsNullOrWhiteSpace(serverId) &&
			!await db.Guilds.AnyAsync(g => g.DiscordServerId == serverId && g.Id != guild.Id, ct))
		{
			guild.DiscordServerId = serverId;
		}
		await db.SaveChangesAsync(ct);
	}
}
