using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Services;
using GuildProfessions.Server.Shared;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Héberge le bot dans le même process que l'API. Sans token configuré, ou si
/// Discord refuse la connexion (token révoqué, panne), le bot ne démarre pas
/// mais l'API reste utilisable : aucune erreur du bot ne remonte à l'hôte.
/// Un seul bot pour toutes les guildes : chaque interaction est rattachée à la
/// guilde du serveur Discord d'où elle vient.
/// Isolation : au chargement comme à la publication, l'unité est la commande
/// de premier niveau (/gkeep, /gkeep-setup). Les groupes des modules (métier,
/// commande, événement…) sont imbriqués dans /gkeep : un groupe invalide
/// ferait tomber tout /gkeep. C'est pourquoi CommandLocalizationTests construit
/// chaque groupe séparément et nomme celui qui échoue, avant toute mise en
/// production. À l'exécution, une commande de module qui lève une exception
/// reçoit une réponse « indisponible » sans toucher aux autres.
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

		try
		{
			client.Log += OnLog;
			interactions.Log += OnLog;
			client.Ready += OnReadyAsync;
			client.InteractionCreated += OnInteractionAsync;
			client.LeftGuild += OnLeftGuildAsync;
			client.JoinedGuild += OnJoinedGuildAsync;
			interactions.InteractionExecuted += OnInteractionExecutedAsync;

			await AddInteractionModulesAsync();
			await client.LoginAsync(TokenType.Bot, token);
			await client.StartAsync();
			_ = Task.Run(() => PurgeRemovedGuildsLoopAsync(_stopping.Token));
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Démarrage du bot Discord impossible : l'API HTTP continue de tourner sans le bot.");
		}
	}

	/// <summary>Modules d'interaction de premier niveau (les groupes de /gkeep sont imbriqués dans le leur).</summary>
	public static IReadOnlyList<Type> InteractionModuleTypes() =>
		typeof(DiscordBotService).Assembly.GetTypes()
			.Where(t => t is { IsNested: false, IsAbstract: false, IsClass: true } && typeof(IInteractionModuleBase).IsAssignableFrom(t))
			.OrderBy(t => t.FullName, StringComparer.Ordinal)
			.ToList();

	// Un module invalide est journalisé et laissé de côté : les autres se chargent quand même.
	private async Task AddInteractionModulesAsync()
	{
		foreach (var type in InteractionModuleTypes())
		{
			try
			{
				await interactions.AddModuleAsync(type, services);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Module Discord {Module} non chargé : ses commandes sont indisponibles.", type.Name);
			}
		}
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		await _stopping.CancelAsync();
		try
		{
			await client.StopAsync();
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Arrêt du bot Discord incomplet.");
		}
	}

	private async Task OnReadyAsync()
	{
		try
		{
			await RegisterCommandsAsync();
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Enregistrement des commandes slash en échec : les commandes déjà publiées restent en place.");
		}
		_ = Task.Run(ReconcileNicknamesAsync);
	}

	private async Task RegisterCommandsAsync()
	{
		var devServers = DiscordGuildIds.Parse(config);
		if (devServers.Count > 0)
		{
			// Développement : enregistrement immédiat sur les serveurs de test, chacun isolément.
			foreach (var serverId in devServers)
			{
				try
				{
					await interactions.RegisterCommandsToGuildAsync(serverId);
					logger.LogInformation("Commandes slash enregistrées sur le serveur {GuildId}.", serverId);
				}
				catch (Exception exception)
				{
					logger.LogError(exception, "Commandes slash non enregistrées sur le serveur {GuildId}.", serverId);
				}
			}
			await client.Rest.DeleteAllGlobalCommandsAsync();
		}
		else
		{
			// Production : commandes globales (~1 h de propagation), disponibles
			// sur tout serveur qui invite le bot. Les anciennes commandes par
			// serveur sont effacées, sinon chaque commande apparaîtrait en double.
			await RegisterGloballyAsync();
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
	}

	/// <summary>
	/// Discord refuse un envoi groupé dès qu'une commande est invalide (et
	/// garde alors les anciennes). En repli, chaque commande de premier niveau
	/// (/gkeep, /gkeep-setup) est publiée seule : /gkeep ne bloque pas
	/// /gkeep-setup, et inversement. L'isolation ne descend pas plus bas : une
	/// sous-commande refusée (un groupe de module) laisse tout /gkeep dans son
	/// ancienne version. Les tests construisent chaque groupe à part pour que
	/// ce cas soit pris avant la mise en production.
	/// </summary>
	private async Task RegisterGloballyAsync()
	{
		try
		{
			await interactions.RegisterCommandsGloballyAsync();
			return;
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Enregistrement groupé des commandes refusé : publication commande par commande.");
		}
		foreach (var module in interactions.Modules.Where(m => m.Parent is null))
		{
			try
			{
				await interactions.AddModulesGloballyAsync(false, module);
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Commande /{Command} refusée par Discord : elle reste dans son ancienne version.",
					module.SlashGroupName ?? module.Name);
			}
		}
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
				// Une guilde en échec (permission Discord, données) n'arrête pas les suivantes.
				try
				{
					await ReconcileGuildNicknamesAsync(guild);
				}
				catch (Exception exception)
				{
					logger.LogError(exception, "Réalignement des pseudos en échec pour « {Guild} ».", guild.Name);
				}
			}
		}
		catch (Exception exception)
		{
			logger.LogError(exception, "Réalignement des pseudos en échec.");
		}
	}

	private async Task ReconcileGuildNicknamesAsync(Guild guild)
	{
		await using var scope = services.CreateAsyncScope();
		scope.ServiceProvider.GetRequiredService<GuildContext>().Use(guild);
		var annuaire = scope.ServiceProvider.GetRequiredService<IAnnuaire>();
		var nicknames = scope.ServiceProvider.GetRequiredService<NicknameSync>();
		var targets = await annuaire.NicknameTargetsAsync();
		var updated = 0;
		foreach (var (discordId, characterName) in targets)
		{
			var outcomes = await nicknames.SyncAsync(guild, discordId, characterName);
			updated += outcomes.Count(o => o == NicknameOutcome.Updated);
		}
		logger.LogInformation("Pseudos réalignés pour « {Guild} » : {Updated} modifié(s) sur {Members} membre(s).",
			guild.Name, updated, targets.Count);
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
				// Réponses dans la langue de l'utilisateur (repli : langue du serveur).
				scope.ServiceProvider.GetRequiredService<BotLocalizer>().Language =
					BotLocalizer.FromLocale(interaction.UserLocale ?? interaction.GuildLocale);
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
	// silencieuse : l'utilisateur reçoit au moins la raison d'un refus, ou un
	// message « momentanément indisponible » si le module a levé une exception
	// (sinon Discord afficherait « réfléchit… » jusqu'à expiration).
	private async Task OnInteractionExecutedAsync(ICommandInfo? command, IInteractionContext context, global::Discord.Interactions.IResult result)
	{
		if (result.IsSuccess)
		{
			return;
		}
		logger.LogError("Commande /{Command} en échec : {Error} — {Reason}", command?.Name, result.Error, result.ErrorReason);
		if (context.Interaction.Type == InteractionType.ApplicationCommandAutocomplete)
		{
			return;
		}
		var message = result.Error switch
		{
			InteractionCommandError.UnmetPrecondition => result.ErrorReason,
			InteractionCommandError.Exception => BotLocalizer.For(BotLocalizer.FromLocale(context.Interaction.UserLocale ?? context.Interaction.GuildLocale))
				.T("Cette fonction est momentanément indisponible. Le reste de Guildkeep fonctionne ; réessaie plus tard.",
					"This feature is temporarily unavailable. The rest of Guildkeep works; try again later."),
			_ => null,
		};
		if (message is null)
		{
			return;
		}
		try
		{
			if (context.Interaction.HasResponded)
			{
				await context.Interaction.FollowupAsync(message, ephemeral: true);
			}
			else
			{
				await context.Interaction.RespondAsync(message, ephemeral: true);
			}
		}
		catch (Exception exception)
		{
			logger.LogDebug(exception, "Réponse d'échec impossible.");
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
			var guilds = scope.ServiceProvider.GetRequiredService<GuildDirectory>();
			var guild = await guilds.FindByDiscordServerAsync(serverId);
			if (guild is null)
			{
				return;
			}
			await guilds.MarkRemovedAsync(guild, removedUtc);
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
					// Une suppression en échec n'empêche pas celle des guildes suivantes.
					try
					{
						await scope.ServiceProvider.GetRequiredService<GuildDirectory>().DeleteAsync(guild, ct);
						logger.LogInformation("Guilde « {Guild} » supprimée (bot retiré depuis plus de 30 jours).", guild.Name);
					}
					catch (Exception exception) when (exception is not OperationCanceledException)
					{
						logger.LogError(exception, "Suppression de la guilde « {Guild} » en échec, nouvel essai au prochain cycle.", guild.Name);
						db.ChangeTracker.Clear();
					}
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
