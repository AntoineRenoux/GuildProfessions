using System.Text.Json;

namespace GuildProfessions.Companion;

public sealed class CompanionConfig
{
	public string ApiBaseUrl { get; set; } = "https://gp.warpvault.com";
	public string GuildToken { get; set; } = "";
	/// <summary>Optionnel : sans token personnel, l'upload passe par le token de guilde.</summary>
	public string UploadToken { get; set; } = "";
	public string WowPath { get; set; } = "";
	public int PollMinutes { get; set; } = 10;
	/// <summary>Windows : inscription automatique au démarrage de la session (HKCU Run).</summary>
	public bool AutoStart { get; set; } = true;

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

	// Zéro configuration côté joueur : sans config.json on continue avec les
	// valeurs par défaut (le zip distribué par l'officier en contient un
	// pré-rempli avec le token de guilde).
	public static CompanionConfig LoadOrCreate(string path)
	{
		if (!File.Exists(path))
		{
			var defaults = new CompanionConfig();
			File.WriteAllText(path, JsonSerializer.Serialize(defaults, JsonOptions));
			return defaults;
		}
		return JsonSerializer.Deserialize<CompanionConfig>(File.ReadAllText(path), JsonOptions) ?? new CompanionConfig();
	}

	/// <summary>
	/// Tente de localiser le dossier de la branche WoW (celui qui contient
	/// Interface/ et WTF/) : installation Windows classique ou Steam/Proton.
	/// Les branches candidates, par ordre de préférence.
	/// </summary>
	public static string? DetectWowPath()
	{
		string[] branches = ["_forever_", "_classic_beta_", "_classic_era_", "_retail_"];
		var bases = new List<string>
		{
			@"C:\Program Files (x86)\World of Warcraft",
			@"D:\World of Warcraft",
		};

		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var compatData = Path.Combine(home, ".local/share/Steam/steamapps/compatdata");
		if (Directory.Exists(compatData))
		{
			bases.AddRange(Directory.EnumerateDirectories(compatData)
				.Select(dir => Path.Combine(dir, "pfx/drive_c/Program Files (x86)/World of Warcraft"))
				.Where(Directory.Exists));
		}

		foreach (var branch in branches)
		{
			foreach (var basePath in bases)
			{
				var candidate = Path.Combine(basePath, branch);
				if (Directory.Exists(Path.Combine(candidate, "Interface")) ||
					Directory.Exists(Path.Combine(candidate, "WTF")))
				{
					return candidate;
				}
			}
		}
		return null;
	}
}
