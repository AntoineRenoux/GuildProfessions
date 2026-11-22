using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Ingestion d'un envoi (compagnon ou /import Discord) dans la guilde
/// courante : filtre d'appartenance, roster, commandes, puis retrait des
/// personnages partis dans une autre guilde.
/// </summary>
public sealed class UploadIngestion(RosterService roster, CraftOrderService craftOrders, GuildContext guildContext)
{
	public async Task<UploadResult> IngestAsync(Member? member, UploadPayload payload, CancellationToken ct = default)
	{
		var filter = GuildFilter.Apply(payload, guildContext.Required.Name);
		var result = await roster.ApplyUploadAsync(member, filter.Payload, ct);
		var orderWarnings = await craftOrders.ApplyUploadAsync(member, filter.Payload, ct);
		var removed = await roster.RemoveDepartedCharactersAsync(filter.Departed, ct);

		List<string> warnings = [.. filter.Warnings, .. result.Warnings, .. orderWarnings];
		if (removed > 0)
		{
			warnings.Add($"{removed} personnage(s) retiré(s) de l'annuaire : ils sont maintenant dans une autre guilde.");
		}
		var version = removed > 0 ? await roster.GetDataVersionAsync(ct) : result.Version;
		return result with { Version = version, Warnings = warnings };
	}
}
