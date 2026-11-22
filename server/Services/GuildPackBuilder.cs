using System.IO.Compression;
using System.Text.Json;
using GuildProfessions.Server.Data;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Pack d'une guilde : le pack tout-en-un publié (un par système, sans token)
/// dont le config.json est remplacé à la volée par celui de la guilde. Le
/// joueur n'a rien à régler : dézipper, lancer, c'est fini.
/// </summary>
public sealed class GuildPackBuilder(IWebHostEnvironment env, IConfiguration config)
{
	/// <summary>Systèmes proposés, avec le nom du pack publié par scripts/package-companion.sh.</summary>
	public static readonly IReadOnlyDictionary<string, string> Platforms = new Dictionary<string, string>
	{
		["win-x64"] = "Windows",
		["osx-arm64"] = "Mac (Apple Silicon)",
		["osx-x64"] = "Mac (Intel)",
		["linux-x64"] = "Linux",
	};

	public const string TemplatePrefix = "Guildkeep-AllInOne-";

	public string ApiBaseUrl => (config["Api:PublicUrl"] is { Length: > 0 } url ? url : "https://guildkeep.warpvault.com").TrimEnd('/');

	public string? TemplatePath(string platform)
	{
		if (!Platforms.ContainsKey(platform))
		{
			return null;
		}
		var path = Path.Combine(env.WebRootPath ?? "wwwroot", "downloads", $"{TemplatePrefix}{platform}.zip");
		return File.Exists(path) ? path : null;
	}

	public static string FileName(Guild guild, string platform) => $"Guildkeep-{guild.Slug}-{platform}.zip";

	public byte[] Build(Guild guild, string templatePath) =>
		WithGuildConfig(File.ReadAllBytes(templatePath), GuildConfigJson(guild, ApiBaseUrl));

	public static string GuildConfigJson(Guild guild, string apiBaseUrl) => JsonSerializer.Serialize(new
	{
		apiBaseUrl,
		guildName = guild.Name,
		guildToken = guild.ApiToken,
		uploadToken = "",
		wowPath = "",
		pollMinutes = 10,
		autoStart = true,
		autoUpdate = true,
		installAddon = true,
	}, new JsonSerializerOptions { WriteIndented = true });

	/// <summary>Copie du zip où chaque config.json au premier niveau du dossier est remplacé.</summary>
	public static byte[] WithGuildConfig(byte[] template, string configJson)
	{
		using var output = new MemoryStream();
		output.Write(template);
		using (var zip = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
		{
			var targets = zip.Entries
				.Where(e => e.Name.Equals("config.json", StringComparison.OrdinalIgnoreCase) &&
					e.FullName.Replace('\\', '/').Count(c => c == '/') <= 1)
				.ToList();
			if (targets.Count == 0)
			{
				throw new InvalidOperationException("Le pack publié ne contient pas de config.json.");
			}
			foreach (var target in targets)
			{
				var fullName = target.FullName;
				target.Delete();
				using var writer = new StreamWriter(zip.CreateEntry(fullName).Open());
				writer.Write(configJson);
			}
		}
		return output.ToArray();
	}
}
