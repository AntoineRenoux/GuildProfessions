using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Presence.Application;

/// <summary>Part de la présence dans un envoi : raids relevés, butin et corrections d'officiers.</summary>
public sealed class PresenceUploadContributor(AttendanceService attendance) : IUploadContributor
{
	public string Module => "Présence et butin";

	// Après les événements : un raid relevé se rattache à l'événement du soir.
	public int Order => 30;

	public async Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct) =>
		await attendance.ApplyUploadAsync(context, ct);
}

/// <summary>Part de la présence dans l'export : raids des 90 derniers jours.</summary>
public sealed class PresenceExportContributor(AttendanceService attendance) : IExportContributor
{
	public string Module => "Présence et butin";

	public int Order => 30;

	public async Task ContributeAsync(ExportDraft draft, CancellationToken ct) =>
		draft.Raids = await attendance.BuildExportAsync(ct);
}
