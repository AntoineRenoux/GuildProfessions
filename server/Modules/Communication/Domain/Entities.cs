namespace GuildProfessions.Server.Data;

// Entités du module Communication. Le namespace reste GuildProfessions.Server.Data,
// comme pour les autres modules (noms d'entités de l'historique des migrations).

/// <summary>Annonce d'un officier à la guilde, éventuellement liée à un événement.</summary>
public sealed class Announcement : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }

	/// <summary>Id généré en jeu pour une annonce rédigée dans l'addon — clé de déduplication des envois.</summary>
	public string? ClientId { get; set; }

	public required string Title { get; set; }
	public required string Body { get; set; }

	/// <summary>Personnage (ou nom Discord) de l'officier qui l'a publiée.</summary>
	public required string Author { get; set; }

	/// <summary>Événement concerné (id serveur), par simple référence : le module Événements reste maître de ses données.</summary>
	public int? EventId { get; set; }

	public long CreatedAt { get; set; }

	/// <summary>Dernière modification acceptée (secondes Unix) : la plus récente gagne.</summary>
	public long UpdatedAt { get; set; }

	public bool Deleted { get; set; }
	public DataSource Source { get; set; }
}

/// <summary>Modèle d'annonce réutilisable ; {event}, {date} et {guild} sont remplacés à la publication.</summary>
public sealed class AnnouncementTemplate : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public required string Key { get; set; }
	public required string Title { get; set; }
	public required string Body { get; set; }
	public long UpdatedAt { get; set; }
	public bool Deleted { get; set; }
}

/// <summary>Section de la charte de la guilde (règlement, règles de butin…), modifiable par les officiers.</summary>
public sealed class HandbookSection : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public required string Key { get; set; }
	public required string Title { get; set; }
	public required string Body { get; set; }
	public int Order { get; set; }
	public string? UpdatedBy { get; set; }
	public long UpdatedAt { get; set; }
	public bool Deleted { get; set; }
}

/// <summary>Objectif de guilde chiffré (ex. 200 flacons pour le raid), auquel les membres contribuent.</summary>
public sealed class GuildGoal : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public string? ClientId { get; set; }
	public required string Title { get; set; }
	public string? Description { get; set; }
	public int Target { get; set; }

	/// <summary>Unité affichée (« flacons », « po »…).</summary>
	public string? Unit { get; set; }

	/// <summary>Échéance (secondes Unix), facultative.</summary>
	public long? Deadline { get; set; }

	public bool Closed { get; set; }
	public required string CreatedBy { get; set; }
	public long UpdatedAt { get; set; }

	public List<GoalContribution> Contributions { get; set; } = [];
}

/// <summary>Contribution d'un membre à un objectif ; ClientId la rend idempotente quel que soit le nombre d'envois.</summary>
public sealed class GoalContribution
{
	public int Id { get; set; }
	public int GoalId { get; set; }
	public GuildGoal Goal { get; set; } = null!;
	public required string ClientId { get; set; }
	public required string Character { get; set; }
	public int Amount { get; set; }
	public string? Note { get; set; }
	public long At { get; set; }
}

/// <summary>Réglages Discord du module : salon où publier les annonces.</summary>
public sealed class CommunicationSettings : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public string? AnnouncementsChannelId { get; set; }
}
