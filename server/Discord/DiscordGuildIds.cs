namespace GuildProfessions.Server.Discord;

/// <summary>
/// Discord:GuildIds = IDs de serveurs séparés par des virgules (test, prod...).
/// Passer en prod = ajouter l'ID du serveur de guilde, sans rien retirer.
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
