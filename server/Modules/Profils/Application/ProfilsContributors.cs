using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Profils.Application;

/// <summary>Part des profils dans un envoi : profils, talents, versions, arrivées et départs, accueil.</summary>
public sealed class ProfilsUploadContributor(ProfileService profiles) : IUploadContributor
{
	public string Module => "Profils";

	public int Order => 60;

	public async Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct) =>
		await profiles.ApplyUploadAsync(context, ct);
}

/// <summary>Part des profils dans l'export.</summary>
public sealed class ProfilsExportContributor(ProfileService profiles) : IExportContributor
{
	public string Module => "Profils";

	public int Order => 60;

	public async Task ContributeAsync(ExportDraft draft, CancellationToken ct) =>
		draft.Profiles = await profiles.BuildExportAsync(ct);
}
