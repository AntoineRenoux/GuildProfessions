namespace GuildProfessions.Server.Modules.Evenements.Contracts;

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
}
