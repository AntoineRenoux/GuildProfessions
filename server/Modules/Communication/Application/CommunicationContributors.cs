using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Communication.Application;

/// <summary>Part de la communication dans un envoi : annonces, modèles, charte, objectifs et contributions.</summary>
public sealed class CommunicationUploadContributor(CommunicationService communication) : IUploadContributor
{
	public string Module => "Communication";

	public int Order => 50;

	public async Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct) =>
		await communication.ApplyUploadAsync(context, afterCommit, ct);
}

/// <summary>Part de la communication dans l'export.</summary>
public sealed class CommunicationExportContributor(CommunicationService communication) : IExportContributor
{
	public string Module => "Communication";

	public int Order => 50;

	public async Task ContributeAsync(ExportDraft draft, CancellationToken ct) =>
		draft.Communication = await communication.BuildExportAsync(ct);
}
