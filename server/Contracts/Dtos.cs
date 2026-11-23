namespace GuildProfessions.Server.Contracts;

// ---- Export (GET /api/v1/export) : consommé par l'app compagnon, qui le
// ---- traduit en Data.lua. Toute évolution ici impacte companion + addon.
// ---- Une section null est celle d'un module en panne (export partiel, en-tête
// ---- X-Guildkeep-Failed-Modules) : « pas de données » pour ce module seulement.

public sealed record ExportDto(
	long Version,
	DateTime GeneratedUtc,
	Dictionary<int, string>? RecipeNames,
	List<ExportCharacterDto>? Characters,
	List<ExportOrderDto>? Orders,
	string? GuildName = null,
	List<ExportEventDto>? Events = null,
	List<ExportRaidDto>? Raids = null,
	List<ExportCatalogRecipeDto>? Catalog = null);

public sealed record ExportOrderDto(
	int Id,
	string? ClientId,
	string Requester,
	string? Crafter,
	string Item,
	int Quantity,
	string? Note,
	string Status,
	string CreatedUtc,
	int? RecipeId = null,
	string? ExpiresUtc = null,
	List<ExportOfferDto>? Offers = null);

public sealed record ExportOfferDto(string Crafter, string? Note, string CreatedUtc);

public sealed record ExportCatalogRecipeDto(int SpellId, string Name, string Profession);

public sealed record ExportEventDto(
	int Id,
	string? ClientId,
	string Title,
	string Kind,
	long StartsAt,
	string? Description,
	string Organizer,
	int Tank,
	int Healer,
	int Melee,
	int Ranged,
	bool Cancelled,
	long UpdatedAt,
	List<ExportSignupDto> Signups,
	bool Locked = false,
	List<ExportReserveDto>? Reserves = null,
	List<ExportHardReserveDto>? HardReserves = null,
	List<ExportDutyDto>? Duties = null);

public sealed record ExportReserveDto(string Character, string Priority, int ItemId, string? ItemName, string? SetBy, long At);

public sealed record ExportHardReserveDto(int ItemId, string? ItemName, string? Holder, bool Active, long At);

public sealed record ExportDutyDto(string Duty, string? Character, long At);


public sealed record ExportSignupDto(
	string Character,
	string? ClassFile,
	string Role,
	string? Spec,
	string Response,
	string? Note,
	long ResponseAt,
	string Placement,
	long PlacementAt);

public sealed record ExportCharacterDto(
	string Name,
	string? ClassFile,
	string? RaceFile,
	int? RaceId,
	int? Gender,
	int? Level,
	List<ExportProfessionDto> Professions,
	Dictionary<int, string>? Equipment = null,
	List<UploadQuest>? Quests = null);

public sealed record ExportProfessionDto(
	string Name,
	int Level,
	int Max,
	string? Note,
	string Source,
	string? ScannedAt,
	List<int> Recipes);

// ---- Upload (POST /api/v1/upload) : payload produit par le compagnon à
// ---- partir des SavedVariables de l'addon.

public sealed record UploadPayload(
	List<UploadCharacter> Characters,
	List<UploadOrder>? Orders = null,
	List<UploadOrderAction>? OrderActions = null,
	// Personnages du joueur désormais relevés dans une autre guilde : à retirer de celle-ci.
	List<string>? DepartedCharacters = null,
	List<UploadEvent>? Events = null,
	List<UploadSignup>? Signups = null,
	List<UploadPlacement>? Placements = null,
	List<UploadReserve>? Reserves = null,
	List<UploadHardReserve>? HardReserves = null,
	List<UploadDuty>? Duties = null,
	List<UploadRaid>? Raids = null,
	List<UploadRaidLoot>? Loot = null,
	List<UploadAttendanceCorrection>? AttendanceCorrections = null,
	List<UploadLootCorrection>? LootCorrections = null,
	List<UploadOffer>? Offers = null);

/// <summary>Commande de craft créée en jeu (id client = clé de déduplication).</summary>
public sealed record UploadOrder(
	string ClientId,
	string Requester,
	string? Crafter,
	string Item,
	int Quantity,
	string? Note,
	string? CreatedAt,
	int? RecipeId = null);

/// <summary>Proposition d'un artisan (ou son retrait) sur une demande serveur ou encore locale.</summary>
public sealed record UploadOffer(
	int? OrderId,
	string? OrderClientId,
	string Crafter,
	string? Note,
	bool Withdrawn);

/// <summary>Changement de statut décidé en jeu, ciblant une commande serveur ou locale.</summary>
/// <summary>Création ou modification d'un événement en jeu (la plus récente, d'après At, gagne).</summary>
public sealed record UploadEvent(
	string? ClientId,
	int? ServerId,
	string Title,
	string Kind,
	long StartsAt,
	string? Description,
	string By,
	int Tank,
	int Healer,
	int Melee,
	int Ranged,
	bool Cancelled,
	long At,
	bool Locked = false);

/// <summary>Réponse d'un joueur à un événement (serveur ou encore local).</summary>
public sealed record UploadSignup(
	int? EventId,
	string? EventClientId,
	string Character,
	string? ClassFile,
	string Role,
	string? Spec,
	string Response,
	string? Note,
	long At);

/// <summary>Réservation de butin : ItemId 0 la retire. By = le joueur, ou un officier pour lui.</summary>
public sealed record UploadReserve(
	int? EventId,
	string? EventClientId,
	string Character,
	string Priority,
	int ItemId,
	string? ItemName,
	string By,
	long At);

/// <summary>Réservation ferme posée (Active) ou levée par l'organisateur ou un officier.</summary>
public sealed record UploadHardReserve(
	int? EventId,
	string? EventClientId,
	int ItemId,
	string? ItemName,
	string? Holder,
	bool Active,
	string By,
	long At);

/// <summary>Rôle de soutien attribué (Character) ou libéré (null).</summary>
public sealed record UploadDuty(
	int? EventId,
	string? EventClientId,
	string Duty,
	string? Character,
	string By,
	long At);

/// <summary>Place d'un inscrit dans le roster, décidée par l'organisateur ou un officier.</summary>
public sealed record UploadPlacement(
	int? EventId,
	string? EventClientId,
	string Character,
	string Placement,
	string By,
	long At);

public sealed record UploadOrderAction(
	int? ServerId,
	string? ClientId,
	string Status,
	// Pour « accepted » décidé par le demandeur : l'artisan dont il retient l'offre.
	string? Crafter = null);

public sealed record UploadCharacter(
	string Name,
	string? ClassFile,
	string? RaceFile,
	int? RaceId,
	int? Gender,
	int? Level,
	List<UploadProfession> Professions,
	string? Guild = null,
	Dictionary<int, string>? Equipment = null,
	// Rang d'officier relevé en jeu : autorise à gérer les événements des autres.
	// null (champ absent : ancien addon ou ancien import) = rang inconnu, on garde celui en base.
	bool? Officer = null,
	// Quêtes partageables du journal (élite, donjon, raid) ; null = pas relevées, on garde les précédentes.
	List<UploadQuest>? Quests = null);

/// <summary>Quête du journal : kind = elite | dungeon | raid ; Location = zone ou donjon (en-tête du journal).</summary>
public sealed record UploadQuest(int Id, string Title, string Kind, string? Location, int? Level, bool Complete);

public sealed record UploadProfession(
	string Name,
	int Level,
	int Max,
	string? ScannedAt,
	List<UploadRecipe>? Recipes,
	// Toutes les recettes montrées par la fenêtre du métier, apprises ou non.
	List<UploadRecipe>? Catalog = null);

public sealed record UploadRecipe(int Id, string? Name);

public sealed record UploadResult(bool Applied, long Version, List<string> Warnings);

// ---- Présence et butin (lot 3) ----

public sealed record ExportRaidDto(
	int Id,
	string Key,
	string Instance,
	string Day,
	string? Leader,
	long StartedAt,
	long EndedAt,
	int? EventId,
	List<ExportRaidParticipantDto> Participants,
	List<ExportRaidLootDto> Loot,
	List<ExportAttendanceCorrectionDto> Corrections);

public sealed record ExportRaidParticipantDto(string Character, string? ClassFile, int Minutes);

public sealed record ExportRaidLootDto(
	int Id, string Character, int ItemId, string? ItemName, int Quality, int Ordinal, long At,
	bool Voided, string? VoidNote, string? VoidBy, long VoidAt);

public sealed record ExportAttendanceCorrectionDto(string Character, string Status, string? Note, string? By, long At);

// ---- Présence et butin (lot 3) ----

/// <summary>Raid relevé par un poste : participants de la guilde et leurs minutes de présence.</summary>
public sealed record UploadRaid(
	string Key,
	string Instance,
	string Day,
	string? Leader,
	long StartedAt,
	long EndedAt,
	string Observer,
	Dictionary<string, int> Participants,
	Dictionary<string, string>? Classes = null);

/// <summary>Objet épique ou mieux vu reçu pendant ce raid.</summary>
public sealed record UploadRaidLoot(
	string Raid,
	string Character,
	int ItemId,
	string? ItemName,
	int Quality,
	int Ordinal,
	long At,
	string Observer);

/// <summary>Correction de présence par un officier (present, absent, excused).</summary>
public sealed record UploadAttendanceCorrection(
	string Raid,
	string Character,
	string Status,
	string? Note,
	string By,
	long At);

/// <summary>Annulation (Void) ou rétablissement d'un objet relevé, par un officier.</summary>
public sealed record UploadLootCorrection(
	string Raid,
	string Character,
	int ItemId,
	int Ordinal,
	bool Void,
	string? Note,
	string By,
	long At);
