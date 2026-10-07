namespace GuildProfessions.Server.Discord;

/// <summary>
/// Les libellés doivent correspondre exactement aux noms de métiers du client
/// de jeu en français : la fusion addon/discord se fait sur ce nom.
/// </summary>
public static class ProfessionChoices
{
	public static readonly string[] All =
	[
		"Alchimie",
		"Forge",
		"Enchantement",
		"Ingénierie",
		"Herboristerie",
		"Travail du cuir",
		"Minage",
		"Dépeçage",
		"Couture",
		"Cuisine",
		"Secourisme",
		"Pêche",
	];

	public static bool IsValid(string name) => All.Contains(name);
}
