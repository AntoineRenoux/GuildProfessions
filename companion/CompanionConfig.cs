using System.Text.Json;

namespace GuildProfessions.Companion;

public sealed class CompanionConfig
{
	public string ApiBaseUrl { get; set; } = "http://localhost:5000";
	public string GuildToken { get; set; } = "";
	public string UploadToken { get; set; } = "";
	public string WowPath { get; set; } = "";
	public int PollMinutes { get; set; } = 10;

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

	public static CompanionConfig? LoadOrCreate(string path)
	{
		if (!File.Exists(path))
		{
			File.WriteAllText(path, JsonSerializer.Serialize(new CompanionConfig(), JsonOptions));
			return null;
		}
		return JsonSerializer.Deserialize<CompanionConfig>(File.ReadAllText(path), JsonOptions);
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
