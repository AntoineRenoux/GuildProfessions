using System.Text.Json;

namespace GuildProfessions.Companion;

/// <summary>
/// Le compagnon sans son interface : préparation (configuration, guilde du
/// pack, instance unique, WoW, addon), puis veille et synchro. Utilisé par
/// l'icône de notification comme par le mode console (--console, ou Linux
/// sans bureau).
/// </summary>
public sealed class CompanionEngine(string[] args) : IDisposable
{
	private SyncService? _sync;
	private SelfUpdater? _updater;
	private AddonInstaller? _addonInstaller;
	private IDisposable? _instanceLock;
	private string? _configPath;

	public bool Background { get; } = args.Contains("--background");

	public CompanionConfig Config { get; private set; } = new();

	/// <summary>
	/// Dossier de config.json : à côté de l'exécutable, ou à côté de l'app sur
	/// macOS (Guildkeep.app/Contents/MacOS → le dossier qui contient l'app), là où
	/// le serveur place la configuration de la guilde dans le pack.
	/// </summary>
	public static string ConfigDirectory(string baseDirectory)
	{
		var dir = baseDirectory.TrimEnd('/', '\\');
		var marker = ".app" + Path.DirectorySeparatorChar + "Contents" + Path.DirectorySeparatorChar + "MacOS";
		var index = dir.LastIndexOf(marker, StringComparison.Ordinal);
		if (index < 0 || index + marker.Length != dir.Length)
		{
			return dir;
		}
		var appBundle = dir[..(index + ".app".Length)];
		return Path.GetDirectoryName(appBundle) ?? dir;
	}

	/// <summary>Prépare le compagnon ; renvoie un code de sortie s'il doit s'arrêter là (déjà lancé).</summary>
	public async Task<int?> PrepareAsync()
	{
		var baseDirectory = ConfigDirectory(AppContext.BaseDirectory);
		_configPath = Path.Combine(baseDirectory, "config.json");

		SyncService.Log($"Guildkeep Companion v{SelfUpdater.CurrentVersion.ToString(3)} : le pont entre ta guilde et l'addon.");
		SelfUpdater.CleanupPreviousVersion();
		Config = CompanionConfig.LoadOrCreate(_configPath);

		_updater = new SelfUpdater(Config, baseDirectory);
		// Lancé à la main (pack de la guilde) : à jour tout de suite, le joueur va jouer.
		if (!Background && Config.AutoUpdate && await _updater.TryUpdateAsync(CancellationToken.None))
		{
			_updater.Restart(args);
		}

		// La guilde de ce pack rejoint la liste des guildes du poste (un pack par guilde, un seul compagnon).
		var guildStore = GuildStore.Default();
		if (!string.IsNullOrEmpty(Config.GuildToken) && guildStore.AddOrUpdate(new GuildEntry
		{
			Name = Config.GuildName,
			Token = Config.GuildToken,
			ApiBaseUrl = Config.ApiBaseUrl,
			UploadToken = Config.UploadToken,
		}))
		{
			SyncService.Log($"Guilde « {(Config.GuildName.Length > 0 ? Config.GuildName : "du pack")} » ajoutée à ce poste.");
		}
		if (guildStore.Load().Count == 0)
		{
			SyncService.Log("Aucune guilde : télécharge le pack depuis le lien de ta guilde (donné par son administrateur) et lance-le.");
			CompanionStatus.Shared.Fail("aucune guilde, lance le pack téléchargé depuis le lien de ta guilde");
		}

		// Un seul compagnon par poste : s'il tourne déjà, il lira la guilde ajoutée ci-dessus.
		_instanceLock = SingleInstance.TryAcquire(GuildStore.DefaultDirectory);
		if (_instanceLock is null)
		{
			SyncService.Log("Le compagnon tourne déjà sur ce poste : il synchronise maintenant cette guilde aussi.");
			return 0;
		}

		if (string.IsNullOrEmpty(Config.WowPath) && WowLocator.Detect() is { } detected)
		{
			Config.WowPath = detected.Path;
			SyncService.Log($"WoW détecté via {detected.Source} : {detected.Path}");
		}

		AutoStart.Sync(Config.AutoStart);

		_addonInstaller = Config.InstallAddon ? new AddonInstaller(Config) : null;
		if (WowFound && _addonInstaller is not null && !Background)
		{
			await _addonInstaller.EnsureLatestAsync(CancellationToken.None);
		}
		_sync = new SyncService(Config, guildStore, baseDirectory);
		return null;
	}

	private bool WowFound => !string.IsNullOrEmpty(Config.WowPath) && Directory.Exists(Config.WowPath);

	/// <summary>Veille et synchro jusqu'à l'arrêt demandé.</summary>
	public async Task RunAsync(CancellationToken ct)
	{
		if (_sync is null)
		{
			throw new InvalidOperationException("PrepareAsync d'abord.");
		}
		if (!WowFound)
		{
			SyncService.Log(string.IsNullOrEmpty(Config.WowPath)
				? "Impossible de localiser World of Warcraft : renseigne wowPath dans config.json (le dossier de la branche, ex. _forever_)."
				: $"wowPath invalide : {Config.WowPath}");
			CompanionStatus.Shared.Fail("World of Warcraft introuvable, renseigne wowPath dans config.json");
			await Task.Delay(Timeout.Infinite, ct);
			return;
		}
		await _sync.RunAsync(() => WowProcess.IsRunning(Config.WowPath), OnWakeAsync, ct);
	}

	// Au réveil (WoW lancé) : addon et compagnon mis à jour si besoin, puis synchro.
	private async Task OnWakeAsync(CancellationToken ct)
	{
		if (_addonInstaller is not null)
		{
			await _addonInstaller.EnsureLatestAsync(ct);
		}
		if (Config.AutoUpdate && _updater is not null && await _updater.TryUpdateAsync(ct))
		{
			_updater.Restart(args);
		}
	}

	public Task SyncNowAsync(CancellationToken ct) =>
		_sync is null || !WowFound ? Task.CompletedTask : _sync.SyncNowAsync(ct);

	/// <summary>Choix du menu : lancement à l'ouverture de session, gardé dans config.json.</summary>
	public void SetAutoStart(bool enabled)
	{
		Config.AutoStart = enabled;
		AutoStart.Sync(enabled);
		if (_configPath is not null)
		{
			try
			{
				Config.Save(_configPath);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				SyncService.Log($"Choix non enregistré dans config.json : {exception.Message}");
			}
		}
	}

	public void Dispose()
	{
		_sync?.Dispose();
		_instanceLock?.Dispose();
	}
}
