using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Evenements.Application;

/// <summary>
/// Libellés localisés des valeurs d'un événement (sorte, rôle, réponse, place,
/// priorité de réservation, poste de la soirée). Sortis d'EventService : un
/// correctif d'affichage ne touche plus la classe qui porte les écritures.
/// </summary>
public static class EventLabels
{
	public static string PlacementLabel(string placement, BotLocalizer l) => placement switch
	{
		"confirmed" => l.T("confirmé", "confirmed"),
		"standby" => l.T("remplaçant", "standby"),
		"bench" => l.T("banc", "bench"),
		_ => l.T("en attente", "pending"),
	};

	public static string KindLabel(string kind, BotLocalizer l) => kind switch
	{
		"raid" => "Raid",
		"dungeon" => l.T("Donjon", "Dungeon"),
		"pvp" => l.T("JcJ", "PvP"),
		_ => l.T("Autre", "Other"),
	};

	public static string ResponseLabel(string response, BotLocalizer l) => response switch
	{
		"yes" => l.T("présent", "attending"),
		"tentative" => l.T("peut-être", "tentative"),
		"no" => l.T("absent", "not attending"),
		_ => l.T("sans réponse", "no answer"),
	};

	public static string RoleLabel(string role, BotLocalizer l) => role switch
	{
		"tank" => "tank",
		"healer" => l.T("soigneur", "healer"),
		"melee" => l.T("DPS mêlée", "melee DPS"),
		"ranged" => l.T("DPS distance", "ranged DPS"),
		_ => role,
	};

	public static string PriorityLabel(string priority, BotLocalizer l) => priority switch
	{
		"high" => l.T("haute", "high"),
		"low" => l.T("basse", "low"),
		_ => l.T("normale", "normal"),
	};

	public static string DutyLabel(string duty, BotLocalizer l) => duty switch
	{
		"lead" => l.T("chef de raid", "raid lead"),
		"summoner" => l.T("invocateur", "summoner"),
		"consumables" => l.T("responsable consommables", "consumables lead"),
		"repair" => l.T("robot de réparation", "repair bot"),
		_ => duty,
	};
}
