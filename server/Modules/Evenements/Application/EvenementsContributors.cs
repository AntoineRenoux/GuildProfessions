using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Evenements.Application;

/// <summary>Part des événements dans un envoi : événements, inscriptions, roster et soirée de raid.</summary>
public sealed class EvenementsUploadContributor(EventService events) : IUploadContributor
{
	public string Module => "Événements";

	public int Order => 20;

	public async Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct) =>
		await events.ApplyUploadAsync(context, ct);
}

/// <summary>Part des événements dans l'export.</summary>
public sealed class EvenementsExportContributor(EventService events) : IExportContributor
{
	public string Module => "Événements";

	public int Order => 20;

	public async Task ContributeAsync(ExportDraft draft, CancellationToken ct) =>
		draft.Events = await events.BuildExportAsync(ct);
}
