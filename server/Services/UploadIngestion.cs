using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Ingestion d'un envoi (compagnon ou /import Discord) dans la guilde
/// courante : filtre d'appartenance, roster, commandes, puis retrait des
/// personnages partis dans une autre guilde.
/// </summary>
public sealed class UploadIngestion(RosterService roster, CraftOrderService craftOrders, GuildContext guildContext,
	BotLocalizer? localizer = null)
{
	private readonly BotLocalizer L = localizer ?? new BotLocalizer();

	public async Task<UploadResult> IngestAsync(Member? member, UploadPayload payload, CancellationToken ct = default)
	{
		var filter = GuildFilter.Apply(payload, guildContext.Required.Name);
		var result = await roster.ApplyUploadAsync(member, filter.Payload, ct);
		var orderWarnings = await craftOrders.ApplyUploadAsync(member, filter.Payload, ct);
		var removed = await roster.RemoveDepartedCharactersAsync(filter.Departed, ct);

		var guildName = guildContext.Required.Name;
		List<string> warnings = [
			.. filter.Rejected.Select(r => L.T(
				$"{r.Character} ignoré : guilde « {r.Guild} » au lieu de « {guildName} ».",
				$"{r.Character} ignored: guild \"{r.Guild}\" instead of \"{guildName}\".")),
			.. result.Warnings,
			.. orderWarnings,
		];
		if (removed > 0)
		{
			warnings.Add(L.T(
				$"{removed} personnage(s) retiré(s) de l'annuaire : ils sont maintenant dans une autre guilde.",
				$"{removed} character(s) removed from the directory: they are now in another guild."));
		}
		var version = removed > 0 ? await roster.GetDataVersionAsync(ct) : result.Version;
		return result with { Version = version, Warnings = warnings };
	}
}
