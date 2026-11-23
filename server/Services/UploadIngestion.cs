using System.Collections.Concurrent;
using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Issue d'un envoi : la réponse au client (forme inchangée) et les modules
/// qui ont échoué. Un envoi incomplet est signalé au client (HTTP 503) pour
/// qu'il le renvoie : chaque module est idempotent, rejouer l'envoi est sans danger.
/// </summary>
public sealed record UploadOutcome(UploadResult Result, IReadOnlyList<string> FailedModules)
{
	public bool Complete => FailedModules.Count == 0;
}

/// <summary>
/// Ingestion d'un envoi (compagnon ou /import Discord) dans la guilde
/// courante : filtre d'appartenance, puis chaque module dans sa propre
/// transaction. Un module en panne est annulé et signalé par un
/// avertissement ; les autres sont appliqués quand même, et la réponse garde
/// la même forme (version et avertissements). Les envois d'une même guilde
/// passent l'un après l'autre : SQLite n'a qu'un écrivain, deux transactions
/// longues simultanées finiraient en « base verrouillée ».
/// </summary>
public sealed class UploadIngestion(
	AppDbContext db,
	IServiceProvider services,
	IEnumerable<UploadContributorType> contributorTypes,
	IDataVersion dataVersion,
	GuildContext guildContext,
	BotLocalizer localizer,
	ILogger<UploadIngestion> logger)
{
	private static readonly ConcurrentDictionary<int, SemaphoreSlim> GuildLocks = new();

	public async Task<UploadOutcome> IngestAsync(int? memberId, UploadPayload payload, UploadSource source, CancellationToken ct = default)
	{
		var guild = guildContext.Required;
		var gate = GuildLocks.GetOrAdd(guild.Id, _ => new SemaphoreSlim(1, 1));
		await gate.WaitAsync(ct);
		try
		{
			return await IngestLockedAsync(memberId, payload, source, ct);
		}
		finally
		{
			gate.Release();
		}
	}

	private async Task<UploadOutcome> IngestLockedAsync(int? memberId, UploadPayload payload, UploadSource source, CancellationToken ct)
	{
		var guildName = guildContext.Required.Name;
		var filter = GuildFilter.Apply(payload, guildName);
		var failed = new List<string>();

		List<string> warnings = [
			.. filter.Rejected.Select(r => localizer.T(
				$"{r.Character} ignoré : guilde « {r.Guild} » au lieu de « {guildName} ».",
				$"{r.Character} ignored: guild \"{r.Guild}\" instead of \"{guildName}\".")),
		];
		var departed = await RemovableDepartedAsync(memberId, source, filter.Departed, ct);
		var context = new UploadContext(memberId, filter.Payload, source, departed);

		var contributors = new List<IUploadContributor>();
		foreach (var registration in contributorTypes)
		{
			// Construit à part : un module mal câblé n'empêche pas la construction des autres.
			try
			{
				contributors.Add((IUploadContributor)services.GetRequiredService(registration.Type));
			}
			catch (Exception exception) when (!ct.IsCancellationRequested)
			{
				logger.LogError(exception, "Envoi : le contributeur {Contributor} ne peut pas être construit.", registration.Type.Name);
				failed.Add(registration.Type.Name);
				warnings.Add(Unavailable(registration.Type.Name));
			}
		}
		foreach (var contributor in contributors.OrderBy(c => c.Order))
		{
			var (moduleWarnings, ok) = await ApplyIsolatedAsync(contributor, context, ct);
			warnings.AddRange(moduleWarnings);
			if (!ok)
			{
				failed.Add(contributor.Module);
			}
		}
		return new UploadOutcome(new UploadResult(true, await dataVersion.CurrentAsync(ct), warnings), failed);
	}

	/// <summary>
	/// Personnages partis que cet envoi a le droit de retirer (règle de
	/// l'annuaire). Si l'annuaire ne répond pas, on ne retire rien : c'est le
	/// choix sans perte, le prochain envoi les retirera.
	/// </summary>
	private async Task<IReadOnlyList<string>> RemovableDepartedAsync(int? memberId, UploadSource source, List<string> departed,
		CancellationToken ct)
	{
		if (departed.Count == 0)
		{
			return [];
		}
		try
		{
			return await services.GetRequiredService<IAnnuaire>().RemovableDepartedAsync(memberId, source, departed, ct);
		}
		catch (Exception exception) when (!ct.IsCancellationRequested)
		{
			logger.LogError(exception, "Envoi : personnages partis non vérifiables, aucun n'est retiré.");
			db.ChangeTracker.Clear();
			return [];
		}
	}

	private async Task<(IReadOnlyList<string> Warnings, bool Ok)> ApplyIsolatedAsync(IUploadContributor contributor, UploadContext context,
		CancellationToken ct)
	{
		var afterCommit = new List<Func<Task>>();
		IReadOnlyList<string> warnings;
		try
		{
			await using var transaction = await db.Database.BeginTransactionAsync(ct);
			warnings = await contributor.ApplyAsync(context, afterCommit, ct);
			await db.SaveChangesAsync(ct);
			await transaction.CommitAsync(ct);
		}
		// Seule l'annulation de l'appelant interrompt l'envoi ; une annulation interne au module (délai) reste confinée.
		catch (Exception exception) when (!ct.IsCancellationRequested)
		{
			// Ce qui a été suivi par ce module ne doit pas fuiter dans la sauvegarde du suivant.
			db.ChangeTracker.Clear();
			logger.LogError(exception, "Envoi : le module {Module} a échoué pour la guilde {Guild}, ses changements sont annulés.",
				contributor.Module, guildContext.Required.Name);
			return ([Unavailable(contributor.Module)], false);
		}
		db.ChangeTracker.Clear();
		await SendAsync(contributor, afterCommit);
		return (warnings, true);
	}

	private string Unavailable(string module) => localizer.T(
		$"Module {module} indisponible : cette partie de l'envoi n'a pas été prise en compte, le reste l'a été. Elle sera renvoyée au prochain envoi ; contacte un administrateur si ça persiste.",
		$"{module} module unavailable: this part of the upload was not applied, the rest was. It will be sent again with the next upload; contact an administrator if it persists.");

	// Messages Discord du module, une fois son écriture validée ; leur échec n'annule rien.
	private async Task SendAsync(IUploadContributor contributor, List<Func<Task>> afterCommit)
	{
		foreach (var send in afterCommit)
		{
			try
			{
				await send();
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "Envoi : notification du module {Module} impossible.", contributor.Module);
			}
		}
	}
}
