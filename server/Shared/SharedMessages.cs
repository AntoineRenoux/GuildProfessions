namespace GuildProfessions.Server.Shared;

/// <summary>
/// Messages communs à plusieurs modules, écrits une seule fois : une règle de
/// formulation changée ici l'est partout.
/// </summary>
public static class SharedMessages
{
	/// <summary>Le membre n'a lié aucun personnage à son compte Discord.</summary>
	public static string NoLinkedCharacter(BotLocalizer l) => l.T(
		"Aucun personnage lié à ton compte : utilise d'abord `/gkeep lier`.",
		"No character linked to your account: use `/gkeep link` first.");

	/// <summary>Le personnage nommé n'est pas un de ceux du membre.</summary>
	public static string NotYourCharacter(BotLocalizer l, string character) => l.T(
		$"**{character}** n'est pas un de tes personnages liés.",
		$"**{character}** is not one of your linked characters.");
}
