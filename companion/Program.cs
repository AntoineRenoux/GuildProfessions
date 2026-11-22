using GuildProfessions.Companion;

var baseDirectory = AppContext.BaseDirectory;
var configPath = Path.Combine(baseDirectory, "config.json");

// Relancé par le démarrage de Windows : pas de fenêtre visible.
if (args.Contains("--background"))
{
	AutoStart.HideConsoleWindow();
}

SyncService.Log($"Guildkeep Companion v{SelfUpdater.CurrentVersion.ToString(3)} : le pont entre ta guilde et l'addon.");
SelfUpdater.CleanupPreviousVersion();

var config = CompanionConfig.LoadOrCreate(configPath);

var updater = new SelfUpdater(config, baseDirectory);
if (config.AutoUpdate && await updater.TryUpdateAsync(CancellationToken.None))
{
	updater.Restart(args);
}
// La guilde de ce pack rejoint la liste des guildes du poste (un joueur peut
// avoir des personnages dans plusieurs guildes : un pack par guilde, un seul
// compagnon pour toutes).
var guildStore = GuildStore.Default();
if (!string.IsNullOrEmpty(config.GuildToken) && guildStore.AddOrUpdate(new GuildEntry
{
	Name = config.GuildName,
	Token = config.GuildToken,
	ApiBaseUrl = config.ApiBaseUrl,
	UploadToken = config.UploadToken,
}))
{
	SyncService.Log($"Guilde « {(config.GuildName.Length > 0 ? config.GuildName : "du pack")} » ajoutée à ce poste.");
}
if (guildStore.Load().Count == 0)
{
	SyncService.Log("Aucune guilde : télécharge le pack depuis le lien de ta guilde (donné par son administrateur) et lance-le.");
}

// Un seul compagnon par poste : s'il tourne déjà, il lira la guilde ajoutée ci-dessus.
using var instanceLock = SingleInstance.TryAcquire(GuildStore.DefaultDirectory);
if (instanceLock is null)
{
	SyncService.Log("Le compagnon tourne déjà sur ce poste : il synchronise maintenant cette guilde aussi. Tu peux fermer cette fenêtre.");
	return 0;
}

if (string.IsNullOrEmpty(config.WowPath))
{
	var detected = WowLocator.Detect();
	if (detected is null)
	{
		SyncService.Log("Impossible de localiser World of Warcraft : renseigne wowPath dans config.json");
		SyncService.Log(@"(le dossier de la branche, ex. C:\Program Files (x86)\World of Warcraft\_forever_).");
		return 1;
	}
	config.WowPath = detected.Value.Path;
	SyncService.Log($"WoW détecté via {detected.Value.Source} : {detected.Value.Path}");
}

if (!Directory.Exists(config.WowPath))
{
	SyncService.Log($"wowPath invalide : {config.WowPath}");
	return 1;
}

AutoStart.Sync(config.AutoStart);

// Pack tout-en-un : l'addon est installé (ou mis à jour) avant la première
// synchro, qui écrira ensuite son Data.lua.
AddonInstaller? addonInstaller = null;
if (config.InstallAddon)
{
	addonInstaller = new AddonInstaller(config);
	await addonInstaller.EnsureLatestAsync(CancellationToken.None);
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	cancellation.Cancel();
	SyncService.Log("Arrêt demandé...");
};

using var sync = new SyncService(config, guildStore, baseDirectory);
if (config.AutoUpdate)
{
	_ = updater.RunPeriodicAsync(args, addonInstaller, cancellation.Token);
}
try
{
	await sync.RunAsync(cancellation.Token);
}
catch (OperationCanceledException)
{
	// Arrêt normal via Ctrl+C.
}
SyncService.Log("Compagnon arrêté.");
return 0;
