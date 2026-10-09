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
					["equipment"] = {
						["5"] = "|cffa335ee|Hitem:14152|h[Robe de l'Archimage]|h|r",
					},
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
			["orders"] = {
				["Elaria-173-xk"] = {
					["requester"] = "Elaria",
					["crafter"] = "Thorgal",
					["item"] = "Heaume Cœur de lion",
					["qty"] = 1,
					["note"] = "mats fournis",
					["createdAt"] = "2026-10-07 21:30",
				},
			},
			["orderActions"] = {
				["17"] = {
					["status"] = "accepted",
				},
				["Elaria-170-zz"] = {
					["status"] = "cancelled",
				},
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
		Assert.Equal("|cffa335ee|Hitem:14152|h[Robe de l'Archimage]|h|r", character.Equipment![5]);
	}

	[Fact]
	public void BuildPayload_Should_ReadOrdersAndActions()
	{
		var payload = SavedVariablesReader.BuildPayload(SampleSavedVariables)!;

		var order = Assert.Single(payload.Orders!);
		Assert.Equal("Elaria-173-xk", order.ClientId);
		Assert.Equal("Thorgal", order.Crafter);
		Assert.Equal("mats fournis", order.Note);

		Assert.Equal(2, payload.OrderActions!.Count);
		var byServerId = payload.OrderActions.Single(action => action.ServerId is not null);
		Assert.Equal(17, byServerId.ServerId);
		Assert.Equal("accepted", byServerId.Status);
		var byClientId = payload.OrderActions.Single(action => action.ClientId is not null);
		Assert.Equal("Elaria-170-zz", byClientId.ClientId);
	}

	[Fact]
	public void BuildPayload_Should_ReturnNull_WhenNoCharacters()
	{
		Assert.Null(SavedVariablesReader.BuildPayload("GuildProfessionsDB = { [\"characters\"] = {} }"));
	}
}

public sealed class AddonInstallerTests : IDisposable
{
	private readonly string _addOns = Path.Combine(Path.GetTempPath(), "gp-addons-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_addOns))
		{
			Directory.Delete(_addOns, recursive: true);
		}
	}

	private static byte[] Zip(params (string Path, string Content)[] files)
	{
		using var memory = new MemoryStream();
		using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach (var (path, content) in files)
			{
				using var writer = new StreamWriter(zip.CreateEntry(path).Open());
				writer.Write(content);
			}
		}
		return memory.ToArray();
	}

	[Fact]
	public void InstallFromZip_Should_InstallFiles_AndNeverTouchDataLua()
	{
		var addon = Path.Combine(_addOns, "GuildProfessions");
		Directory.CreateDirectory(addon);
		File.WriteAllText(Path.Combine(addon, "Data.lua"), "-- données réelles de la guilde");

		var count = AddonInstaller.InstallFromZip(Zip(
			("GuildProfessions/GuildProfessions.toc", "## Interface: 16001\n## Version: 1.0.31\n"),
			("GuildProfessions/Core.lua", "-- code"),
			("GuildProfessions/Data.lua", "-- version vide du zip")), _addOns);

		Assert.Equal(2, count);
		Assert.Equal("-- données réelles de la guilde", File.ReadAllText(Path.Combine(addon, "Data.lua")));
		Assert.Equal("1.0.31", AddonInstaller.ReadInstalledVersion(_addOns));
	}

	[Fact]
	public void InstallFromZip_Should_RemoveFilesNoLongerShipped()
	{
		var addon = Path.Combine(_addOns, "GuildProfessions");
		Directory.CreateDirectory(addon);
		File.WriteAllText(Path.Combine(addon, "Ancien.lua"), "-- retiré dans la nouvelle version");

		AddonInstaller.InstallFromZip(Zip(("GuildProfessions/Core.lua", "-- code")), _addOns);

		Assert.False(File.Exists(Path.Combine(addon, "Ancien.lua")));
		Assert.True(File.Exists(Path.Combine(addon, "Core.lua")));
	}

	[Fact]
	public void InstallFromZip_Should_IgnoreEntriesEscapingTheAddonFolder()
	{
		AddonInstaller.InstallFromZip(Zip(
			("GuildProfessions/../../evil.txt", "zip-slip"),
			("AutreAddon/Core.lua", "-- pas à nous")), _addOns);

		Assert.False(File.Exists(Path.Combine(_addOns, "..", "evil.txt")));
		Assert.False(Directory.Exists(Path.Combine(_addOns, "AutreAddon")));
	}

	[Fact]
	public void ReadInstalledVersion_Should_ReturnNull_WhenAddonAbsent()
	{
		Assert.Null(AddonInstaller.ReadInstalledVersion(_addOns));
	}
}

public sealed class SelfUpdaterTests
{
	[Theory]
	[InlineData("1.0.28", "1.0.27", true)]
	[InlineData("1.0.28", "1.0.28", false)]
	[InlineData("1.0.28", "1.0.28.0", false)] // version d'assembly à 4 composants : égale
	[InlineData("1.0.9", "1.0.28", false)]     // pas de rétrogradation (comparaison numérique, pas texte)
	[InlineData("1.0.100", "1.0.99", true)]
	[InlineData("pas-une-version", "1.0.0", false)]
	public void IsNewer_Should_CompareNumerically(string candidate, string current, bool expected)
	{
		Assert.Equal(expected, SelfUpdater.IsNewer(candidate, Version.Parse(current)));
	}

	[Fact]
	public void CurrentRid_Should_MatchAPublishedBuild()
	{
		Assert.Contains(SelfUpdater.CurrentRid(), new[] { "win-x64", "linux-x64", "osx-x64", "osx-arm64" });
	}
}

public sealed class WowLocatorTests
{
	[Fact]
	public void ExtractWowPaths_Should_FindPathsInBinaryBlob()
	{
		// Simule un product.db : chemins ASCII noyés dans du binaire.
		var blob = new List<byte> { 0x0A, 0x2E, 0x01 };
		blob.AddRange("E:/Jeux/World of Warcraft"u8.ToArray());
		blob.Add(0x00);
		blob.AddRange("C:/ProgramData/Battle.net"u8.ToArray()); // sans WoW : ignoré
		blob.Add(0x12);
		blob.AddRange("prefs.json"u8.ToArray()); // pas un chemin : ignoré
		blob.Add(0xFF);

		var paths = WowLocator.ExtractWowPaths([.. blob]);

		var path = Assert.Single(paths);
		Assert.Contains("World of Warcraft", path);
		Assert.StartsWith("E:", path);
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
			],
			[
				new ExportOrderDto(7, null, "Nayra", "Grimbart", "Flacon des Titans", 2, "urgent", "open", "2026-10-07 10:00"),
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
				orders = {
					{ id = 7, requester = "Nayra", crafter = "Grimbart", item = "Flacon des Titans", qty = 2, status = "open", createdUtc = "2026-10-07 10:00", note = "urgent" },
				},
			}

			""".ReplaceLineEndings(Environment.NewLine);

		Assert.Equal(expected, lua);
	}
}
