namespace GuildProfessions.Server.Data;

// Entités du module Annuaire. Le namespace reste GuildProfessions.Server.Data :
// l'historique des migrations EF (AppDbContextModelSnapshot) nomme chaque
// entité par son nom CLR complet, le changer imposerait une migration à vide.

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

	/// <summary>
	/// Rang d'officier relevé en jeu, qui donne sur Discord les mêmes droits qu'en
	/// jeu. Posé seulement par un envoi au token personnel (voir RosterService).
	/// </summary>
	public bool IsOfficer { get; set; }

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
