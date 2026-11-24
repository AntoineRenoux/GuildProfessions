using System.Text.Json;

namespace GuildProfessions.Companion;

/// <summary>Une guilde connue de ce poste : son nom en jeu et le token de son pack.</summary>
public sealed record GuildEntry
{
	public string Name { get; init; } = "";
	public string Token { get; init; } = "";
	public string ApiBaseUrl { get; init; } = "";
	/// <summary>Token personnel facultatif (/gkeep token), propre à cette guilde.</summary>
	public string UploadToken { get; init; } = "";
}

/// <summary>
/// Guildes connues du poste, dans un fichier commun à tous les packs
/// (dossier de données de l'utilisateur) : un joueur dont les personnages
/// sont dans plusieurs guildes lance le pack de chacune une fois, et un seul
/// compagnon les synchronise toutes. Relu à chaque cycle : une guilde ajoutée
/// par un autre pack est prise en compte sans redémarrer.
/// </summary>
public sealed class GuildStore(string path)
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

	public string FilePath { get; } = path;

	/// <summary>Dossier des données du compagnon ; GUILDKEEP_DATA_DIR le déplace (essais, diagnostic).</summary>
	public static string DefaultDirectory =>
		Environment.GetEnvironmentVariable("GUILDKEEP_DATA_DIR") is { Length: > 0 } custom
			? custom
			: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Guildkeep");

	public static GuildStore Default() => new(Path.Combine(DefaultDirectory, "guildes.json"));

	public List<GuildEntry> Load()
	{
		try
		{
			return File.Exists(FilePath)
				? JsonSerializer.Deserialize<List<GuildEntry>>(File.ReadAllText(FilePath), JsonOptions) ?? []
				: [];
		}
		catch (Exception exception) when (exception is IOException or JsonException)
		{
			SyncService.Log($"Liste des guildes illisible ({FilePath}) : {exception.Message}");
			return [];
		}
	}

	/// <summary>Ajoute une guilde, ou met à jour celle qui a le même token ou le même nom (lien régénéré).</summary>
	public bool AddOrUpdate(GuildEntry entry)
	{
		var guilds = Load();
		var index = guilds.FindIndex(g => g.Token == entry.Token);
		if (index < 0 && entry.Name.Length > 0)
		{
			index = guilds.FindIndex(g => SameGuild(g.Name, entry.Name) && g.ApiBaseUrl == entry.ApiBaseUrl);
		}
		var merged = index >= 0
			? guilds[index] with
			{
				Token = entry.Token,
				Name = entry.Name.Length > 0 ? entry.Name : guilds[index].Name,
				ApiBaseUrl = entry.ApiBaseUrl,
				UploadToken = entry.UploadToken.Length > 0 ? entry.UploadToken : guilds[index].UploadToken,
			}
			: entry;
		if (index >= 0 && guilds[index] == merged)
		{
			return false;
		}
		if (index >= 0)
		{
			guilds[index] = merged;
		}
		else
		{
			guilds.Add(merged);
		}
		Save(guilds);
		return true;
	}

	/// <summary>Nom renvoyé par le serveur (un ancien config.json ne le donnait pas).</summary>
	public void SetName(string token, string name)
	{
		var guilds = Load();
		var index = guilds.FindIndex(g => g.Token == token);
		if (index >= 0 && guilds[index].Name != name)
		{
			guilds[index] = guilds[index] with { Name = name };
			Save(guilds);
		}
	}

	public static bool SameGuild(string? a, string? b) =>
		a is not null && b is not null && string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

	private void Save(List<GuildEntry> guilds)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
		File.WriteAllText(FilePath, JsonSerializer.Serialize(guilds, JsonOptions));
	}
}
