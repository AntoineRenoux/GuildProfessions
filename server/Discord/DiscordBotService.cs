using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Héberge le bot dans le même process que l'API. Sans token configuré, le
/// bot ne démarre pas mais l'API reste utilisable (tests locaux, CI).
/// Un seul bot pour toutes les guildes : chaque interaction est rattachée à la
/// guilde du serveur Discord d'où elle vient.
/// </summary>
public sealed class DiscordBotService(
	DiscordSocketClient client,
	InteractionService interactions,
	IServiceProvider services,
	IConfiguration config,
	ILogger<DiscordBotService> logger) : IHostedService
{
	/// <summary>Délai avant suppression des données d'une guilde dont le serveur a retiré le bot.</summary>
	private static readonly TimeSpan RemovedGuildRetention = TimeSpan.FromDays(30);

	private readonly CancellationTokenSource _stopping = new();

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
		client.LeftGuild += OnLeftGuildAsync;
		client.JoinedGuild += OnJoinedGuildAsync;
		interactions.InteractionExecuted += OnInteractionExecutedAsync;

		await interactions.AddModulesAsync(typeof(DiscordBotService).Assembly, services);
		await client.LoginAsync(TokenType.Bot, token);
		await client.StartAsync();
		_ = Task.Run(() => PurgeRemovedGuildsLoopAsync(_stopping.Token));
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		await _stopping.CancelAsync();
		await client.StopAsync();
	}

	private async Task OnReadyAsync()
	{
		var devServers = DiscordGuildIds.Parse(config);
		if (devServers.Count > 0)
		{
			// Développement : enregistrement immédiat sur les serveurs de test.
			await client.Rest.DeleteAllGlobalCommandsAsync();
			foreach (var serverId in devServers)
			{
				await interactions.RegisterCommandsToGuildAsync(serverId);
				logger.LogInformation("Commandes slash enregistrées sur le serveur {GuildId}.", serverId);
			}
		}
		else
		{
			// Production : commandes globales (~1 h de propagation), disponibles
			// sur tout serveur qui invite le bot. Les anciennes commandes par
			// serveur sont effacées, sinon chaque commande apparaîtrait en double.
			await interactions.RegisterCommandsGloballyAsync();
			foreach (var guild in client.Guilds)
			{
				try
				{
					await client.Rest.BulkOverwriteGuildCommands([], guild.Id);
				}
				catch (Exception exception)
				{
					logger.LogDebug(exception, "Nettoyage des commandes du serveur {GuildId} impossible.", guild.Id);
				}
			}
			logger.LogInformation("Commandes slash enregistrées globalement ({Count} serveurs).", client.Guilds.Count);
		}

		_ = Task.Run(ReconcileNicknamesAsync);
	}

	// Réaligne les pseudos des guildes qui l'ont activé : rattrape un renommage
	// manuel, ou un membre arrivé sur le serveur après son /link.
	private async Task ReconcileNicknamesAsync()
	{
		try
		{
			List<Guild> guilds;
			await using (var scope = services.CreateAsyncScope())
			{
				guilds = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Guilds.AsNoTracking()
					.Where(g => g.SyncNicknames && g.DiscordServerId != null && g.BotRemovedUtc == null)
					.ToListAsync();
			}
			foreach (var guild in guilds)
			{
				await using var scope = services.CreateAsyncScope();
				scope.ServiceProvider.GetRequiredService<GuildContext>().Use(guild);
				var roster = scope.ServiceProvider.GetRequiredService<RosterService>();
				var nicknames = scope.ServiceProvider.GetRequiredService<NicknameSync>();
				var targets = await roster.GetNicknameTargetsAsync();
				var updated = 0;
				foreach (var (discordId, characterName) in targets)
				{
					var outcomes = await nicknames.SyncAsync(guild, discordId, characterName);
					updated += outcomes.Count(o => o == NicknameOutcome.Updated);
				}
				logger.LogInformation("Pseudos réalignés pour « {Guild} » : {Updated} modifié(s) sur {Members} membre(s).",
					guild.Name, updated, targets.Count);
			}
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
				// Un scope DI par interaction, rattaché à la guilde du serveur d'origine.
				await using var scope = services.CreateAsyncScope();
				var guild = await scope.ServiceProvider.GetRequiredService<GuildDirectory>()
					.FindByDiscordServerAsync(interaction.GuildId);
				if (guild is not null)
				{
					scope.ServiceProvider.GetRequiredService<GuildContext>().Use(guild);
				}
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

	// Sans ce hook, une commande qui échoue (exception, précondition...) est
	// silencieuse : l'utilisateur reçoit au moins la raison d'un refus.
	private async Task OnInteractionExecutedAsync(ICommandInfo? command, IInteractionContext context, global::Discord.Interactions.IResult result)
	{
		if (result.IsSuccess)
		{
			return;
		}
		logger.LogError("Commande /{Command} en échec : {Error} — {Reason}", command?.Name, result.Error, result.ErrorReason);
		if (result.Error == InteractionCommandError.UnmetPrecondition && !context.Interaction.HasResponded &&
			context.Interaction.Type != InteractionType.ApplicationCommandAutocomplete)
		{
			try
			{
				await context.Interaction.RespondAsync(result.ErrorReason, ephemeral: true);
			}
			catch (Exception exception)
			{
				logger.LogDebug(exception, "Réponse de refus impossible.");
			}
		}
	}

	// Bot retiré du serveur : la guilde est marquée, ses données supprimées
	// après le délai de rétention (le temps de réinviter le bot par erreur).
	private async Task OnLeftGuildAsync(SocketGuild server) =>
		await SetRemovedAsync(server.Id, DateTime.UtcNow);

	private async Task OnJoinedGuildAsync(SocketGuild server) =>
		await SetRemovedAsync(server.Id, null);

	private async Task SetRemovedAsync(ulong serverId, DateTime? removedUtc)
	{
		try
		{
			await using var scope = services.CreateAsyncScope();
			var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
			var guild = await scope.ServiceProvider.GetRequiredService<GuildDirectory>().FindByDiscordServerAsync(serverId);
			if (guild is null)
			{
				return;
			}
			guild.BotRemovedUtc = removedUtc;
			await db.SaveChangesAsync();
			logger.LogInformation(removedUtc is null
				? "Bot réinvité sur le serveur de « {Guild} »."
				: "Bot retiré du serveur de « {Guild} » : suppression des données dans 30 jours.", guild.Name);
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Mise à jour de la guilde du serveur {ServerId} impossible.", serverId);
		}
	}

	private async Task PurgeRemovedGuildsLoopAsync(CancellationToken ct)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
		do
		{
			try
			{
				await using var scope = services.CreateAsyncScope();
				var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
				var threshold = DateTime.UtcNow - RemovedGuildRetention;
				var expired = await db.Guilds.Where(g => g.BotRemovedUtc != null && g.BotRemovedUtc < threshold).ToListAsync(ct);
				foreach (var guild in expired)
				{
					await scope.ServiceProvider.GetRequiredService<GuildDirectory>().DeleteAsync(guild, ct);
					logger.LogInformation("Guilde « {Guild} » supprimée (bot retiré depuis plus de 30 jours).", guild.Name);
				}
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				logger.LogError(exception, "Purge des guildes retirées en échec.");
			}
		}
		while (await timer.WaitForNextTickAsync(ct));
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
