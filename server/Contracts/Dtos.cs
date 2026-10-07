namespace GuildProfessions.Server.Contracts;

// ---- Export (GET /api/v1/export) : consommé par l'app compagnon, qui le
// ---- traduit en Data.lua. Toute évolution ici impacte companion + addon.

public sealed record ExportDto(
	long Version,
	DateTime GeneratedUtc,
	Dictionary<int, string> RecipeNames,
	List<ExportCharacterDto> Characters,
	List<ExportOrderDto> Orders,
	string? GuildName = null);

public sealed record ExportOrderDto(
	int Id,
	string? ClientId,
	string Requester,
	string Crafter,
	string Item,
	int Quantity,
	string? Note,
	string Status,
	string CreatedUtc);

public sealed record ExportCharacterDto(
	string Name,
	string? ClassFile,
	string? RaceFile,
	int? RaceId,
	int? Gender,
	int? Level,
	List<ExportProfessionDto> Professions);

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
	List<UploadOrderAction>? OrderActions = null);

/// <summary>Commande de craft créée en jeu (id client = clé de déduplication).</summary>
public sealed record UploadOrder(
	string ClientId,
	string Requester,
	string Crafter,
	string Item,
	int Quantity,
	string? Note,
	string? CreatedAt);

/// <summary>Changement de statut décidé en jeu, ciblant une commande serveur ou locale.</summary>
public sealed record UploadOrderAction(
	int? ServerId,
	string? ClientId,
	string Status);

public sealed record UploadCharacter(
	string Name,
	string? ClassFile,
	string? RaceFile,
	int? RaceId,
	int? Gender,
	int? Level,
	List<UploadProfession> Professions,
	string? Guild = null);

public sealed record UploadProfession(
	string Name,
	int Level,
	int Max,
	string? ScannedAt,
	List<UploadRecipe>? Recipes);

public sealed record UploadRecipe(int Id, string? Name);

public sealed record UploadResult(bool Applied, long Version, List<string> Warnings);
