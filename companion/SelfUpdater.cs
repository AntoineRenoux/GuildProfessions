using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace GuildProfessions.Companion;

public sealed record CompanionManifest(string Version, Dictionary<string, CompanionAsset> Assets);

public sealed record CompanionAsset(string Url, string Sha256);

/// <summary>
/// Mise à jour automatique : compare la version embarquée au manifeste publié
/// par le serveur (GET /api/v1/companion/manifest), télécharge le zip de la
/// plateforme, vérifie son SHA-256, remplace l'exécutable puis redémarre.
/// config.json et l'état de synchro ne sont jamais touchés.
/// </summary>
public sealed class SelfUpdater(CompanionConfig config, string baseDirectory)
{
	private const string ExeBaseName = "GuildProfessionsCompanion";
	// Code de sortie qui demande à systemd (Restart=on-failure) de relancer le binaire neuf.
	private const int RestartExitCode = 75;

	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	public static Version CurrentVersion =>
		Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);

	public static bool IsNewer(string candidate, Version current) =>
		Version.TryParse(candidate, out var parsed) && Normalize(parsed) > Normalize(current);

	// « 1.0.42 » et « 1.0.42.0 » sont la même version.
	private static Version Normalize(Version v) =>
		new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

	public static string? CurrentRid()
	{
		var arch = RuntimeInformation.ProcessArchitecture;
		if (OperatingSystem.IsWindows())
		{
			return arch == Architecture.X64 ? "win-x64" : null;
		}
		if (OperatingSystem.IsMacOS())
		{
			return arch == Architecture.Arm64 ? "osx-arm64" : "osx-x64";
		}
		if (OperatingSystem.IsLinux())
		{
			return arch == Architecture.X64 ? "linux-x64" : null;
		}
		return null;
	}

	/// <summary>Le binaire tourne-t-il en exécutable publié (et pas via « dotnet x.dll » en dev) ?</summary>
	private static string? PublishedExePath()
	{
		var path = Environment.ProcessPath;
		return path is not null && Path.GetFileNameWithoutExtension(path) == ExeBaseName ? path : null;
	}

	public static void CleanupPreviousVersion()
	{
		var exePath = PublishedExePath();
		if (exePath is null)
		{
			return;
		}
		try
		{
			File.Delete(exePath + ".old");
		}
		catch
		{
			// Encore verrouillé : on retentera au prochain démarrage.
		}
	}

	/// <summary>true si une mise à jour a été installée (le process doit alors redémarrer).</summary>
	public async Task<bool> TryUpdateAsync(CancellationToken ct)
	{
		var exePath = PublishedExePath();
		var rid = CurrentRid();
		if (exePath is null || rid is null)
		{
			return false;
		}

		try
		{
			using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
			var manifestUrl = $"{config.ApiBaseUrl.TrimEnd('/')}/api/v1/companion/manifest";
			var manifest = await http.GetFromJsonAsync<CompanionManifest>(manifestUrl, JsonOptions, ct);
			if (manifest is null || !IsNewer(manifest.Version, CurrentVersion))
			{
				return false;
			}
			if (!manifest.Assets.TryGetValue(rid, out var asset))
			{
				SyncService.Log($"Mise à jour {manifest.Version} disponible mais pas de build {rid}.");
				return false;
			}

			SyncService.Log($"Mise à jour disponible : {CurrentVersion.ToString(3)} → {manifest.Version}. Téléchargement...");
			var zipBytes = await http.GetByteArrayAsync(asset.Url, ct);
			var hash = Convert.ToHexStringLower(SHA256.HashData(zipBytes));
			if (!string.Equals(hash, asset.Sha256, StringComparison.OrdinalIgnoreCase))
			{
				SyncService.Log("Mise à jour annulée : l'empreinte SHA-256 du téléchargement ne correspond pas.");
				return false;
			}

			var newExePath = exePath + ".new";
			using (var zip = new ZipArchive(new MemoryStream(zipBytes)))
			{
				var entry = zip.Entries.FirstOrDefault(e =>
					Path.GetFileNameWithoutExtension(e.Name) == ExeBaseName && !e.Name.EndsWith(".pdb"));
				if (entry is null)
				{
					SyncService.Log("Mise à jour annulée : exécutable introuvable dans l'archive.");
					return false;
				}
				entry.ExtractToFile(newExePath, overwrite: true);
			}
			if (!OperatingSystem.IsWindows())
			{
				File.SetUnixFileMode(newExePath,
					UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
					UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
					UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
			}

			// Windows refuse d'écraser un exe en cours d'exécution mais accepte de
			// le renommer : on le met de côté (.old, supprimé au prochain démarrage).
			// Sous Linux/macOS, remplacer le fichier ne gêne pas le process en cours.
			if (OperatingSystem.IsWindows())
			{
				File.Move(exePath, exePath + ".old", overwrite: true);
				try
				{
					File.Move(newExePath, exePath, overwrite: true);
				}
				catch
				{
					// Retour arrière : ne jamais laisser le dossier sans exécutable.
					File.Move(exePath + ".old", exePath, overwrite: true);
					throw;
				}
			}
			else
			{
				File.Move(newExePath, exePath, overwrite: true);
			}

			SyncService.Log($"Version {manifest.Version} installée, redémarrage.");
			return true;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			SyncService.Log($"Vérification de mise à jour impossible : {exception.Message}");
			return false;
		}
	}

	public async Task RunPeriodicAsync(string[] args, CancellationToken ct)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromHours(6));
		while (await timer.WaitForNextTickAsync(ct))
		{
			if (await TryUpdateAsync(ct))
			{
				Restart(args);
			}
		}
	}

	public void Restart(string[] args)
	{
		// Sous systemd, on rend la main avec un code d'échec : le service relance le binaire neuf.
		if (Environment.GetEnvironmentVariable("INVOCATION_ID") is not null)
		{
			Environment.Exit(RestartExitCode);
		}
		var startInfo = new ProcessStartInfo(PublishedExePath()!)
		{
			UseShellExecute = false,
			WorkingDirectory = baseDirectory,
		};
		foreach (var arg in args)
		{
			startInfo.ArgumentList.Add(arg);
		}
		Process.Start(startInfo);
		Environment.Exit(0);
	}
}
