namespace GuildProfessions.Server.Data;

// Entités du module Artisanat. Le namespace reste GuildProfessions.Server.Data :
// l'historique des migrations EF (AppDbContextModelSnapshot) nomme chaque
// entité par son nom CLR complet, le changer imposerait une migration à vide.

public enum CraftOrderStatus
{
	/// <summary>Ouverte : attend un artisan (offres possibles).</summary>
	Open = 0,
	/// <summary>Un artisan s'en charge.</summary>
	Accepted = 1,
	/// <summary>Le demandeur a confirmé la réception.</summary>
	Done = 2,
	Cancelled = 3,
	/// <summary>Fabriqué, prêt à être échangé en main propre.</summary>
	Ready = 4,
	/// <summary>Envoyé par courrier.</summary>
	Mailed = 5,
	/// <summary>Restée ouverte trop longtemps sans artisan.</summary>
	Expired = 6,
}

public sealed class CraftOrder : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }

	/// <summary>Id généré par l'addon pour une commande créée en jeu — clé de déduplication des uploads.</summary>
	public string? ClientId { get; set; }

	/// <summary>Noms en clair (pas de FK) : une commande peut viser un personnage pas encore connu du roster.</summary>
	public required string RequesterCharacter { get; set; }

	/// <summary>Artisan chargé de la commande ; null = demande ouverte à tous les artisans.</summary>
	public string? CrafterCharacter { get; set; }

	/// <summary>Recette visée (id de sort), quand la demande vient du catalogue ou d'une liste de recettes.</summary>
	public int? RecipeId { get; set; }

	/// <summary>Une demande encore ouverte à cette date expire.</summary>
	public DateTime? ExpiresUtc { get; set; }

	public List<CraftOffer> Offers { get; set; } = [];

	public required string Item { get; set; }
	public int Quantity { get; set; } = 1;
	public string? Note { get; set; }
	public CraftOrderStatus Status { get; set; }
	public DateTime CreatedUtc { get; set; }
	public DateTime UpdatedUtc { get; set; }
	public DataSource Source { get; set; }
}

/// <summary>Proposition d'un artisan sur une demande ouverte ; le demandeur en choisit une.</summary>
public sealed class CraftOffer
{
	public int Id { get; set; }
	public int OrderId { get; set; }
	public CraftOrder Order { get; set; } = null!;

	public required string Crafter { get; set; }
	public string? Note { get; set; }
	public bool Withdrawn { get; set; }
	public DateTime CreatedUtc { get; set; }
	public DateTime UpdatedUtc { get; set; }
}

/// <summary>
/// Catalogue des recettes de la guilde : toutes celles que les fenêtres de
/// métier des membres ont montrées, apprises ou non. Sert à demander un
/// objet que personne ne sait encore faire.
/// </summary>
public sealed class CatalogRecipe : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public int SpellId { get; set; }
	public required string Name { get; set; }
	public required string Profession { get; set; }
	public DateTime UpdatedUtc { get; set; }
}
