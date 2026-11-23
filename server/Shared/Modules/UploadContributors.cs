using GuildProfessions.Server.Contracts;

namespace GuildProfessions.Server.Shared.Modules;

/// <summary>Canal d'un envoi : il décide de la confiance accordée aux champs déclarés par le client.</summary>
public enum UploadSource
{
	/// <summary>Compagnon authentifié par le token personnel d'un membre.</summary>
	PersonalToken,

	/// <summary>Compagnon zéro-config authentifié par le token partagé de la guilde.</summary>
	GuildToken,

	/// <summary>Chaîne collée à la main dans /gkeep import : rien n'en garantit le contenu.</summary>
	DiscordImport,
}

/// <summary>
/// Un envoi déjà filtré sur la guilde courante (GuildFilter). MemberId est
/// null pour un envoi partagé : l'identité est alors « les personnages de
/// l'envoi ». Le noyau ne porte que l'identifiant du membre, jamais l'entité
/// de l'annuaire : c'est l'annuaire qui sait ce qu'est un membre.
/// Departed ne contient que les personnages que cet envoi a le droit de
/// retirer (voir IAnnuaire.RemovableDepartedAsync).
/// </summary>
public sealed record UploadContext(int? MemberId, UploadPayload Payload, UploadSource Source, IReadOnlyList<string> Departed);

/// <summary>
/// Part d'un module dans l'ingestion d'un envoi. L'orchestrateur appelle
/// chaque contributeur dans sa propre transaction : si l'un échoue, ses
/// écritures sont annulées et les autres modules sont quand même appliqués.
/// </summary>
public interface IUploadContributor
{
	/// <summary>Nom du module, en clair, pour les journaux et l'avertissement « module indisponible ».</summary>
	string Module { get; }

	/// <summary>Ordre d'appel : l'annuaire d'abord (il lie les personnages), la présence après les événements.</summary>
	int Order { get; }

	/// <summary>
	/// Applique la part du module et renvoie ses avertissements. Les effets
	/// externes (messages Discord) vont dans <paramref name="afterCommit"/> :
	/// ils ne partent qu'une fois la transaction du module validée.
	/// </summary>
	Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct);
}
