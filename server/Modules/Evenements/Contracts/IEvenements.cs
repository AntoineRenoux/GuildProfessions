namespace GuildProfessions.Server.Modules.Evenements.Contracts;

/// <summary>Résumé d'un événement pour les autres modules (titre, date, annulation).</summary>
public sealed record EventBrief(int Id, string Title, long StartsAt, bool Cancelled);

/// <summary>Contrat public du module Événements : ce que les autres modules peuvent en lire.</summary>
public interface IEvenements
{
	/// <summary>
	/// Événement de raid (ou de donjon) prévu à moins de <paramref name="window"/>
	/// du début d'un raid joué, non annulé. À défaut d'autre indice, le plus
	/// proche ; mais un événement dont des inscrits figurent parmi les
	/// participants passe devant. null si aucun ne convient.
	/// </summary>
	Task<int?> EventOfRaidAsync(long startedAt, TimeSpan window, IReadOnlyCollection<string> participants, CancellationToken ct = default);

	/// <summary>Titre et date d'un événement de la guilde, pour le citer (annonces) ; null s'il est inconnu.</summary>
	Task<EventBrief?> EventBriefAsync(int eventId, CancellationToken ct = default);

	/// <summary>Prochains événements non annulés (encore visibles quatre heures après leur début), le plus proche d'abord.</summary>
	Task<IReadOnlyList<EventBrief>> UpcomingBriefsAsync(int max, CancellationToken ct = default);
}
