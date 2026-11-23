using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>Lot 3 : présence observée, butin, corrections d'officiers et taux.</summary>
public sealed class AttendanceTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();
	private readonly TestModules _modules;
	private readonly RosterService _roster;
	private readonly AttendanceService _attendance;
	private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	private static readonly string[] Raiders = ["Thorgal Marteau", "Nayra Solen", "Elaria Brume", "Kael Fourbe", "Mira Vent", "Brann Roc", "Lyra Sol"];

	public AttendanceTests()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		_db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _guild);
		_db.Database.EnsureCreated();
		_guild.Use(TestGuilds.Create(_db, "FORMER HOPE", "former-hope"));
		_modules = TestModules.For(_db, _guild);
		_roster = _modules.Roster;
		_attendance = _modules.Attendance;
	}

	public void Dispose()
	{
		_modules.Dispose();
		_db.Dispose();
		_connection.Dispose();
	}

	private static UploadCharacter Char(string name, bool officer = false) => new(name, null, null, null, null, 60, [], Officer: officer);

	private static UploadRaid Raid(string key, long startedAt, string observer, Dictionary<string, int> participants) =>
		new(key, "Cœur du Magma", DateTimeOffset.FromUnixTimeSeconds(startedAt).ToString("yyyy-MM-dd"), "Thorgal Marteau",
			startedAt, startedAt + 3 * 3600, observer, participants);

	private static Dictionary<string, int> Everyone(int minutes, params string[] except) =>
		Raiders.Where(r => !except.Contains(r)).ToDictionary(r => r, _ => minutes);

	private Task<List<string>> Upload(string by, bool officer = false, List<UploadRaid>? raids = null, List<UploadRaidLoot>? loot = null,
		List<UploadAttendanceCorrection>? corrections = null, List<UploadLootCorrection>? lootCorrections = null) =>
		_attendance.ApplyUploadAsync(null, new UploadPayload([Char(by, officer)], Raids: raids, Loot: loot,
			AttendanceCorrections: corrections, LootCorrections: lootCorrections));

	[Fact]
	public async Task Observers_Should_MergeIntoOneRaid_KeepingTheLongestPresence()
	{
		await Upload("Thorgal Marteau", raids: [Raid("k1", Now - 86400, "Thorgal Marteau", Everyone(120))]);
		await Upload("Nayra Solen", raids: [Raid("k1", Now - 86400 - 600, "Nayra Solen", new() { ["Nayra Solen"] = 180, ["Thorgal Marteau"] = 90 })]);

		var raid = await _db.RaidSessions.Include(s => s.Participants).SingleAsync();
		Assert.Equal(7, raid.Participants.Count);
		Assert.Equal(180, raid.Participants.Single(p => p.Character == "Nayra Solen").Minutes);
		Assert.Equal(120, raid.Participants.Single(p => p.Character == "Thorgal Marteau").Minutes);
		Assert.Equal(Now - 86400 - 600, raid.StartedAt);
	}

	[Fact]
	public async Task Observer_Should_HaveBeenInTheRaid()
	{
		await Upload("Intrus Lointain", raids: [Raid("k1", Now - 86400, "Intrus Lointain", Everyone(120))]);
		Assert.Empty(await _db.RaidSessions.ToListAsync());
	}

	[Fact]
	public async Task Loot_Should_BeEpicOnly_AndCountedOnceAcrossObservers()
	{
		await Upload("Thorgal Marteau", raids: [Raid("k1", Now - 86400, "Thorgal Marteau", Everyone(120))]);
		UploadRaidLoot Drop(string observer, int quality = 4) => new("k1", "Nayra Solen", 19019, "Serre-tonnerre", quality, 1, Now - 80000, observer);
		await Upload("Thorgal Marteau", loot: [Drop("Thorgal Marteau"), new("k1", "Kael Fourbe", 1, "Bricole", 3, 1, Now, "Thorgal Marteau")]);
		await Upload("Nayra Solen", raids: [Raid("k1", Now - 86400, "Nayra Solen", Everyone(120))], loot: [Drop("Nayra Solen")]);

		var loot = Assert.Single(await _db.RaidLoot.ToListAsync());
		Assert.Equal(19019, loot.ItemId);
	}

	[Fact]
	public async Task Correction_Should_BeRefused_WhenOfficerRankIsOnlyDeclaredWithGuildToken()
	{
		await Upload("Thorgal Marteau", raids: [Raid("r1", Now - 3 * 86400, "Thorgal Marteau", Everyone(150))]);

		// Un détenteur du pack se déclare officier pour corriger la présence d'un rival (chemin réel de l'envoi).
		var outcome = await _modules.Ingestion.IngestAsync(null, new UploadPayload([Char("Nayra Solen", officer: true)],
			AttendanceCorrections: [new("r1", "Kael Fourbe", "absent", null, "Nayra Solen", Now)]), UploadSource.GuildToken);

		Assert.Contains(outcome.Result.Warnings, w => w.Contains("réservée aux officiers"));
		Assert.Empty(await _db.AttendanceCorrections.ToListAsync());
		Assert.False((await _db.Characters.SingleAsync(c => c.Name == "Nayra Solen")).IsOfficer);
	}

	[Fact]
	public async Task Rates_Should_UseObservedPresence_AndOfficerCorrections()
	{
		// Trois raids de guilde ce mois-ci ; Mira n'était qu'au premier.
		await Upload("Thorgal Marteau", raids: [Raid("r1", Now - 3 * 86400, "Thorgal Marteau", Everyone(150))]);
		await Upload("Thorgal Marteau", raids: [Raid("r2", Now - 10 * 86400, "Thorgal Marteau", Everyone(150, "Mira Vent"))]);
		await Upload("Thorgal Marteau", raids: [Raid("r3", Now - 17 * 86400, "Thorgal Marteau", Everyone(150, "Mira Vent", "Kael Fourbe"))]);
		// Un raid plus ancien (40 jours) : compte dans 60 et 90 jours, et dans « les 30 d'avant ».
		await Upload("Thorgal Marteau", raids: [Raid("r0", Now - 40 * 86400, "Thorgal Marteau", Everyone(150))]);

		// Simple membre : sa correction est refusée. Officier : Kael excusé au 3e raid, Mira marquée présente au 2e.
		await _roster.PromoteOfficerAsync("3", "Elaria Brume");
		Assert.Single(await Upload("Nayra Solen", corrections: [new("r3", "Kael Fourbe", "excused", null, "Nayra Solen", Now)]));
		Assert.Empty(await Upload("Elaria Brume", officer: true, corrections: [
			new("r3", "Kael Fourbe", "excused", "Déplacement", "Elaria Brume", Now),
			new("r2", "Mira Vent", "present", "Déconnexion", "Elaria Brume", Now)]));

		var sessions = await _attendance.RecentAsync(AttendanceService.HistoryWindow);
		var mira = AttendanceStats.StatsFor(sessions, "Mira Vent", DateTimeOffset.UtcNow);
		var kael = AttendanceStats.StatsFor(sessions, "Kael Fourbe", DateTimeOffset.UtcNow);
		Assert.Equal(new AttendanceStats.Rate(2, 3), mira.Last30);
		Assert.Equal(new AttendanceStats.Rate(2, 2), kael.Last30); // excusé = hors calcul
		Assert.Equal(new AttendanceStats.Rate(1, 1), mira.Previous30);
		Assert.Equal(new AttendanceStats.Rate(3, 4), mira.Last60);
	}

	[Fact]
	public async Task SmallGroups_Should_NotCountAsGuildRaids()
	{
		await Upload("Thorgal Marteau", raids: [Raid("petit", Now - 86400, "Thorgal Marteau", new() { ["Thorgal Marteau"] = 120, ["Nayra Solen"] = 120 })]);
		var stats = AttendanceStats.StatsFor(await _attendance.RecentAsync(AttendanceService.HistoryWindow), "Nayra Solen", DateTimeOffset.UtcNow);
		Assert.Equal(0, stats.Last30.Eligible);
	}

	[Fact]
	public async Task Raid_Should_LinkToTheEveningEvent()
	{
		var events = _modules.Events;
		await events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")],
			Events: [new UploadEvent("e1", null, "MC", "raid", Now - 86400 + 900, null, "Thorgal Marteau", 0, 0, 0, 0, false, 1)]));
		await Upload("Thorgal Marteau", raids: [Raid("k1", Now - 86400, "Thorgal Marteau", Everyone(120))]);
		Assert.Equal((await _db.Events.SingleAsync()).Id, (await _db.RaidSessions.SingleAsync()).EventId);
	}

	[Fact]
	public async Task Discord_Should_LetOfficersCorrectAndVoid()
	{
		await Upload("Thorgal Marteau", raids: [Raid("k1", Now - 86400, "Thorgal Marteau", Everyone(120))],
			loot: [new("k1", "Nayra Solen", 19019, "Serre-tonnerre", 5, 1, Now - 80000, "Thorgal Marteau")]);
		var raidId = (await _db.RaidSessions.SingleAsync()).Id;
		var lootId = (await _db.RaidLoot.SingleAsync()).Id;
		await _roster.LinkCharacterAsync("1", "membre", "Nayra Solen");
		await _roster.LinkCharacterAsync("2", "admin", "Elaria Brume");

		Assert.False((await _attendance.CorrectFromDiscordAsync("1", false, raidId, "Kael Fourbe", "absent", null)).IsSuccess);
		Assert.True((await _attendance.CorrectFromDiscordAsync("2", true, raidId, "kael fourbe", "absent", "Parti tôt")).IsSuccess);
		Assert.False((await _attendance.VoidLootFromDiscordAsync("1", false, lootId, true, null)).IsSuccess);
		Assert.True((await _attendance.VoidLootFromDiscordAsync("2", true, lootId, true, "Désenchanté")).IsSuccess);

		var raid = (await _attendance.GetAsync(raidId))!;
		Assert.False(AttendanceStats.Attended(raid, "Kael Fourbe"));
		Assert.True(raid.Loot.Single().Voided);
		var export = Assert.Single(await _attendance.BuildExportAsync());
		Assert.True(export.Loot.Single().Voided);
		Assert.Equal("absent", export.Corrections.Single().Status);
	}
}
