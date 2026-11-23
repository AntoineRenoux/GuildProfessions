using GuildProfessions.Server.Contracts;

namespace GuildProfessions.Server.Shared.Modules;

/// <summary>
/// Export en cours de construction : chaque module remplit ses sections en
/// les assignant (jamais en modifiant sur place celles d'un autre module).
/// Une section qu'aucun module n'a fournie reste null : c'est ainsi qu'une
/// section d'un module en panne est absente de l'export, les autres étant
/// servies normalement (même forme JSON que sur le chemin nominal).
/// </summary>
public sealed class ExportDraft
{
	public Dictionary<int, string>? RecipeNames { get; set; }

	public List<ExportCharacterDto>? Characters { get; set; }

	public List<ExportOrderDto>? Orders { get; set; }

	public List<ExportEventDto>? Events { get; set; }

	public List<ExportRaidDto>? Raids { get; set; }

	public List<ExportCatalogRecipeDto>? Catalog { get; set; }

	public ExportCommunicationDto? Communication { get; set; }

	public ExportProfilesDto? Profiles { get; set; }

	/// <summary>Copie des sections avant l'appel d'un module, pour annuler sa part s'il échoue en cours de route.</summary>
	internal ExportDraft Snapshot() => (ExportDraft)MemberwiseClone();

	internal void RestoreFrom(ExportDraft snapshot)
	{
		RecipeNames = snapshot.RecipeNames;
		Characters = snapshot.Characters;
		Orders = snapshot.Orders;
		Events = snapshot.Events;
		Raids = snapshot.Raids;
		Catalog = snapshot.Catalog;
		Communication = snapshot.Communication;
		Profiles = snapshot.Profiles;
	}
}

/// <summary>Part d'un module dans l'export ; appelée isolément, son échec n'empêche pas les autres sections.</summary>
public interface IExportContributor
{
	string Module { get; }

	/// <summary>Ordre d'appel : l'annuaire d'abord, les quêtes complètent ensuite les personnages.</summary>
	int Order { get; }

	Task ContributeAsync(ExportDraft draft, CancellationToken ct);
}
