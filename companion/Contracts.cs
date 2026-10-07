namespace GuildProfessions.Companion;

// Miroir des contrats du serveur (server/Contracts/Dtos.cs). Le compagnon
// reste autonome (publication single-file) : toute évolution se fait des
// deux côtés.

public sealed record ExportDto(
	long Version,
	DateTime GeneratedUtc,
	Dictionary<int, string> RecipeNames,
	List<ExportCharacterDto> Characters,
	List<ExportOrderDto>? Orders,
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
	List<ExportProfessionDto> Professions,
	Dictionary<int, string>? Equipment = null);

public sealed record ExportProfessionDto(
	string Name,
	int Level,
	int Max,
	string? Note,
	string Source,
	string? ScannedAt,
	List<int> Recipes);

public sealed record UploadPayload(
	List<UploadCharacter> Characters,
	List<UploadOrder>? Orders = null,
	List<UploadOrderAction>? OrderActions = null);

public sealed record UploadOrder(
	string ClientId,
	string Requester,
	string Crafter,
	string Item,
	int Quantity,
	string? Note,
	string? CreatedAt);

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
	string? Guild = null,
	Dictionary<int, string>? Equipment = null);

public sealed record UploadProfession(
	string Name,
	int Level,
	int Max,
	string? ScannedAt,
	List<UploadRecipe>? Recipes);

public sealed record UploadRecipe(int Id, string? Name);
