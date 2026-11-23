using GuildProfessions.Companion;
using GuildProfessions.Companion.Lua;
using Xunit;

namespace GuildProfessions.Companion.Tests;

/// <summary>Lot 7 : profils, talents, versions, arrivées et départs, accueil, dans les deux sens.</summary>
public sealed class ProfilesSyncTests
{
	private const string Saved = """
		GuildkeepDB = {
			["characters"] = {
				["Thorgal Marteau"] = { ["guild"] = "FORMER HOPE" },
				["Thorgal Reroll"] = { ["guild"] = "FORMER HOPE" },
				["Mira Vent"] = { ["guild"] = "Les Autres" },
			},
			["profileEdits"] = {
				["Thorgal Marteau"] = { ["main"] = "Thorgal Marteau", ["alts"] = { "Thorgal Reroll" }, ["roles"] = { "tank", "melee" }, ["availability"] = "Soirs", ["at"] = 100 },
			},
			["talents"] = {
				["Thorgal Marteau"] = { ["spec"] = "Protection", ["summary"] = "5/5/41", ["at"] = 110 },
				["Mira Vent"] = { ["summary"] = "0/31/20", ["at"] = 111 },
			},
			["versions"] = { ["Thorgal Marteau"] = "1.7.0", ["Mira Vent"] = "1.6.2" },
			["rosterChanges"] = {
				{ ["character"] = "Bleu", ["kind"] = "join", ["at"] = 120, ["by"] = "Thorgal Marteau" },
				{ ["character"] = "Ancien", ["kind"] = "leave", ["at"] = 121, ["by"] = "Mira Vent" },
			},
			["onboardingEdits"] = {
				["Bleu|handbook"] = { ["character"] = "Bleu", ["step"] = "handbook", ["done"] = true, ["by"] = "Thorgal Marteau", ["at"] = 130 },
			},
		}
		""";

	[Fact]
	public void BuildPayload_Should_ReadProfilesAndOnboarding()
	{
		var p = SavedVariablesReader.BuildPayload(Saved)!.Profiles!;
		var profile = Assert.Single(p.Profiles!);
		Assert.Equal(["Thorgal Reroll"], profile.Alts);
		Assert.Equal(["tank", "melee"], profile.Roles);
		Assert.Equal(2, p.Talents!.Count);
		Assert.Equal("1.7.0", p.Versions!.Single(v => v.Character == "Thorgal Marteau").Version);
		Assert.Equal("Thorgal Marteau", p.RosterChanges!.Single(r => r.Character == "Bleu").By);
		Assert.True(Assert.Single(p.Onboarding!).Done);
	}

	[Fact]
	public void Split_Should_SendEachRecord_ToItsCharactersGuild()
	{
		var payload = SavedVariablesReader.BuildPayload(Saved)!;
		var guilds = new List<GuildEntry> { new() { Name = "FORMER HOPE", Token = "a" }, new() { Name = "Les Autres", Token = "b" } };

		var uploads = UploadSplitter.Split(payload, guilds, out _);

		var hope = uploads.Single(u => u.Guild.Name == "FORMER HOPE").Payload.Profiles!;
		Assert.Single(hope.Profiles!);
		Assert.Equal(["Bleu"], hope.RosterChanges!.Select(r => r.Character));
		Assert.Single(hope.Onboarding!);
		var autres = uploads.Single(u => u.Guild.Name == "Les Autres").Payload.Profiles!;
		Assert.Null(autres.Profiles);
		Assert.Equal(["Mira Vent"], autres.Talents!.Select(t => t.Character));
		Assert.Equal(["Ancien"], autres.RosterChanges!.Select(r => r.Character));
	}

	[Fact]
	public void DataLua_Should_CarryProfiles_AndOmitThemWhenAbsent()
	{
		var profiles = new ExportProfilesDto(
			[new ExportProfileDto("Thorgal Marteau", ["Thorgal Reroll"], ["tank"], "Soirs \"tard\"", null, 100)],
			[new ExportTalentsDto("Thorgal Marteau", "Protection", "5/5/41", 110)],
			[new ExportRosterChangeDto("Bleu", "join", 120)],
			[new ExportOnboardingDto("Bleu", ["discord", "handbook"], 130)],
			[new ExportVersionDto("Thorgal Marteau", "1.7.0", 140)]);
		var export = new ExportDto(1, DateTime.UtcNow, [], [], [], "FORMER HOPE", Profiles: profiles);

		var section = LuaParser.ParseSavedVariable(DataLuaWriter.Build([export]), "Guildkeep_ServerData").GetTable("guilds")!.Items[0].AsTable()!;
		var p = section.GetTable("profiles")!;
		var profile = p.GetTable("profiles")!.Items[0].AsTable()!;
		Assert.Equal("Soirs \"tard\"", profile.GetString("availability"));
		Assert.Equal("Thorgal Reroll", profile.GetTable("alts")!.Items[0].AsString());
		Assert.Equal("5/5/41", p.GetTable("talents")!.Items[0].AsTable()!.GetString("summary"));
		Assert.Equal("handbook", p.GetTable("onboarding")!.Items[0].AsTable()!.GetTable("done")!.Items[1].AsString());
		Assert.Equal("1.7.0", p.GetTable("versions")!.Items[0].AsTable()!.GetString("version"));

		var without = LuaParser.ParseSavedVariable(DataLuaWriter.Build([export with { Profiles = null }]), "Guildkeep_ServerData")
			.GetTable("guilds")!.Items[0].AsTable()!;
		Assert.Null(without.GetTable("profiles"));
	}
}
