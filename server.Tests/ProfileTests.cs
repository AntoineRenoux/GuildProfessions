using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>Lot 7 : profils (principal, rerolls, rôles), talents, versions, arrivées et départs, accueil.</summary>
public sealed class ProfileTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();
	private readonly TestModules _modules;
	private readonly RosterService _roster;
	private readonly ProfileService _profiles;

	public ProfileTests()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		_db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _guild);
		_db.Database.EnsureCreated();
		_guild.Use(TestGuilds.Create(_db, "FORMER HOPE", "former-hope"));
		_modules = TestModules.For(_db, _guild);
		_roster = _modules.Roster;
		_profiles = _modules.Profiles;
	}

	public void Dispose()
	{
		_modules.Dispose();
		_db.Dispose();
		_connection.Dispose();
	}

	private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	private static UploadPayload Payload(UploadProfiles profiles, params string[] characters) =>
		new(characters.Select(c => new UploadCharacter(c, "MAGE", null, null, null, 60, [])).ToList(), Profiles: profiles);

	private async Task<Member> OfficerAsync(string discordId, string character)
	{
		await _roster.PromoteOfficerAsync(discordId, character);
		return await _db.Members.SingleAsync(m => m.DiscordId == discordId);
	}

	[Fact]
	public async Task Profile_Should_OnlyGroupCharactersOfTheUpload_AndKeepTheLatest()
	{
		var refused = await _profiles.ApplyUploadAsync(null, Payload(new(Profiles:
			[new UploadProfile("Nayra", ["Ombrelune"], ["healer"], null, null, 100)]), "Nayra"));
		Assert.Single(refused); // Ombrelune n'est pas dans l'envoi
		Assert.Empty(await _db.PlayerProfiles.ToListAsync());

		await _profiles.ApplyUploadAsync(null, Payload(new(Profiles:
			[new UploadProfile("Nayra", ["Ombrelune", "Nayra"], ["healer", "pirate", "ranged"], "Soirs", null, 100)]), "Nayra", "Ombrelune"));
		var profile = await _db.PlayerProfiles.SingleAsync();
		Assert.Equal(["Ombrelune"], ProfileService.AltsOf(profile));
		Assert.Equal("healer,ranged", profile.Roles);

		// Une version plus ancienne n'écrase pas la plus récente.
		await _profiles.ApplyUploadAsync(null, Payload(new(Profiles:
			[new UploadProfile("Nayra", [], ["tank"], null, null, 50)]), "Nayra"));
		Assert.Equal("healer,ranged", (await _db.PlayerProfiles.AsNoTracking().SingleAsync()).Roles);
	}

	[Fact]
	public async Task Profile_Should_MoveACharacterToItsNewMain()
	{
		await _profiles.ApplyUploadAsync(null, Payload(new(Profiles:
			[new UploadProfile("Nayra", ["Ombrelune"], [], null, null, 100)]), "Nayra", "Ombrelune"));
		// Ombrelune devient principal : l'ancien profil où il était principal ou reroll s'adapte.
		await _profiles.ApplyUploadAsync(null, Payload(new(Profiles:
			[new UploadProfile("Ombrelune", ["Nayra"], ["tank"], null, null, 200)]), "Nayra", "Ombrelune"));
		var profile = await _db.PlayerProfiles.AsNoTracking().SingleAsync();
		Assert.Equal("Ombrelune", profile.Main);
		Assert.Equal(["Nayra"], ProfileService.AltsOf(profile));
	}

	[Fact]
	public async Task TalentsAndVersions_Should_BeForOwnCharactersOnly()
	{
		await _profiles.ApplyUploadAsync(null, Payload(new(
			Talents: [new UploadTalents("Nayra", "Sacré", "31/20/0", 100), new UploadTalents("Autre", null, "0/0/51", 100)],
			Versions: [new UploadVersion("Nayra", "1.7.0"), new UploadVersion("Autre", "0.1")]), "Nayra"));
		Assert.Equal("31/20/0", (await _db.CharacterTalents.SingleAsync()).Summary);
		Assert.Equal("Nayra", (await _db.AddonVersionReports.SingleAsync()).Character);
	}

	[Fact]
	public async Task RosterChanges_Should_BeDeduplicated_AndRecent()
	{
		var at = Now - 3600;
		await _profiles.ApplyUploadAsync(null, Payload(new(RosterChanges:
		[
			new UploadRosterChange("Bleu", "join", at),
			new UploadRosterChange("Bleu", "join", at + 600), // même arrivée vue par un autre membre
			new UploadRosterChange("Vieux", "leave", Now - 200 * 86400),
			new UploadRosterChange("Bizarre", "promote", at),
		]), "Nayra"));
		await _profiles.ApplyUploadAsync(null, Payload(new(RosterChanges: [new UploadRosterChange("Bleu", "join", at + 1200)]), "Ombrelune"));
		var change = await _db.RosterChanges.SingleAsync();
		Assert.Equal("Bleu", change.Character);
		Assert.Equal("Nayra", change.ReportedBy);
	}

	[Fact]
	public async Task Onboarding_Should_BeSelfOrOfficer_AndDeriveStepsFromProfileAndDiscord()
	{
		var refused = await _profiles.ApplyUploadAsync(null, Payload(new(Onboarding:
			[new UploadOnboardingStep("Bleu", "handbook", true, "Nayra", 100)]), "Nayra"));
		Assert.Single(refused);

		await _profiles.ApplyUploadAsync(null, Payload(new(
			Profiles: [new UploadProfile("Bleu", [], ["melee"], null, null, 100)],
			Onboarding: [new UploadOnboardingStep("Bleu", "handbook", true, "Bleu", 100)],
			RosterChanges: [new UploadRosterChange("Bleu", "join", Now - 60)]), "Bleu"));
		var officer = await OfficerAsync("1", "Chef");
		await _profiles.ApplyUploadAsync(officer, Payload(new(Onboarding:
			[new UploadOnboardingStep("Bleu", "intro", true, "Chef", 100)]), "Chef"));
		await _roster.LinkCharacterAsync("2", "bleu", "Bleu");

		var export = await _profiles.BuildExportAsync();
		var bleu = export.Onboarding.Single(o => o.Character == "Bleu");
		Assert.Equal(["discord", "main", "profile", "handbook", "intro"], bleu.Done);
		Assert.Single(export.RosterChanges);
		Assert.Empty(await _profiles.IncompleteAsync());
	}

	[Fact]
	public async Task Discord_Should_EditOwnProfile_AndOnlyOfficersTickOthers()
	{
		await _roster.LinkCharacterAsync("2", "nayra", "Nayra");
		await _roster.LinkCharacterAsync("2", "nayra", "Ombrelune");
		var edited = await _profiles.EditFromDiscordAsync("2", ["tank"], "Week-end", null, "Nayra");
		Assert.True(edited.IsSuccess);
		var profile = await _profiles.FindProfileAsync("ombrelune");
		Assert.NotNull(profile);
		Assert.Equal("Nayra", profile.Main);
		Assert.Equal(["Ombrelune"], ProfileService.AltsOf(profile));
		Assert.False((await _profiles.EditFromDiscordAsync("2", null, null, null, "Inconnu")).IsSuccess);

		Assert.True((await _profiles.SetStepFromDiscordAsync("2", false, "handbook", true, null)).IsSuccess);
		Assert.False((await _profiles.SetStepFromDiscordAsync("2", false, "intro", true, "Bleu")).IsSuccess);
		await OfficerAsync("1", "Chef");
		Assert.True((await _profiles.SetStepFromDiscordAsync("1", false, "intro", true, "Bleu")).IsSuccess);
		Assert.True((await _profiles.SetStepFromDiscordAsync("1", false, "intro", false, "Bleu")).IsSuccess);
		Assert.False((await _db.OnboardingSteps.AsNoTracking().SingleAsync(o => o.Character == "Bleu")).Done);
	}

	[Fact]
	public async Task Export_Should_CarryProfiles()
	{
		await _profiles.ApplyUploadAsync(null, Payload(new(
			Profiles: [new UploadProfile("Nayra", [], ["healer"], "Soirs", "Dispo pour les donjons", 100)],
			Talents: [new UploadTalents("Nayra", "Sacré", "31/20/0", 100)]), "Nayra"));
		var export = (await _modules.Export.BuildAsync()).Export;
		Assert.NotNull(export.Profiles);
		Assert.Equal("Soirs", export.Profiles.Profiles.Single().Availability);
		Assert.Equal("31/20/0", export.Profiles.Talents.Single().Summary);
	}
}
