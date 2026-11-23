namespace GuildProfessions.Server.Data;

// Entités du module Profils. Le namespace reste GuildProfessions.Server.Data,
// comme pour les autres modules (noms d'entités de l'historique des migrations).
// Les personnages sont désignés par leur nom : le module ne touche pas à l'annuaire.

/// <summary>
/// Profil d'un joueur : son personnage principal, ses rerolls (relevés sur
/// le même compte de jeu), les rôles qu'il joue, ses disponibilités et une note.
/// </summary>
public sealed class PlayerProfile : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public required string Main { get; set; }

	/// <summary>Rerolls, JSON ["Nom", …].</summary>
	public string AltsJson { get; set; } = "[]";

	/// <summary>Rôles joués, séparés par des virgules (tank, healer, melee, ranged).</summary>
	public string Roles { get; set; } = "";
	public string? Availability { get; set; }
	public string? Note { get; set; }

	/// <summary>Dernière modification acceptée (secondes Unix) : la plus récente gagne.</summary>
	public long UpdatedAt { get; set; }
}

/// <summary>Aperçu des talents d'un personnage (points par arbre, spécialisation dominante).</summary>
public sealed class CharacterTalents : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public required string Character { get; set; }
	public string? Spec { get; set; }
	public required string Summary { get; set; }
	public long At { get; set; }
}

/// <summary>Version de l'addon relevée sur un personnage (vérification par les officiers).</summary>
public sealed class AddonVersionReport : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public required string Character { get; set; }
	public required string Version { get; set; }
	public long At { get; set; }
}

/// <summary>Arrivée ou départ d'un membre, relevé par un addon en jeu (join | leave).</summary>
public sealed class RosterChange : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public required string Character { get; set; }
	public required string Kind { get; set; }
	public long At { get; set; }
	public string? ReportedBy { get; set; }
}

/// <summary>Étape d'accueil d'un joueur (discord, main, profile, handbook, intro), cochée par lui ou un officier.</summary>
public sealed class OnboardingStep : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public required string Character { get; set; }
	public required string Step { get; set; }
	public bool Done { get; set; }
	public string? By { get; set; }
	public long At { get; set; }
}
