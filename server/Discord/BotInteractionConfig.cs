using Discord;
using Discord.Interactions;

namespace GuildProfessions.Server.Discord;

/// <summary>Réglages du moteur de commandes slash, isolés pour être testés.</summary>
public static class BotInteractionConfig
{
	public static InteractionServiceConfig Create() => new()
	{
		LogLevel = LogSeverity.Debug,
		// Exécution inline : les exceptions remontent dans notre await au
		// lieu d'être avalées par le fire-and-forget interne de RunMode.Async.
		DefaultRunMode = RunMode.Sync,
		ThrowOnError = true,
		// DiscordBotService ouvre un scope par interaction et y pose la guilde du serveur
		// et la langue de l'utilisateur. Avec les scopes automatiques (défaut de
		// Discord.Net), chaque commande et chaque précondition recevaient un scope neuf,
		// sans guilde : « ce serveur n'utilise pas encore Guildkeep » juste après /gkeep-setup.
		AutoServiceScopes = false,
		// Noms et descriptions des commandes en français (anglais par défaut, dans le code).
		LocalizationManager = new JsonLocalizationManager(Path.Combine(AppContext.BaseDirectory, "Localization"), "commands"),
	};
}
