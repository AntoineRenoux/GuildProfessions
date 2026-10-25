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
		// L'enregistrement par serveur est immédiat (global : ~1 h de propagation).
		var guildIds = DiscordGuildIds.Parse(config);

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

		_ = Task.Run(ReconcileNicknamesAsync);
	}

	// Réaligne les pseudos de tous les membres ayant un personnage principal :
	// rattrape un renommage manuel, ou un membre arrivé sur le serveur après son /link.
	private async Task ReconcileNicknamesAsync()
	{
		try
		{
			await using var scope = services.CreateAsyncScope();
			var roster = scope.ServiceProvider.GetRequiredService<GuildProfessions.Server.Services.RosterService>();
			var nicknames = scope.ServiceProvider.GetRequiredService<NicknameSync>();
			var targets = await roster.GetNicknameTargetsAsync();
			var updated = 0;
			foreach (var (discordId, characterName) in targets)
			{
				var outcomes = await nicknames.SyncAsync(discordId, characterName);
				updated += outcomes.Count(o => o == NicknameOutcome.Updated);
			}
			logger.LogInformation("Pseudos réalignés : {Updated} modifié(s) pour {Members} membre(s) ayant un personnage principal.",
				updated, targets.Count);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Réalignement des pseudos en échec.");
		}
	}

	private Task OnInteractionAsync(SocketInteraction interaction)
	{
		logger.LogDebug("Interaction reçue : {Type} de {User}.", interaction.Type, interaction.User?.Username);
		// Détachée du thread de la gateway (pattern recommandé Discord.Net) :
		// un traitement inline bloque la connexion et donc... la réponse.
		_ = Task.Run(async () =>
		{
			try
			{
				// Un scope DI par commande : les modules consomment le DbContext scoped.
				await using var scope = services.CreateAsyncScope();
				var context = new SocketInteractionContext(client, interaction);
				await interactions.ExecuteCommandAsync(context, scope.ServiceProvider);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Échec de traitement d'une interaction.");
			}
		});
		return Task.CompletedTask;
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
