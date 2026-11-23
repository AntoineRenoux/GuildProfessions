namespace GuildProfessions.Server.Data;

// Entités du module Presence. Le namespace reste GuildProfessions.Server.Data :
// l'historique des migrations EF (AppDbContextModelSnapshot) nomme chaque
// entité par son nom CLR complet, le changer imposerait une migration à vide.

/// <summary>
/// Raid réellement joué, relevé en jeu par les membres présents (et non
/// d'après les inscriptions). Plusieurs postes relèvent le même raid : ils
/// partagent la même clé « jour|instance|chef de raid » et leurs relevés
/// sont fusionnés (le plus long temps de présence gagne).
/// </summary>
public sealed class RaidSession : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }

	public required string Key { get; set; }
	public required string Instance { get; set; }

	/// <summary>Jour du raid, « AAAA-MM-JJ » (heure du royaume).</summary>
	public required string Day { get; set; }
	public string? Leader { get; set; }
	public long StartedAt { get; set; }
	public long EndedAt { get; set; }

	/// <summary>Événement de guilde correspondant (même soir), s'il y en a un.</summary>
	public int? EventId { get; set; }

	public List<RaidParticipant> Participants { get; set; } = [];
	public List<RaidLoot> Loot { get; set; } = [];
	public List<AttendanceCorrection> Corrections { get; set; } = [];
}

public sealed class RaidParticipant
{
	public int Id { get; set; }
	public int SessionId { get; set; }
	public RaidSession Session { get; set; } = null!;

	public required string Character { get; set; }
	public string? ClassFile { get; set; }

	/// <summary>Minutes de présence relevées (la plus grande valeur des observateurs).</summary>
	public int Minutes { get; set; }
}

/// <summary>Objet épique ou mieux reçu pendant un raid. Ordinal : n-ième exemplaire du même objet pour ce joueur.</summary>
public sealed class RaidLoot
{
	public int Id { get; set; }
	public int SessionId { get; set; }
	public RaidSession Session { get; set; } = null!;

	public required string Character { get; set; }
	public int ItemId { get; set; }
	public string? ItemName { get; set; }
	public int Quality { get; set; }
	public int Ordinal { get; set; } = 1;
	public long At { get; set; }

	// Correction d'officier, à part du relevé : un objet annulé (désenchanté, erreur) n'est jamais effacé.
	public bool Voided { get; set; }
	public string? VoidNote { get; set; }
	public string? VoidBy { get; set; }
	public long VoidAt { get; set; }
}

/// <summary>Correction de présence par un officier : present, absent ou excused (excusé = hors du calcul).</summary>
public sealed class AttendanceCorrection
{
	public int Id { get; set; }
	public int SessionId { get; set; }
	public RaidSession Session { get; set; } = null!;

	public required string Character { get; set; }
	public required string Status { get; set; }
	public string? Note { get; set; }
	public string? By { get; set; }
	public long At { get; set; }
}
