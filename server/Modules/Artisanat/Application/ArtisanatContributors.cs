using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Artisanat.Application;

/// <summary>Part de l'artisanat dans un envoi : catalogue des recettes, puis commandes, offres et changements d'état.</summary>
public sealed class ArtisanatUploadContributor(CatalogService catalog, CraftOrderService orders) : IUploadContributor
{
	public string Module => "Artisanat";

	public int Order => 10;

	public async Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct)
	{
		await catalog.ApplyUploadAsync(context.Payload, ct);
		return await orders.ApplyUploadAsync(context, afterCommit, ct);
	}
}

/// <summary>Part de l'artisanat dans l'export : commandes en cours et catalogue.</summary>
public sealed class ArtisanatExportContributor(CatalogService catalog, CraftOrderService orders) : IExportContributor
{
	public string Module => "Artisanat";

	public int Order => 10;

	public async Task ContributeAsync(ExportDraft draft, CancellationToken ct)
	{
		draft.Orders = await orders.BuildExportAsync(ct);
		draft.Catalog = await catalog.BuildExportAsync(ct);
	}
}
