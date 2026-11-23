namespace GuildProfessions.Server.Data;

// Entités du module Quêtes. Le namespace reste GuildProfessions.Server.Data,
// comme pour les autres modules (noms d'entités de l'historique des migrations).

/// <summary>
/// Journal de quêtes partageables d'un personnage (élites, donjon, raid),
/// relevé en jeu. Propre au module Quêtes : il ne touche pas à l'annuaire et
/// ne référence le personnage que par son nom.
/// </summary>
public sealed class CharacterQuestLog : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }

	public required string Character { get; set; }

	/// <summary>Quêtes relevées, JSON [{ Id, Title, Kind, Location, Level, Complete }].</summary>
	public required string QuestsJson { get; set; }

	public DateTime UpdatedUtc { get; set; }
}
