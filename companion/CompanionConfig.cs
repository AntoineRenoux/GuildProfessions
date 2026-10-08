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

}
