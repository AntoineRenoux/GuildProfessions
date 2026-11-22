using GuildProfessions.Companion;
using GuildProfessions.Companion.Lua;
using Xunit;

namespace GuildProfessions.Companion.Tests;

public sealed class LuaParserTests
{
	// Forme exacte émise par le client WoW à la déconnexion.
	private const string SampleSavedVariables = """
		GuildkeepDB = {
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
		var db = LuaParser.ParseSavedVariable(SampleSavedVariables, "GuildkeepDB");

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
		Assert.Null(SavedVariablesReader.BuildPayload("GuildkeepDB = { [\"characters\"] = {} }"));
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
		var addon = Path.Combine(_addOns, "Guildkeep");
		Directory.CreateDirectory(addon);
		File.WriteAllText(Path.Combine(addon, "Data.lua"), "-- données réelles de la guilde");

		var count = AddonInstaller.InstallFromZip(Zip(
			("Guildkeep/Guildkeep.toc", "## Interface: 16001\n## Version: 1.0.31\n"),
			("Guildkeep/Core.lua", "-- code"),
			("Guildkeep/Data.lua", "-- version vide du zip")), _addOns);

		Assert.Equal(2, count);
		Assert.Equal("-- données réelles de la guilde", File.ReadAllText(Path.Combine(addon, "Data.lua")));
		Assert.Equal("1.0.31", AddonInstaller.ReadInstalledVersion(_addOns));
	}

	[Fact]
	public void InstallFromZip_Should_RemoveFilesNoLongerShipped()
	{
		var addon = Path.Combine(_addOns, "Guildkeep");
		Directory.CreateDirectory(addon);
		File.WriteAllText(Path.Combine(addon, "Ancien.lua"), "-- retiré dans la nouvelle version");

		AddonInstaller.InstallFromZip(Zip(("Guildkeep/Core.lua", "-- code")), _addOns);

		Assert.False(File.Exists(Path.Combine(addon, "Ancien.lua")));
		Assert.True(File.Exists(Path.Combine(addon, "Core.lua")));
	}

	[Fact]
	public void InstallFromZip_Should_IgnoreEntriesEscapingTheAddonFolder()
	{
		AddonInstaller.InstallFromZip(Zip(
			("Guildkeep/../../evil.txt", "zip-slip"),
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

public sealed class ForeverBranchTests : IDisposable
{
	private readonly string _wow = Path.Combine(Path.GetTempPath(), "gp-wow-" + Guid.NewGuid().ToString("N"));

	public ForeverBranchTests() => Directory.CreateDirectory(_wow);

	public void Dispose() => Directory.Delete(_wow, recursive: true);

	// Format réel du launcher (relevé sur la bêta, 2026-10), colonnes inutiles abrégées.
	private void BuildInfo(params (string Version, string Product)[] rows)
	{
		var lines = new List<string> { "Branch!STRING:0|Active!DEC:1|Build Key!HEX:16|Version!STRING:0|KeyRing!HEX:16|Product!STRING:0" };
		lines.AddRange(rows.Select(r => $"eu|1|abc|{r.Version}||{r.Product}"));
		File.WriteAllLines(Path.Combine(_wow, ".build.info"), lines);
	}

	private string Branch(string folder, string? product)
	{
		var dir = Path.Combine(_wow, folder);
		Directory.CreateDirectory(Path.Combine(dir, "WTF"));
		if (product is not null)
		{
			File.WriteAllLines(Path.Combine(dir, ".flavor.info"), ["Product Flavor!STRING:0", product]);
		}
		return dir;
	}

	[Fact]
	public void FindForeverBranch_Should_PickBeta_NotClassicEra_TodaysSetup()
	{
		BuildInfo(("1.15.9.70003", "wow_classic_era"), ("1.60.1.70450", "wow_classic_beta"));
		Branch("_classic_era_", "wow_classic_era");
		var beta = Branch("_classic_beta_", "wow_classic_beta");

		Assert.Equal(beta, WowLocator.FindForeverBranch(_wow));
	}

	[Fact]
	public void FindForeverBranch_Should_FindReleaseUnderUnknownFolderName()
	{
		BuildInfo(("1.15.9.70003", "wow_classic_era"), ("1.61.0.80000", "wow_classic_plus"));
		Branch("_classic_era_", "wow_classic_era");
		var release = Branch("_classic_plus_", "wow_classic_plus");

		Assert.Equal(release, WowLocator.FindForeverBranch(_wow));
	}

	[Fact]
	public void FindForeverBranch_Should_PreferReleaseOverBeta_WhenBothInstalled()
	{
		BuildInfo(("1.60.1.70450", "wow_classic_beta"), ("1.60.2.81000", "wow_forever"));
		Branch("_classic_beta_", "wow_classic_beta");
		var release = Branch("_forever_", "wow_forever");

		Assert.Equal(release, WowLocator.FindForeverBranch(_wow));
	}

	[Fact]
	public void FindForeverBranch_Should_ReturnNull_WhenOnlyClassicEraInstalled()
	{
		BuildInfo(("1.15.9.70003", "wow_classic_era"));
		Branch("_classic_era_", "wow_classic_era");
		Branch("_retail_", null);

		Assert.Null(WowLocator.FindForeverBranch(_wow));
	}

	[Fact]
	public void FindForeverBranch_Should_FallBackToForeverFolderNames_WithoutLauncherMetadata()
	{
		Branch("_classic_era_", null);
		var forever = Branch("_forever_", null);

		Assert.Equal(forever, WowLocator.FindForeverBranch(_wow));
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
	public void Build_Should_WriteOneSectionPerGuild()
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
			],
			"FORMER HOPE");
		var other = new ExportDto(3, new DateTime(2026, 10, 7, 18, 0, 0, DateTimeKind.Utc), [], [], [], "Les Autres");

		var lua = DataLuaWriter.Build([export, other]);

		var expected = """
			-- Fichier généré par l'app compagnon Guildkeep.
			-- Ne pas éditer : écrasé à chaque synchronisation.
			-- guildkeep-sync: FORMER HOPE=42;Les Autres=3
			Guildkeep_ServerData = {
				guilds = {
					{
						version = 42,
						generatedUtc = "2026-10-07 18:00 UTC",
						guildName = "FORMER HOPE",
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
					},
					{
						version = 3,
						generatedUtc = "2026-10-07 18:00 UTC",
						guildName = "Les Autres",
						recipeNames = {
						},
						characters = {
						},
						orders = {
						},
					},
				},
			}

			""".ReplaceLineEndings(Environment.NewLine);

		Assert.Equal(expected, lua);
		// Le fichier reste lisible par le parseur Lua du compagnon.
		Assert.NotNull(LuaParser.ParseSavedVariable(lua, "Guildkeep_ServerData").GetTable("guilds"));
	}
}

public sealed class MultiGuildTests : IDisposable
{
	private readonly string _directory = Path.Combine(Path.GetTempPath(), "gk-tests-" + Guid.NewGuid().ToString("N"));

	public void Dispose()
	{
		if (Directory.Exists(_directory))
		{
			Directory.Delete(_directory, recursive: true);
		}
	}

	private static GuildEntry Guild(string name) => new() { Name = name, Token = "token-" + name };

	private static UploadCharacter Character(string name, string? guild) =>
		new(name, "WARRIOR", null, null, null, 60, [], guild);

	[Fact]
	public void Split_Should_SendEachCharacter_OnlyToItsOwnGuild()
	{
		var payload = new UploadPayload(
			[Character("Aldrin", "FORMER HOPE"), Character("Mira", "les autres"), Character("Reroll", "Inconnue"), Character("Vieux", null)],
			[
				new UploadOrder("o1", "Aldrin", "Bob", "Lingot", 1, null, null),
				new UploadOrder("o2", "Mira", "Zed", "Potion", 1, null, null),
			],
			[new UploadOrderAction(12, null, "done")]);

		var uploads = UploadSplitter.Split(payload, [Guild("FORMER HOPE"), Guild("Les Autres")], out var unregistered);

		var hope = uploads.Single(u => u.Guild.Name == "FORMER HOPE").Payload;
		Assert.Equal(["Aldrin"], hope.Characters.Select(c => c.Name));
		Assert.Equal(["o1"], hope.Orders!.Select(o => o.ClientId));
		Assert.Equal(["Mira", "Reroll"], hope.DepartedCharacters!);
		Assert.Single(hope.OrderActions!);

		var autres = uploads.Single(u => u.Guild.Name == "Les Autres").Payload;
		Assert.Equal(["Mira"], autres.Characters.Select(c => c.Name));
		Assert.Equal(["o2"], autres.Orders!.Select(o => o.ClientId));
		Assert.Equal(["Aldrin", "Reroll"], autres.DepartedCharacters!);

		Assert.Equal(["Inconnue"], unregistered);
	}

	[Fact]
	public void Store_Should_AddGuilds_AndUpdateOneWhoseLinkWasRegenerated()
	{
		var store = new GuildStore(Path.Combine(_directory, "guildes.json"));

		Assert.True(store.AddOrUpdate(new GuildEntry { Name = "FORMER HOPE", Token = "a", ApiBaseUrl = "https://api" }));
		Assert.True(store.AddOrUpdate(new GuildEntry { Name = "Les Autres", Token = "b", ApiBaseUrl = "https://api" }));
		Assert.False(store.AddOrUpdate(new GuildEntry { Name = "FORMER HOPE", Token = "a", ApiBaseUrl = "https://api" }));
		Assert.True(store.AddOrUpdate(new GuildEntry { Name = "former hope", Token = "a2", ApiBaseUrl = "https://api" }));

		var guilds = store.Load();
		Assert.Equal(2, guilds.Count);
		Assert.Contains(guilds, g => g.Token == "a2" && GuildStore.SameGuild(g.Name, "FORMER HOPE"));
		Assert.DoesNotContain(guilds, g => g.Token == "a");

		store.SetName("b", "Les Autres Renommés");
		Assert.Contains(store.Load(), g => g.Name == "Les Autres Renommés");
	}

	[Fact]
	public void SingleInstance_Should_RefuseASecondCompanion_UntilTheFirstStops()
	{
		using (var first = SingleInstance.TryAcquire(_directory))
		{
			Assert.NotNull(first);
			Assert.Null(SingleInstance.TryAcquire(_directory));
		}
		using var again = SingleInstance.TryAcquire(_directory);
		Assert.NotNull(again);
	}
}

/// <summary>
/// Une mise à jour de l'addon par CurseForge remplace le dossier, Data.lua compris,
/// par la version vide du zip : le compagnon doit le réécrire même si les versions
/// du serveur n'ont pas changé.
/// </summary>
public sealed class DataLuaSignatureTests : IDisposable
{
	private readonly string _dir = Directory.CreateTempSubdirectory("gk-data-").FullName;
	private string DataPath => Path.Combine(_dir, "Data.lua");

	public void Dispose() => Directory.Delete(_dir, recursive: true);

	private static ExportDto Export(string guild, long version) =>
		new(version, new DateTime(2026, 11, 22, 21, 0, 0, DateTimeKind.Utc), [], [], [], guild);

	[Fact]
	public void Le_fichier_ecrit_par_le_compagnon_est_a_jour()
	{
		var exports = new[] { Export("Former Hope", 4), Export("Les Autres", 2) };
		File.WriteAllText(DataPath, DataLuaWriter.Build(exports));

		Assert.True(DataLuaWriter.IsCurrent(DataPath, new Dictionary<string, long> { ["Les Autres"] = 2, ["Former Hope"] = 4 }));
		Assert.False(DataLuaWriter.IsCurrent(DataPath, new Dictionary<string, long> { ["Former Hope"] = 5, ["Les Autres"] = 2 }));
	}

	[Fact]
	public void Le_Data_lua_vide_livre_avec_l_addon_n_est_jamais_a_jour()
	{
		var shipped = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "addon", "Guildkeep", "Data.lua");
		File.Copy(shipped, DataPath);

		Assert.False(DataLuaWriter.IsCurrent(DataPath, new Dictionary<string, long> { ["Former Hope"] = 4 }));
	}

	[Fact]
	public void Un_fichier_absent_n_est_pas_a_jour() =>
		Assert.False(DataLuaWriter.IsCurrent(DataPath, new Dictionary<string, long> { ["Former Hope"] = 4 }));
}
