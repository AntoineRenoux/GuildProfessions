namespace GuildProfessions.Server.Data;

// Entités du module Evenements. Le namespace reste GuildProfessions.Server.Data :
// l'historique des migrations EF (AppDbContextModelSnapshot) nomme chaque
// entité par son nom CLR complet, le changer imposerait une migration à vide.

/// <summary>
/// Événement de guilde (raid, donjon, sortie…) créé en jeu ou sur Discord.
/// Les dates sont en secondes Unix (UTC) : l'addon les tient de
/// GetServerTime(), commun à tous les clients, et Discord les affiche dans
/// le fuseau de chacun.
/// </summary>
public sealed class GuildEvent : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }

	/// <summary>Id généré par l'addon pour un événement créé en jeu — clé de déduplication des uploads.</summary>
	public string? ClientId { get; set; }

	public required string Title { get; set; }

	/// <summary>raid | dungeon | pvp | other.</summary>
	public string Kind { get; set; } = "raid";

	public long StartsAt { get; set; }
	public string? Description { get; set; }

	/// <summary>Personnage organisateur (nom complet) : il gère l'événement et son roster.</summary>
	public required string Organizer { get; set; }

	// Composition visée (0 = pas d'objectif pour ce rôle).
	public int TargetTank { get; set; }
	public int TargetHealer { get; set; }
	public int TargetMelee { get; set; }
	public int TargetRanged { get; set; }

	public bool Cancelled { get; set; }

	/// <summary>Réservations de butin verrouillées : seuls l'organisateur et les officiers les changent encore.</summary>
	public bool ReservesLocked { get; set; }

	/// <summary>Instant (Unix) de la dernière modification acceptée : la plus récente gagne.</summary>
	public long UpdatedAt { get; set; }

	public DateTime CreatedUtc { get; set; }
	public DataSource Source { get; set; }

	public List<EventSignup> Signups { get; set; } = [];
	public List<EventReserve> Reserves { get; set; } = [];
	public List<EventHardReserve> HardReserves { get; set; } = [];
	public List<EventDuty> Duties { get; set; } = [];
}

/// <summary>
/// Inscription d'un personnage à un événement : sa réponse (choisie par le
/// joueur) et sa place dans le roster (choisie par l'organisateur ou un
/// officier), chacune horodatée à part pour que l'une n'écrase pas l'autre.
/// </summary>
public sealed class EventSignup
{
	public int Id { get; set; }
	public int EventId { get; set; }
	public GuildEvent Event { get; set; } = null!;

	public required string Character { get; set; }
	public string? ClassFile { get; set; }

	/// <summary>tank | healer | melee | ranged.</summary>
	public string Role { get; set; } = "melee";
	public string? Spec { get; set; }

	/// <summary>yes | tentative | no.</summary>
	public string Response { get; set; } = "yes";
	public string? Note { get; set; }
	public long ResponseAt { get; set; }

	/// <summary>pending | confirmed | standby | bench.</summary>
	public string Placement { get; set; } = "pending";
	public long PlacementAt { get; set; }
}

/// <summary>
/// Réservation de butin (« soft reserve ») : au plus un objet par priorité
/// (high, normal, low) et par joueur. ItemId 0 = réservation retirée,
/// gardée pour que la plus récente gagne même après un retrait.
/// </summary>
public sealed class EventReserve
{
	public int Id { get; set; }
	public int EventId { get; set; }
	public GuildEvent Event { get; set; } = null!;

	public required string Character { get; set; }
	public string Priority { get; set; } = "normal";
	public int ItemId { get; set; }
	public string? ItemName { get; set; }

	/// <summary>Qui l'a posée : le joueur, ou un officier pour un joueur sans addon.</summary>
	public string? SetBy { get; set; }
	public long At { get; set; }
}

/// <summary>Objet réservé d'office (« hard reserve ») : hors des réservations des joueurs.</summary>
public sealed class EventHardReserve
{
	public int Id { get; set; }
	public int EventId { get; set; }
	public GuildEvent Event { get; set; } = null!;

	public int ItemId { get; set; }
	public string? ItemName { get; set; }

	/// <summary>Destinataire prévu (facultatif : sinon décidé par le chef de raid).</summary>
	public string? Holder { get; set; }
	public bool Active { get; set; } = true;
	public string? SetBy { get; set; }
	public long At { get; set; }
}

/// <summary>Rôle de soutien d'une soirée : summoner, consumables, repair ou lead.</summary>
public sealed class EventDuty
{
	public int Id { get; set; }
	public int EventId { get; set; }
	public GuildEvent Event { get; set; } = null!;

	public required string Duty { get; set; }

	/// <summary>Personnage chargé du rôle ; null = poste libre.</summary>
	public string? Character { get; set; }
	public string? SetBy { get; set; }
	public long At { get; set; }
}
