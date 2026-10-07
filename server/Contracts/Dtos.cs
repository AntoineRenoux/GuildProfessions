namespace GuildProfessions.Server.Contracts;

// ---- Export (GET /api/v1/export) : consommé par l'app compagnon, qui le
// ---- traduit en Data.lua. Toute évolution ici impacte companion + addon.

public sealed record ExportDto(
	long Version,
	DateTime GeneratedUtc,
	Dictionary<int, string> RecipeNames,
	List<ExportCharacterDto> Characters);

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

public sealed record UploadPayload(List<UploadCharacter> Characters);

public sealed record UploadCharacter(
	string Name,
	string? ClassFile,
	string? RaceFile,
	int? RaceId,
	int? Gender,
	int? Level,
	List<UploadProfession> Professions);

public sealed record UploadProfession(
	string Name,
	int Level,
	int Max,
	string? ScannedAt,
	List<UploadRecipe>? Recipes);

public sealed record UploadRecipe(int Id, string? Name);

public sealed record UploadResult(bool Applied, long Version, List<string> Warnings);
