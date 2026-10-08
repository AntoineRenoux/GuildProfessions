using Discord;
using Discord.Interactions;
using Discord.WebSocket;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Héberge le bot dans le même process que l'API. Sans token configuré, le
/// bot ne démarre pas mais l'API reste utilisable (tests locaux, CI).
/// </summary>
public sealed class DiscordBotService(
	DiscordSocketClient client,
	InteractionService interactions,
	IServiceProvider services,
	IConfiguration config,
	ILogger<DiscordBotService> logger) : IHostedService
{
	public async Task StartAsync(CancellationToken cancellationToken)
	{
		var token = config["Discord:Token"];
		if (string.IsNullOrEmpty(token))
		{
			logger.LogWarning("Discord:Token absent — le bot Discord ne démarre pas (API seule).");
			return;
		}

		client.Log += OnLog;
		interactions.Log += OnLog;
		client.Ready += OnReadyAsync;
		client.InteractionCreated += OnInteractionAsync;
		// Sans ce hook, une commande qui échoue (exception, précondition...)
		// est totalement silencieuse côté serveur.
		interactions.InteractionExecuted += (command, _, result) =>
		{
			if (!result.IsSuccess)
			{
				logger.LogError("Commande /{Command} en échec : {Error} — {Reason}",
					command?.Name, result.Error, result.ErrorReason);
			}
			return Task.CompletedTask;
		};

		await interactions.AddModulesAsync(typeof(DiscordBotService).Assembly, services);
		await client.LoginAsync(TokenType.Bot, token);
		await client.StartAsync();
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		await client.StopAsync();
	}

	private async Task OnReadyAsync()
	{
		// Discord:GuildIds = IDs de serveurs séparés par des virgules (test,
		// prod...). L'enregistrement par serveur est immédiat ; passer en prod
		// = ajouter l'ID du serveur de guilde à la liste, sans rien retirer.
		var guildIds = (config["Discord:GuildIds"] ?? config["Discord:GuildId"] ?? "")
			.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Select(raw => ulong.TryParse(raw, out var id) ? id : 0)
			.Where(id => id > 0)
			.ToList();

		if (guildIds.Count > 0)
		{
			// Purge des commandes globales (un premier lancement les avait
			// enregistrées) : sinon chaque commande apparaît en double.
			await client.Rest.DeleteAllGlobalCommandsAsync();
			foreach (var guildId in guildIds)
			{
				await interactions.RegisterCommandsToGuildAsync(guildId);
				logger.LogInformation("Commandes slash enregistrées sur le serveur {GuildId}.", guildId);
			}
		}
		else
		{
			// Global : propagation en ~1 h côté Discord.
			await interactions.RegisterCommandsGloballyAsync();
			logger.LogInformation("Commandes slash enregistrées globalement.");
		}
	}

	private async Task OnInteractionAsync(SocketInteraction interaction)
	{
		// Un scope DI par commande : les modules consomment le DbContext scoped.
		await using var scope = services.CreateAsyncScope();
		var context = new SocketInteractionContext(client, interaction);
		await interactions.ExecuteCommandAsync(context, scope.ServiceProvider);
	}

	private Task OnLog(LogMessage message)
	{
		logger.Log(message.Severity switch
		{
			LogSeverity.Critical => LogLevel.Critical,
			LogSeverity.Error => LogLevel.Error,
			LogSeverity.Warning => LogLevel.Warning,
			LogSeverity.Info => LogLevel.Information,
			_ => LogLevel.Debug,
		}, "[Discord] {Message}", message.ToString());
		return Task.CompletedTask;
	}
}
