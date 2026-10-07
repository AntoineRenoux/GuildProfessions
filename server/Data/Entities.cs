namespace GuildProfessions.Server.Data;

public enum DataSource
{
	/// <summary>Déclaré à la main via le bot Discord.</summary>
	Discord = 0,

	/// <summary>Scanné en jeu par l'addon (prioritaire : donnée exacte).</summary>
	Addon = 1,
}

public sealed class Member
{
	public int Id { get; set; }
	public required string DiscordId { get; set; }
	public required string DiscordName { get; set; }
	public string? UploadToken { get; set; }

	public List<Character> Characters { get; set; } = [];
}

public sealed class Character
{
	public int Id { get; set; }
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

/// <summary>Petit magasin clé/valeur : porte le compteur de version de l'export.</summary>
public sealed class Meta
{
	public required string Key { get; set; }
	public required string Value { get; set; }
}
