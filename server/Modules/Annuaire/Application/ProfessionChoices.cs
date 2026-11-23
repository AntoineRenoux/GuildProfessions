using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Annuaire.Application;

/// <summary>
/// Noms de métiers du client de jeu, en français et en anglais : la fusion
/// addon/Discord se fait sur ce nom, qui doit donc être exactement celui du
/// jeu. Les noms déjà présents dans l'annuaire de la guilde sont acceptés
/// aussi (client dans une autre langue).
/// </summary>
public static class ProfessionChoices
{
	public static readonly (string French, string English)[] All =
	[
		("Alchimie", "Alchemy"),
		("Forge", "Blacksmithing"),
		("Enchantement", "Enchanting"),
		("Ingénierie", "Engineering"),
		("Herboristerie", "Herbalism"),
		("Travail du cuir", "Leatherworking"),
		("Minage", "Mining"),
		("Dépeçage", "Skinning"),
		("Couture", "Tailoring"),
		("Cuisine", "Cooking"),
		("Secourisme", "First Aid"),
		("Pêche", "Fishing"),
	];

	public static IEnumerable<string> For(BotLanguage language) =>
		All.Select(p => language == BotLanguage.French ? p.French : p.English);

	public static bool IsGameName(string name) => All.Any(p => p.French == name || p.English == name);
}
