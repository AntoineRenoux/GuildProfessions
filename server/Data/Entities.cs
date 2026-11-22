namespace GuildProfessions.Server.Data;

public enum DataSource
{
	/// <summary>Déclaré à la main via le bot Discord.</summary>
	Discord = 0,

	/// <summary>Scanné en jeu par l'addon (prioritaire : donnée exacte).</summary>
	Addon = 1,
}

/// <summary>
/// Une guilde inscrite : l'unité d'isolation de toutes les données. Créée par
/// l'assistant /setup sur Discord (ou reprise de la config pour la guilde
/// historique).
/// </summary>
public sealed class Guild
{
	public int Id { get; set; }

	/// <summary>Nom exact de la guilde en jeu : filtre des rerolls et clé de répartition côté compagnon.</summary>
	public required string Name { get; set; }

	/// <summary>Identifiant lisible et unique, pour la page d'installation (/g/{slug}).</summary>
	public required string Slug { get; set; }

	/// <summary>Serveur Discord rattaché (null tant que /setup n'a pas été fait sur Discord).</summary>
	public string? DiscordServerId { get; set; }

	/// <summary>Token de l'API embarqué dans le pack de la guilde (régénérable par l'admin).</summary>
	public required string ApiToken { get; set; }

	/// <summary>Code public du lien d'installation partagé aux joueurs (régénéré avec le token).</summary>
	public required string InviteCode { get; set; }

	/// <summary>Le bot renomme les membres avec leur personnage principal (choix de l'admin, non par défaut).</summary>
	public bool SyncNicknames { get; set; }

	/// <summary>Salon où annoncer les nouvelles commandes de craft (optionnel).</summary>
	public string? OrdersChannelId { get; set; }

	/// <summary>Compteur de version de l'export de cette guilde.</summary>
	public long DataVersion { get; set; }

	public DateTime CreatedUtc { get; set; }
	public string? CreatedByDiscordId { get; set; }

	/// <summary>Date à laquelle le bot a été retiré du serveur (suppression différée des données).</summary>
	public DateTime? BotRemovedUtc { get; set; }
}

/// <summary>Donnée appartenant à une guilde : isolée par le filtre global d'AppDbContext.</summary>
public interface IGuildOwned
{
	int GuildId { get; set; }
}

public sealed class Member : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public required string DiscordId { get; set; }
	public required string DiscordName { get; set; }
	public string? UploadToken { get; set; }

	/// <summary>Personnage dont le nom devient le pseudo Discord du membre (posé au premier /link, changé par /main).</summary>
	public int? MainCharacterId { get; set; }
	public Character? MainCharacter { get; set; }

	public List<Character> Characters { get; set; } = [];
}

public sealed class Character : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }
	public int? MemberId { get; set; }
	public Member? Member { get; set; }

	public required string Name { get; set; }
	public string? ClassFile { get; set; }
	public string? RaceFile { get; set; }
	public int? RaceId { get; set; }
	public int? Gender { get; set; }
	public int? Level { get; set; }
	public DateTime? LastSeenUtc { get; set; }
	public DataSource Source { get; set; }

	/// <summary>Équipement scanné, JSON { "slot (1-19)": "lien d'objet" } — opaque pour le serveur.</summary>
	public string? EquipmentJson { get; set; }

	public List<Profession> Professions { get; set; } = [];
}

public sealed class Profession
{
	public int Id { get; set; }
	public int CharacterId { get; set; }
	public Character Character { get; set; } = null!;

	public required string Name { get; set; }
	public int SkillLevel { get; set; }
	public int MaxSkill { get; set; }
	public string? Note { get; set; }
	public DateTime UpdatedUtc { get; set; }
	public DataSource Source { get; set; }
	/// <summary>Horodatage du scan côté addon (heure locale du joueur, informatif).</summary>
	public string? ScannedAt { get; set; }

	public List<Recipe> Recipes { get; set; } = [];
}

public sealed class Recipe
{
	public int Id { get; set; }
	public int ProfessionId { get; set; }
	public Profession Profession { get; set; } = null!;

	public int SpellId { get; set; }
	public string? Name { get; set; }
}

public enum CraftOrderStatus
{
	Open = 0,
	Accepted = 1,
	Done = 2,
	Cancelled = 3,
}

public sealed class CraftOrder : IGuildOwned
{
	public int Id { get; set; }
	public int GuildId { get; set; }

	/// <summary>Id généré par l'addon pour une commande créée en jeu — clé de déduplication des uploads.</summary>
	public string? ClientId { get; set; }

	/// <summary>Noms en clair (pas de FK) : une commande peut viser un personnage pas encore connu du roster.</summary>
	public required string RequesterCharacter { get; set; }
	public required string CrafterCharacter { get; set; }

	public required string Item { get; set; }
	public int Quantity { get; set; } = 1;
	public string? Note { get; set; }
	public CraftOrderStatus Status { get; set; }
	public DateTime CreatedUtc { get; set; }
	public DateTime UpdatedUtc { get; set; }
	public DataSource Source { get; set; }
}
