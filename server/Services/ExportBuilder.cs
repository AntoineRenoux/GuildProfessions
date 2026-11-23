using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Export construit et modules en échec. Un export incomplet est servi quand
/// même (les sections des modules en échec y sont null) : la panne d'un
/// module ne doit pas priver les autres de leurs données. Il est signalé à
/// part (en-tête HTTP, message Discord) pour que le compagnon ne le tienne
/// pas pour définitif.
/// </summary>
public sealed record ExportBuild(ExportDto Export, IReadOnlyList<string> FailedModules)
{
	public bool Complete => FailedModules.Count == 0;
}

/// <summary>
/// Export de la guilde courante, assemblé section par section par les
/// modules. Un module en panne est journalisé et sa part annulée (ses
/// sections restent null, même s'il avait commencé à les remplir) ; les
/// autres sections sont construites et servies normalement.
/// </summary>
public sealed class ExportBuilder(
	IServiceProvider services,
	IEnumerable<ExportContributorType> contributorTypes,
	IDataVersion dataVersion,
	GuildContext guildContext,
	ILogger<ExportBuilder> logger)
{
	public async Task<ExportBuild> BuildAsync(CancellationToken ct = default)
	{
		// Version lue d'abord : un changement pendant la construction donnera une version plus récente au prochain export.
		var version = await dataVersion.CurrentAsync(ct);
		var draft = new ExportDraft();
		var failed = new List<string>();
		var contributors = new List<IExportContributor>();
		foreach (var registration in contributorTypes)
		{
			try
			{
				contributors.Add((IExportContributor)services.GetRequiredService(registration.Type));
			}
			catch (Exception exception) when (!ct.IsCancellationRequested)
			{
				logger.LogError(exception, "Export : le contributeur {Contributor} ne peut pas être construit.", registration.Type.Name);
				failed.Add(registration.Type.Name);
			}
		}
		foreach (var contributor in contributors.OrderBy(c => c.Order))
		{
			var snapshot = draft.Snapshot();
			try
			{
				await contributor.ContributeAsync(draft, ct);
			}
			catch (Exception exception) when (!ct.IsCancellationRequested)
			{
				// Ce que le module avait déjà assigné est retiré : une section à moitié remplie passerait pour complète.
				draft.RestoreFrom(snapshot);
				logger.LogError(exception, "Export : la section du module {Module} est en échec pour la guilde {Guild}.",
					contributor.Module, guildContext.Required.Name);
				failed.Add(contributor.Module);
			}
		}
		if (failed.Count > 0)
		{
			logger.LogError("Export partiel servi pour la guilde {Guild} : sections absentes pour {Modules}.",
				guildContext.Required.Name, string.Join(", ", failed));
		}
		var export = new ExportDto(version, DateTime.UtcNow, draft.RecipeNames, draft.Characters, draft.Orders,
			guildContext.Required.Name, draft.Events, draft.Raids, draft.Catalog, draft.Communication, draft.Profiles);
		return new ExportBuild(export, failed);
	}
}
