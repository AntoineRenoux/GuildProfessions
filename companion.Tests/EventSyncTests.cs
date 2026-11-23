using GuildProfessions.Companion;
using GuildProfessions.Companion.Lua;
using Xunit;

namespace GuildProfessions.Companion.Tests;

public sealed class EventSyncTests
{
	// Forme émise par le client WoW : événement créé en jeu (clé client),
	// réponse à un événement serveur (#12), placement par un officier.
	private const string Saved = """
		GuildkeepDB = {
			["characters"] = {
				["Thorgal Marteau"] = {
					["classFile"] = "WARRIOR",
					["guild"] = "FORMER HOPE",
					["officer"] = true,
				},
			},
			["eventEdits"] = {
				["Thorgal Marteau-1795000000-1234"] = {
					["title"] = "Cœur du Magma",
					["kind"] = "raid",
					["startsAt"] = 1795100000,
					["by"] = "Thorgal Marteau",
					["tank"] = 2,
					["healer"] = 6,
					["cancelled"] = false,
					["at"] = 1795000000,
				},
			},
			["eventSignups"] = {
				["12|Thorgal Marteau"] = {
					["event"] = "12",
					["character"] = "Thorgal Marteau",
					["role"] = "tank",
					["spec"] = "Protection",
					["response"] = "yes",
					["at"] = 1795000100,
				},
			},
			["eventReserves"] = {
				["12|Thorgal Marteau|high"] = {
					["event"] = "12",
					["character"] = "Thorgal Marteau",
					["priority"] = "high",
					["itemId"] = 19019,
					["itemName"] = "Serre-tonnerre",
					["by"] = "Thorgal Marteau",
					["at"] = 1795000300,
				},
			},
			["eventHardReserves"] = {
				["12|17076"] = { ["event"] = "12", ["itemId"] = 17076, ["active"] = true, ["by"] = "Thorgal Marteau", ["at"] = 1795000400 },
			},
			["eventDuties"] = {
				["12|summoner"] = { ["event"] = "12", ["duty"] = "summoner", ["character"] = "Nayra Solen", ["by"] = "Thorgal Marteau", ["at"] = 1795000500 },
			},
			["eventPlacements"] = {
				["12|Nayra Solen"] = {
					["event"] = "12",
					["character"] = "Nayra Solen",
					["placement"] = "confirmed",
					["by"] = "Thorgal Marteau",
					["at"] = 1795000200,
				},
			},
		}
		""";

	[Fact]
	public void BuildPayload_Should_ReadEventsSignupsAndPlacements()
	{
		var payload = SavedVariablesReader.BuildPayload(Saved)!;

		Assert.True(Assert.Single(payload.Characters).Officer);
		var created = Assert.Single(payload.Events!);
		Assert.Equal(("Thorgal Marteau-1795000000-1234", (int?)null, 1795100000L, 2, 6), (created.ClientId, created.ServerId, created.StartsAt, created.Tank, created.Healer));
		var signup = Assert.Single(payload.Signups!);
		Assert.Equal((12, "tank", "yes"), (signup.EventId!.Value, signup.Role, signup.Response));
		var placement = Assert.Single(payload.Placements!);
		Assert.Equal(("Nayra Solen", "confirmed", "Thorgal Marteau"), (placement.Character, placement.Placement, placement.By));
		Assert.Equal((12, "high", 19019), (Assert.Single(payload.Reserves!).EventId!.Value, payload.Reserves![0].Priority, payload.Reserves[0].ItemId));
		Assert.True(Assert.Single(payload.HardReserves!).Active);
		Assert.Equal("Nayra Solen", Assert.Single(payload.Duties!).Character);
	}

	[Fact]
	public void Split_Should_SendEventChanges_ToTheActingCharactersGuild()
	{
		var payload = SavedVariablesReader.BuildPayload(Saved)! with { };
		var guilds = new List<GuildEntry> { new() { Name = "FORMER HOPE", Token = "a" }, new() { Name = "Les Autres", Token = "b" } };

		var uploads = UploadSplitter.Split(payload, guilds, out _);

		var hope = uploads.Single(u => u.Guild.Name == "FORMER HOPE");
		Assert.Single(hope.Payload.Events!);
		Assert.Single(hope.Payload.Signups!);
		Assert.Single(hope.Payload.Placements!);
		Assert.Single(hope.Payload.Reserves!);
		Assert.Single(hope.Payload.Duties!);
		// L'autre guilde n'apprend que le départ du personnage, rien de ses événements.
		var autres = uploads.Single(u => u.Guild.Name == "Les Autres").Payload;
		Assert.Null(autres.Events);
		Assert.Null(autres.Signups);
		Assert.Null(autres.Placements);
	}

	[Fact]
	public void DataLua_Should_CarryEvents_InAParsableForm()
	{
		var export = new ExportDto(5, new DateTime(2026, 11, 23, 8, 0, 0, DateTimeKind.Utc), [], [], [], "FORMER HOPE",
		[
			new ExportEventDto(12, "c-1", "Cœur du \"Magma\"", "raid", 1795100000, "Flacons", "Thorgal Marteau", 2, 6, 8, 8, false, 1795000000,
			[
				new ExportSignupDto("Nayra Solen", "PRIEST", "healer", "Sacré", "yes", null, 1795000100, "confirmed", 1795000200),
			], true,
			[new ExportReserveDto("Nayra Solen", "high", 19019, "Serre-tonnerre", null, 1795000300)],
			[new ExportHardReserveDto(17076, null, "Thorgal Marteau", true, 1795000400)],
			[new ExportDutyDto("summoner", "Nayra Solen", 1795000500)]),
		]);

		var lua = DataLuaWriter.Build([export]);

		var section = LuaParser.ParseSavedVariable(lua, "Guildkeep_ServerData").GetTable("guilds")!.Items[0].AsTable()!;
		var e = section.GetTable("events")!.Items[0].AsTable()!;
		Assert.Equal("Cœur du \"Magma\"", e.GetString("title"));
		Assert.Equal(1795100000L, e.GetLong("startsAt"));
		Assert.False(e.GetBool("cancelled"));
		var signup = e.GetTable("signups")!.Items[0].AsTable()!;
		Assert.Equal(("Nayra Solen", "confirmed", "Sacré"), (signup.GetString("character"), signup.GetString("placement"), signup.GetString("spec")));
		Assert.True(e.GetBool("locked"));
		Assert.Equal(19019, e.GetTable("reserves")!.Items[0].AsTable()!.GetInt("itemId"));
		Assert.Equal("Thorgal Marteau", e.GetTable("hardReserves")!.Items[0].AsTable()!.GetString("holder"));
		Assert.Equal("summoner", e.GetTable("duties")!.Items[0].AsTable()!.GetString("duty"));
	}
}

public sealed class AttendanceSyncTests
{
	private const string Saved = """
		GuildkeepDB = {
			["characters"] = { ["Thorgal Marteau"] = { ["guild"] = "FORMER HOPE" } },
			["raidSessions"] = {
				["2026-11-22|Cœur du Magma|Thorgal Marteau"] = {
					["key"] = "2026-11-22|Cœur du Magma|Thorgal Marteau",
					["instance"] = "Cœur du Magma",
					["day"] = "2026-11-22",
					["leader"] = "Thorgal Marteau",
					["startedAt"] = 1795000000,
					["endedAt"] = 1795010000,
					["observer"] = "Thorgal Marteau",
					["participants"] = { ["Thorgal Marteau"] = 160, ["Nayra Solen"] = 150 },
					["classes"] = { ["Nayra Solen"] = "PRIEST" },
				},
			},
			["raidLoot"] = {
				["k|Nayra Solen|19019|1"] = { ["raid"] = "2026-11-22|Cœur du Magma|Thorgal Marteau", ["character"] = "Nayra Solen", ["itemId"] = 19019, ["quality"] = 5, ["ordinal"] = 1, ["at"] = 1795005000, ["observer"] = "Thorgal Marteau" },
			},
			["attendanceCorrections"] = {
				["k|Kael Fourbe"] = { ["raid"] = "2026-11-22|Cœur du Magma|Thorgal Marteau", ["character"] = "Kael Fourbe", ["status"] = "excused", ["by"] = "Thorgal Marteau", ["at"] = 1795020000 },
			},
			["lootCorrections"] = {
				["k|Nayra Solen|19019|1"] = { ["raid"] = "2026-11-22|Cœur du Magma|Thorgal Marteau", ["character"] = "Nayra Solen", ["itemId"] = 19019, ["ordinal"] = 1, ["void"] = true, ["by"] = "Thorgal Marteau", ["at"] = 1795020000 },
			},
		}
		""";

	[Fact]
	public void BuildPayload_Should_ReadRaidsLootAndCorrections()
	{
		var payload = SavedVariablesReader.BuildPayload(Saved)!;
		var raid = Assert.Single(payload.Raids!);
		Assert.Equal((160, "PRIEST"), (raid.Participants["Thorgal Marteau"], raid.Classes!["Nayra Solen"]));
		Assert.Equal(5, Assert.Single(payload.Loot!).Quality);
		Assert.Equal("excused", Assert.Single(payload.AttendanceCorrections!).Status);
		Assert.True(Assert.Single(payload.LootCorrections!).Void);

		var uploads = UploadSplitter.Split(payload, [new GuildEntry { Name = "FORMER HOPE", Token = "a" }], out _);
		Assert.Single(Assert.Single(uploads).Payload.Raids!);
	}

	[Fact]
	public void DataLua_Should_CarryRaids()
	{
		var export = new ExportDto(1, DateTime.UtcNow, [], [], [], "FORMER HOPE", null,
		[
			new ExportRaidDto(3, "k", "Cœur du Magma", "2026-11-22", "Thorgal Marteau", 1795000000, 1795010000, 12,
				[new ExportRaidParticipantDto("Nayra Solen", "PRIEST", 150)],
				[new ExportRaidLootDto(9, "Nayra Solen", 19019, "Serre-tonnerre", 5, 1, 1795005000, true, "DE", "Thorgal Marteau", 1795020000)],
				[new ExportAttendanceCorrectionDto("Kael Fourbe", "excused", null, "Thorgal Marteau", 1795020000)]),
		]);
		var section = LuaParser.ParseSavedVariable(DataLuaWriter.Build([export]), "Guildkeep_ServerData").GetTable("guilds")!.Items[0].AsTable()!;
		var raid = section.GetTable("raids")!.Items[0].AsTable()!;
		Assert.Equal((12, "Cœur du Magma"), (raid.GetInt("eventId"), raid.GetString("instance")));
		Assert.Equal(150, raid.GetTable("participants")!.Items[0].AsTable()!.GetInt("minutes"));
		Assert.True(raid.GetTable("loot")!.Items[0].AsTable()!.GetBool("voided"));
		Assert.Equal("excused", raid.GetTable("corrections")!.Items[0].AsTable()!.GetString("status"));
	}
}

public sealed class CraftSyncTests
{
	private const string Saved = """
		GuildkeepDB = {
			["characters"] = {
				["Thorgal Marteau"] = {
					["guild"] = "FORMER HOPE",
					["professions"] = { ["Alchimie"] = { ["level"] = 300, ["max"] = 300, ["recipes"] = { 17635 }, ["catalog"] = { 17635, 17636, 99999 } } },
				},
			},
			["recipeNames"] = { [17635] = "Flacon des Titans", [17636] = "Flacon de sagesse distillée" },
			["orders"] = {
				["Thorgal Marteau-1-2"] = { ["requester"] = "Thorgal Marteau", ["item"] = "Lingot", ["qty"] = 5, ["recipeId"] = 22967 },
			},
			["orderActions"] = { ["12"] = { ["status"] = "accepted", ["crafter"] = "Nayra Solen" } },
			["orderOffers"] = { ["14|Thorgal Marteau"] = { ["order"] = "14", ["crafter"] = "Thorgal Marteau", ["note"] = "Ce soir" } },
		}
		""";

	[Fact]
	public void BuildPayload_Should_ReadOpenRequestsOffersActionsAndCatalog()
	{
		var payload = SavedVariablesReader.BuildPayload(Saved)!;
		var order = Assert.Single(payload.Orders!);
		Assert.Equal((null as string, 22967), (order.Crafter, order.RecipeId!.Value));
		Assert.Equal("Nayra Solen", Assert.Single(payload.OrderActions!).Crafter);
		Assert.Equal((14, "Ce soir"), (Assert.Single(payload.Offers!).OrderId!.Value, payload.Offers![0].Note));
		var catalog = payload.Characters.Single().Professions.Single().Catalog!;
		Assert.Equal([17635, 17636], catalog.Select(r => r.Id)); // sans nom connu : ignorée
	}

	[Fact]
	public void DataLua_Should_CarryOffersAndCatalog()
	{
		var export = new ExportDto(1, DateTime.UtcNow, [], [],
			[new ExportOrderDto(14, null, "Nayra Solen", null, "Flacon des Titans", 2, null, "open", "2026-11-23 10:00", 17635, "2026-11-28 10:00",
				[new ExportOfferDto("Thorgal Marteau", "Ce soir", "2026-11-23 11:00")])],
			"FORMER HOPE", Catalog: [new ExportCatalogRecipeDto(17635, "Flacon des Titans", "Alchimie")]);
		var section = LuaParser.ParseSavedVariable(DataLuaWriter.Build([export]), "Guildkeep_ServerData").GetTable("guilds")!.Items[0].AsTable()!;
		var order = section.GetTable("orders")!.Items[0].AsTable()!;
		Assert.Null(order.GetString("crafter"));
		Assert.Equal("Thorgal Marteau", order.GetTable("offers")!.Items[0].AsTable()!.GetString("crafter"));
		Assert.Equal("Alchimie", section.GetTable("catalog")!.GetTable("17635")!.Items[1].AsString());
	}
}

public sealed class QuestSyncTests
{
	[Fact]
	public void Quests_Should_RoundTrip_AndEmptyLogStaysEmpty()
	{
		const string saved = """
			GuildkeepDB = {
				["characters"] = {
					["Thorgal Marteau"] = {
						["guild"] = "FORMER HOPE",
						["quests"] = { { ["id"] = 4903, ["title"] = "Avertir Thrall", ["kind"] = "dungeon", ["location"] = "Pic Rochenoire", ["level"] = 58, ["complete"] = true } },
					},
					["Nayra Solen"] = { ["guild"] = "FORMER HOPE", ["quests"] = {} },
					["Elaria Brume"] = { ["guild"] = "FORMER HOPE" },
				},
			}
			""";
		var payload = SavedVariablesReader.BuildPayload(saved)!;
		var thorgal = payload.Characters.Single(c => c.Name == "Thorgal Marteau").Quests!;
		Assert.True(Assert.Single(thorgal).Complete);
		Assert.Empty(payload.Characters.Single(c => c.Name == "Nayra Solen").Quests!);
		Assert.Null(payload.Characters.Single(c => c.Name == "Elaria Brume").Quests);

		var export = new ExportDto(1, DateTime.UtcNow, [], [new ExportCharacterDto("Thorgal Marteau", null, null, null, null, 60, [], null, thorgal)], [], "FORMER HOPE");
		var section = LuaParser.ParseSavedVariable(DataLuaWriter.Build([export]), "Guildkeep_ServerData").GetTable("guilds")!.Items[0].AsTable()!;
		var quest = section.GetTable("characters")!.Items[0].AsTable()!.GetTable("quests")!.Items[0].AsTable()!;
		Assert.Equal(("Pic Rochenoire", 58), (quest.GetString("location"), quest.GetInt("level")));
	}
}
