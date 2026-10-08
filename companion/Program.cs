using GuildProfessions.Companion;

var baseDirectory = AppContext.BaseDirectory;
var configPath = Path.Combine(baseDirectory, "config.json");

// Relancé par le démarrage de Windows : pas de fenêtre visible.
if (args.Contains("--background"))
{
	AutoStart.HideConsoleWindow();
}

SyncService.Log($"GuildProfessions Companion v{SelfUpdater.CurrentVersion.ToString(3)} — pont entre le serveur de guilde et l'addon.");
SelfUpdater.CleanupPreviousVersion();

var config = CompanionConfig.LoadOrCreate(configPath);

var updater = new SelfUpdater(config, baseDirectory);
if (config.AutoUpdate && await updater.TryUpdateAsync(CancellationToken.None))
{
	updater.Restart(args);
}
if (string.IsNullOrEmpty(config.GuildToken) && string.IsNullOrEmpty(config.UploadToken))
{
	SyncService.Log("Aucun token dans config.json : la synchronisation sera inactive.");
	SyncService.Log("Utilise le zip fourni par l'officier (config.json pré-rempli) ou renseigne guildToken.");
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

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	cancellation.Cancel();
	SyncService.Log("Arrêt demandé...");
};

using var sync = new SyncService(config, baseDirectory);
if (config.AutoUpdate)
{
	_ = updater.RunPeriodicAsync(args, cancellation.Token);
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
