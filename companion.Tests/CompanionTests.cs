using GuildProfessions.Companion;
using GuildProfessions.Companion.Lua;
using Xunit;

namespace GuildProfessions.Companion.Tests;

public sealed class LuaParserTests
{
	// Forme exacte émise par le client WoW à la déconnexion.
	private const string SampleSavedVariables = """
		GuildProfessionsDB = {
			["characters"] = {
				["Elaria"] = {
					["classFile"] = "MAGE",
					["level"] = 60,
					["gender"] = 3,
					["professions"] = {
						["Couture"] = {
							["level"] = 300,
							["max"] = 300,
							["scannedAt"] = "2026-10-07 21:12",
							["recipes"] = {
								14046, -- commentaire inline
								18560,
							},
						},
						["Enchantement"] = {
							["level"] = 285,
							["max"] = 300,
						},
					},
				},
			},
			["recipeNames"] = {
				[14046] = "Sac en étoffe runique",
				[18560] = "Étoffe \"lunaire\"",
			},
		}
		""";

	[Fact]
	public void ParseSavedVariable_Should_ReadNestedTablesKeysAndEscapes()
	{
		var db = LuaParser.ParseSavedVariable(SampleSavedVariables, "GuildProfessionsDB");

		var elaria = db.GetTable("characters")!.GetTable("Elaria")!;
		Assert.Equal("MAGE", elaria.GetString("classFile"));
		Assert.Equal(60, elaria.GetInt("level"));
		var couture = elaria.GetTable("professions")!.GetTable("Couture")!;
		Assert.Equal(300, couture.GetInt("level"));
		Assert.Equal([14046, 18560], couture.GetTable("recipes")!.Items.Select(item => item.AsInt()!.Value));
		Assert.Equal("Étoffe \"lunaire\"", db.GetTable("recipeNames")!.GetString("18560"));
	}

	[Fact]
	public void BuildPayload_Should_ProduceUploadWithRecipeNames()
	{
		var payload = SavedVariablesReader.BuildPayload(SampleSavedVariables);

		Assert.NotNull(payload);
		var character = Assert.Single(payload.Characters);
		Assert.Equal("Elaria", character.Name);
		Assert.Equal(2, character.Professions.Count);
		var couture = character.Professions.Single(profession => profession.Name == "Couture");
		Assert.Equal("2026-10-07 21:12", couture.ScannedAt);
		Assert.Equal("Sac en étoffe runique", couture.Recipes!.Single(recipe => recipe.Id == 14046).Name);
		var enchantement = character.Professions.Single(profession => profession.Name == "Enchantement");
		Assert.Null(enchantement.Recipes);
	}

	[Fact]
	public void BuildPayload_Should_ReturnNull_WhenNoCharacters()
	{
		Assert.Null(SavedVariablesReader.BuildPayload("GuildProfessionsDB = { [\"characters\"] = {} }"));
	}
}

public sealed class DataLuaWriterTests
{
	[Fact]
	public void Build_Should_MatchExpectedLuaShape()
	{
		var export = new ExportDto(
			42,
			new DateTime(2026, 10, 7, 18, 0, 0, DateTimeKind.Utc),
			new Dictionary<int, string> { [17635] = "Flacon des \"Titans\"" },
			[
				new ExportCharacterDto("Grimbart", "PALADIN", null, null, null, 60,
				[
					new ExportProfessionDto("Alchimie", 295, 300, "Transmute dispo", "addon", "2026-10-07 12:00", [17635]),
				]),
			]);

		var lua = DataLuaWriter.Build(export);

		var expected = """
			-- Fichier généré par l'app compagnon GuildProfessions.
			-- Ne pas éditer : écrasé à chaque synchronisation.
			GuildProfessions_ServerData = {
				version = 42,
				generatedUtc = "2026-10-07 18:00 UTC",
				recipeNames = {
					[17635] = "Flacon des \"Titans\"",
				},
				characters = {
					{
						name = "Grimbart",
						classFile = "PALADIN",
						level = 60,
						professions = {
							{ name = "Alchimie", level = 295, max = 300, source = "addon", note = "Transmute dispo", scannedAt = "2026-10-07 12:00", recipes = { 17635 } },
						},
					},
				},
			}

			""".ReplaceLineEndings(Environment.NewLine);

		Assert.Equal(expected, lua);
	}
}
