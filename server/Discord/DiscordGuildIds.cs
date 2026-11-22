namespace GuildProfessions.Server.Discord;

/// <summary>
/// Discord:GuildIds (développement) : IDs de serveurs séparés par des
/// virgules, où les commandes sont enregistrées immédiatement. Vide en
/// production : commandes globales, disponibles sur tout serveur qui invite le bot.
/// </summary>
public static class DiscordGuildIds
{
	public static List<ulong> Parse(IConfiguration config)
	{
		return (config["Discord:GuildIds"] ?? config["Discord:GuildId"] ?? "")
			.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(raw => ulong.TryParse(raw, out var id) ? id : 0)
			.Where(id => id > 0)
			.ToList();
	}
}
