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
		var guildId = config.GetValue<ulong?>("Discord:GuildId");
		if (guildId is > 0)
		{
			// Enregistrement sur une guilde précise : effectif immédiatement.
			await interactions.RegisterCommandsToGuildAsync(guildId.Value);
			logger.LogInformation("Commandes slash enregistrées sur la guilde {GuildId}.", guildId);
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
