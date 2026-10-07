using GuildProfessions.Companion;

var baseDirectory = AppContext.BaseDirectory;
var configPath = Path.Combine(baseDirectory, "config.json");

SyncService.Log("GuildProfessions Companion — pont entre le serveur de guilde et l'addon.");

var config = CompanionConfig.LoadOrCreate(configPath);
if (config is null)
{
	SyncService.Log($"Premier lancement : {configPath} créé.");
	SyncService.Log("Renseigne apiBaseUrl, guildToken (fourni par l'officier), uploadToken (/token sur Discord), puis relance.");
	return 1;
}

if (string.IsNullOrEmpty(config.WowPath))
{
	var detected = CompanionConfig.DetectWowPath();
	if (detected is null)
	{
		SyncService.Log("Impossible de localiser World of Warcraft : renseigne wowPath dans config.json");
		SyncService.Log(@"(le dossier de la branche, ex. C:\Program Files (x86)\World of Warcraft\_forever_).");
		return 1;
	}
	config.WowPath = detected;
	SyncService.Log($"WoW détecté : {detected}");
}

if (!Directory.Exists(config.WowPath))
{
	SyncService.Log($"wowPath invalide : {config.WowPath}");
	return 1;
}

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
	eventArgs.Cancel = true;
	cancellation.Cancel();
	SyncService.Log("Arrêt demandé...");
};

using var sync = new SyncService(config, baseDirectory);
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
