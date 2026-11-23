using GuildProfessions.Companion;
using GuildProfessions.Companion.Lua;
using Xunit;

namespace GuildProfessions.Companion.Tests;

/// <summary>Lot 6 : communication de la guilde, dans les deux sens.</summary>
public sealed class CommunicationSyncTests
{
	private const string Saved = """
		GuildkeepDB = {
			["characters"] = {
				["Thorgal Marteau"] = { ["guild"] = "FORMER HOPE" },
				["Mira Vent"] = { ["guild"] = "Les Autres" },
			},
			["announcementEdits"] = {
				["Thorgal Marteau-1-2"] = { ["key"] = "Thorgal Marteau-1-2", ["title"] = "Raid ce soir", ["body"] = "20 h 30", ["eventId"] = 12, ["by"] = "Thorgal Marteau", ["at"] = 100 },
				["7"] = { ["key"] = "7", ["title"] = "Ancienne", ["body"] = "x", ["by"] = "Thorgal Marteau", ["deleted"] = true, ["at"] = 110 },
			},
			["templateEdits"] = { ["raid"] = { ["key"] = "raid", ["title"] = "Raid {event}", ["body"] = "Le {date}", ["by"] = "Thorgal Marteau", ["at"] = 120 } },
			["handbookEdits"] = { ["butin"] = { ["key"] = "butin", ["title"] = "Butin", ["body"] = "Réservations", ["order"] = 2, ["by"] = "Thorgal Marteau", ["at"] = 130 } },
			["goalEdits"] = { ["g-1"] = { ["key"] = "g-1", ["title"] = "Flacons", ["target"] = 200, ["unit"] = "flacons", ["by"] = "Thorgal Marteau", ["at"] = 140 } },
			["goalContributions"] = {
				["c-1"] = { ["clientId"] = "c-1", ["goal"] = "g-1", ["character"] = "Thorgal Marteau", ["amount"] = 20, ["at"] = 150 },
				["c-2"] = { ["clientId"] = "c-2", ["goal"] = "9", ["character"] = "Mira Vent", ["amount"] = 5, ["at"] = 160 },
			},
		}
		""";

	[Fact]
	public void BuildPayload_Should_ReadEveryKindOfChange()
	{
		var c = SavedVariablesReader.BuildPayload(Saved)!.Communication!;
		Assert.Equal(2, c.Announcements!.Count);
		Assert.Equal((7, true), (c.Announcements.Single(a => a.ServerId is not null).ServerId!.Value, c.Announcements.Single(a => a.ServerId is not null).Deleted));
		Assert.Equal(12, c.Announcements.Single(a => a.ServerId is null).EventId);
		Assert.Equal(2, Assert.Single(c.Handbook!).Order);
		Assert.Equal(200, Assert.Single(c.Goals!).Target);
		Assert.Equal(("g-1", null as int?), (c.Contributions!.Single(x => x.ClientId == "c-1").GoalClientId, c.Contributions!.Single(x => x.ClientId == "c-1").GoalId));
		Assert.Equal(9, c.Contributions.Single(x => x.ClientId == "c-2").GoalId);
	}

	[Fact]
	public void Split_Should_SendEachChange_ToTheActingCharactersGuild()
	{
		var payload = SavedVariablesReader.BuildPayload(Saved)!;
		var guilds = new List<GuildEntry> { new() { Name = "FORMER HOPE", Token = "a" }, new() { Name = "Les Autres", Token = "b" } };

		var uploads = UploadSplitter.Split(payload, guilds, out _);

		var hope = uploads.Single(u => u.Guild.Name == "FORMER HOPE").Payload.Communication!;
		Assert.Equal(2, hope.Announcements!.Count);
		Assert.Equal(["c-1"], hope.Contributions!.Select(c => c.ClientId));
		var autres = uploads.Single(u => u.Guild.Name == "Les Autres").Payload.Communication!;
		Assert.Null(autres.Announcements);
		Assert.Equal(["c-2"], autres.Contributions!.Select(c => c.ClientId));
	}

	[Fact]
	public void DataLua_Should_CarryCommunication_AndOmitItWhenAbsent()
	{
		var communication = new ExportCommunicationDto(
			[new ExportAnnouncementDto(3, "a-1", "Raid \"ce soir\"", "Ligne 1\nLigne 2", "Thorgal Marteau", 12, 100, 100)],
			[new ExportTemplateDto("raid", "Raid {event}", "Le {date}", 120)],
			[new ExportHandbookSectionDto("butin", "Butin", "Réservations", 2, "Thorgal Marteau", 130)],
			[new ExportGoalDto(5, "g-1", "Flacons", null, 200, "flacons", null, false, "Thorgal Marteau", 140, 20,
				[new ExportContributionDto("c-1", "Thorgal Marteau", 20, "Titans", 150)])]);
		var export = new ExportDto(1, DateTime.UtcNow, [], [], [], "FORMER HOPE", Communication: communication);

		var section = LuaParser.ParseSavedVariable(DataLuaWriter.Build([export]), "Guildkeep_ServerData").GetTable("guilds")!.Items[0].AsTable()!;
		var c = section.GetTable("communication")!;
		Assert.Equal("Ligne 1\nLigne 2", c.GetTable("announcements")!.Items[0].AsTable()!.GetString("body"));
		Assert.Equal(12, c.GetTable("announcements")!.Items[0].AsTable()!.GetInt("eventId"));
		Assert.Equal("raid", c.GetTable("templates")!.Items[0].AsTable()!.GetString("key"));
		var goal = c.GetTable("goals")!.Items[0].AsTable()!;
		Assert.Equal((20, "Titans"), (goal.GetInt("progress"), goal.GetTable("contributions")!.Items[0].AsTable()!.GetString("note")));

		var without = LuaParser.ParseSavedVariable(DataLuaWriter.Build([export with { Communication = null }]), "Guildkeep_ServerData")
			.GetTable("guilds")!.Items[0].AsTable()!;
		Assert.Null(without.GetTable("communication"));
	}
}
