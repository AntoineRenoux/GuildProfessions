using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GuildProfessions.Companion;

/// <summary>
/// Pack tout-en-un : installe l'addon dans Interface/AddOns puis le tient à
/// jour, d'après l'entrée « addon » du manifeste (version + URL + SHA-256).
/// Data.lua n'est jamais extrait : c'est le fichier que la synchro écrit, le
/// remplacer par la version vide du zip effacerait l'annuaire en jeu.
/// </summary>
public sealed partial class AddonInstaller(CompanionConfig config)
{
	public const string AddonFolder = "GuildProfessions";
	private const string DataFile = "Data.lua";

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private string AddOnsDirectory => Path.Combine(config.WowPath, "Interface", "AddOns");

	[GeneratedRegex(@"^##\s*Version:\s*(\S+)", RegexOptions.Multiline)]
	private static partial Regex TocVersionRegex();

	/// <summary>Version lue dans le .toc installé ; null si l'addon est absent.</summary>
	public static string? ReadInstalledVersion(string addOnsDirectory)
	{
		var tocPath = Path.Combine(addOnsDirectory, AddonFolder, AddonFolder + ".toc");
		if (!File.Exists(tocPath))
		{
			return null;
		}
		var match = TocVersionRegex().Match(File.ReadAllText(tocPath));
		return match.Success ? match.Groups[1].Value : "0.0.0";
	}

	/// <summary>
	/// Extrait le dossier de l'addon du zip vers AddOns. Les fichiers qui ne
	/// sont plus livrés sont retirés ; Data.lua n'est jamais écrit ni supprimé.
	/// </summary>
	public static int InstallFromZip(byte[] zipBytes, string addOnsDirectory)
	{
		var target = Path.Combine(addOnsDirectory, AddonFolder);
		var targetRoot = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
		Directory.CreateDirectory(target);

		var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		using var zip = new ZipArchive(new MemoryStream(zipBytes));
		foreach (var entry in zip.Entries)
		{
			var normalized = entry.FullName.Replace('\\', '/');
			if (!normalized.StartsWith(AddonFolder + "/", StringComparison.OrdinalIgnoreCase) || entry.Name.Length == 0)
			{
				continue; // dossiers et fichiers hors de l'addon
			}
			var relative = normalized[(AddonFolder.Length + 1)..];
			if (relative.Equals(DataFile, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			var destination = Path.GetFullPath(Path.Combine(target, relative));
			// Garde-fou zip-slip : rien ne sort du dossier de l'addon.
			if (!destination.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
			entry.ExtractToFile(destination, overwrite: true);
			written.Add(destination);
		}

		foreach (var existing in Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories))
		{
			var isData = Path.GetFileName(existing).Equals(DataFile, StringComparison.OrdinalIgnoreCase)
				&& Path.GetDirectoryName(Path.GetFullPath(existing)) == Path.GetFullPath(target);
			if (!isData && !written.Contains(Path.GetFullPath(existing)))
			{
				File.Delete(existing);
			}
		}
		return written.Count;
	}

	public async Task EnsureLatestAsync(CancellationToken ct)
	{
		try
		{
			if (!Directory.Exists(AddOnsDirectory))
			{
				Directory.CreateDirectory(AddOnsDirectory);
			}
			using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
			var manifest = await http.GetFromJsonAsync<CompanionManifest>(
				$"{config.ApiBaseUrl.TrimEnd('/')}/api/v1/companion/manifest", JsonOptions, ct);
			var addon = manifest?.Addon;
			if (addon is null)
			{
				return;
			}

			var installed = ReadInstalledVersion(AddOnsDirectory);
			if (installed is not null && !SelfUpdater.IsNewer(addon.Version, ParseOrZero(installed)))
			{
				return;
			}

			SyncService.Log(installed is null
				? $"Installation de l'addon v{addon.Version} dans {AddOnsDirectory}..."
				: $"Mise à jour de l'addon : {installed} → {addon.Version}...");
			var zipBytes = await http.GetByteArrayAsync(addon.Url, ct);
			var hash = Convert.ToHexStringLower(SHA256.HashData(zipBytes));
			if (!string.Equals(hash, addon.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				SyncService.Log("Installation de l'addon annulée : l'empreinte SHA-256 ne correspond pas.");
				return;
			}
			var count = InstallFromZip(zipBytes, AddOnsDirectory);
			SyncService.Log($"Addon v{addon.Version} installé ({count} fichiers). En jeu : /reload, ou relance WoW s'il était fermé.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			SyncService.Log($"Installation de l'addon impossible : {exception.Message}");
		}
	}

	private static Version ParseOrZero(string version) =>
		Version.TryParse(version, out var parsed) ? parsed : new Version(0, 0, 0);
}
