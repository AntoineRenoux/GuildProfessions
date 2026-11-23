using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GuildProfessions.Server.Tests;

public sealed class EventServiceTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();
	private readonly TestModules _modules;
	private readonly RosterService _roster;
	private readonly EventService _events;
	private static readonly long Tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

	public EventServiceTests()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		_db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _guild);
		_db.Database.EnsureCreated();
		_guild.Use(TestGuilds.Create(_db, "FORMER HOPE", "former-hope"));
		_modules = TestModules.For(_db, _guild);
		_roster = _modules.Roster;
		_events = _modules.Events;
	}

	public void Dispose()
	{
		_modules.Dispose();
		_db.Dispose();
		_connection.Dispose();
	}

	private static UploadCharacter Char(string name, bool officer = false) =>
		new(name, "WARRIOR", null, null, null, 60, [], Officer: officer);

	private static UploadEvent NewEvent(string by, long at, string title = "Cœur du Magma", string? clientId = "c-1", int? serverId = null) =>
		new(clientId, serverId, title, "raid", Tomorrow, "Flacons obligatoires", by, 2, 6, 8, 8, false, at);

	[Fact]
	public async Task Upload_Should_CreateEventAndSignupsInOneGo()
	{
		var payload = new UploadPayload([Char("Thorgal Marteau")],
			Events: [NewEvent("Thorgal Marteau", 100)],
			Signups: [new UploadSignup(null, "c-1", "Thorgal Marteau", "WARRIOR", "tank", "Protection", "yes", null, 100)]);

		var warnings = await _events.ApplyUploadAsync(null, payload);

		Assert.Empty(warnings);
		var created = await _db.Events.Include(e => e.Signups).SingleAsync();
		Assert.Equal("Thorgal Marteau", created.Organizer);
		Assert.Equal(6, created.TargetHealer);
		var signup = Assert.Single(created.Signups);
		Assert.Equal(("tank", "yes", "pending"), (signup.Role, signup.Response, signup.Placement));
	}

	[Fact]
	public async Task Upload_Should_IgnoreEventCreatedForSomeoneElse()
	{
		var warnings = await _events.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")],
			Events: [NewEvent("Thorgal Marteau", 100)]));

		Assert.Single(warnings);
		Assert.Empty(await _db.Events.ToListAsync());
	}

	[Fact]
	public async Task Edits_Should_KeepTheMostRecent_AndOnlyFromOrganizerOrOfficer()
	{
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")], Events: [NewEvent("Thorgal Marteau", 100)]));
		var id = (await _db.Events.SingleAsync()).Id;

		// Un membre quelconque ne peut pas modifier l'événement d'un autre.
		var refused = await _events.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")],
			Events: [NewEvent("Nayra Solen", 300, "Piraté", null, id)]));
		Assert.Single(refused);

		// Un officier (reconnu par l'annuaire) peut ; une modification plus ancienne arrivée ensuite n'écrase rien.
		await _roster.PromoteOfficerAsync("3", "Elaria Brume");
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Elaria Brume", officer: true)],
			Events: [NewEvent("Elaria Brume", 200, "Repaire d'Onyxia", null, id)]));
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")],
			Events: [NewEvent("Thorgal Marteau", 150, "Ancien titre", null, id)]));

		var saved = await _db.Events.SingleAsync();
		Assert.Equal("Repaire d'Onyxia", saved.Title);
		Assert.Equal("Thorgal Marteau", saved.Organizer);
	}

	[Fact]
	public async Task Signup_Should_OnlyBeForOwnCharacters()
	{
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")], Events: [NewEvent("Thorgal Marteau", 100)]));
		var id = (await _db.Events.SingleAsync()).Id;

		var warnings = await _events.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")],
			Signups: [new UploadSignup(id, null, "Thorgal Marteau", null, "tank", null, "no", null, 500)]));

		Assert.Single(warnings);
		Assert.Empty(await _db.EventSignups.ToListAsync());
	}

	[Fact]
	public async Task Placement_And_Response_Should_NotOverwriteEachOther()
	{
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")], Events: [NewEvent("Thorgal Marteau", 100)]));
		var id = (await _db.Events.SingleAsync()).Id;

		// L'organisateur place Nayra avant que sa réponse n'arrive au serveur.
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")],
			Placements: [new UploadPlacement(id, null, "Nayra Solen", "confirmed", "Thorgal Marteau", 300)]));
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")],
			Signups: [new UploadSignup(id, null, "Nayra Solen", "PRIEST", "healer", "Sacré", "yes", null, 200)]));

		var signup = await _db.EventSignups.SingleAsync();
		Assert.Equal(("healer", "yes", "confirmed"), (signup.Role, signup.Response, signup.Placement));

		// Un simple membre ne place personne.
		var refused = await _events.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")],
			Placements: [new UploadPlacement(id, null, "Nayra Solen", "bench", "Nayra Solen", 400)]));
		Assert.Single(refused);
		Assert.Equal("confirmed", (await _db.EventSignups.SingleAsync()).Placement);
	}

	[Fact]
	public async Task Officer_Flag_Should_OnlyCountForCharactersOfThisUpload()
	{
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")], Events: [NewEvent("Thorgal Marteau", 100)]));
		var id = (await _db.Events.SingleAsync()).Id;

		// Se dire officier sous le nom d'un autre ne sert à rien : By doit être de l'envoi.
		var refused = await _events.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen", officer: true)],
			Placements: [new UploadPlacement(id, null, "Nayra Solen", "confirmed", "Elaria Brume", 400)]));
		Assert.Single(refused);
	}

	[Fact]
	public async Task OfficerFlag_DeclaredWithGuildToken_Should_GrantNothing()
	{
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")], Events: [NewEvent("Thorgal Marteau", 100)]));
		var id = (await _db.Events.SingleAsync()).Id;

		// Se déclarer officier dans un envoi au token de guilde ne donne aucun droit.
		var refused = await _events.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen", officer: true)],
			Events: [NewEvent("Nayra Solen", 300, "Piraté", null, id)]));

		Assert.Single(refused);
		Assert.NotEqual("Piraté", (await _db.Events.SingleAsync()).Title);
	}

	[Fact]
	public async Task Export_Should_ContainEventsOfThisGuildOnly()
	{
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")],
			Events: [NewEvent("Thorgal Marteau", 100)],
			Signups: [new UploadSignup(null, "c-1", "Thorgal Marteau", "WARRIOR", "tank", null, "yes", null, 100)]));

		var other = new GuildContext();
		using var otherDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, other);
		other.Use(TestGuilds.Create(otherDb, "Les Autres", "les-autres"));

		var mine = await _events.BuildExportAsync();
		using var otherModules = TestModules.For(otherDb, other);
		var theirs = await otherModules.Events.BuildExportAsync();

		var exported = Assert.Single(mine);
		Assert.Equal("c-1", exported.ClientId);
		Assert.Single(exported.Signups);
		Assert.Empty(theirs);
	}

	[Fact]
	public async Task Discord_Response_Should_UseMainCharacter()
	{
		await _roster.LinkCharacterAsync("42", "nayra", "Nayra Solen");
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")], Events: [NewEvent("Thorgal Marteau", 100)]));
		var id = (await _db.Events.SingleAsync()).Id;

		var result = await _events.RespondFromDiscordAsync("42", id, "tentative", "healer", null, "Retard possible");

		Assert.True(result.IsSuccess);
		var signup = await _db.EventSignups.SingleAsync();
		Assert.Equal(("Nayra Solen", "healer", "tentative"), (signup.Character, signup.Role, signup.Response));
		Assert.False((await _events.RespondFromDiscordAsync("99", id, "yes", null, null, null)).IsSuccess);
	}
}

public sealed class EventStringCodecTests
{
	[Fact]
	public void AddonExport_Should_CarryEventChanges()
	{
		// Forme JSON produite par l'addon (GP.BuildExportPayload), clés camelCase.
		const string json = """
			{"characters":[{"name":"Thorgal Marteau","guild":"FORMER HOPE","officer":true,"professions":[]}],
			 "events":[{"clientId":"Thorgal Marteau-1795000000-1234","title":"Onyxia","kind":"raid","startsAt":1795100000,"by":"Thorgal Marteau","tank":1,"healer":0,"melee":0,"ranged":0,"cancelled":false,"at":1795000000}],
			 "signups":[{"eventId":12,"character":"Thorgal Marteau","role":"tank","response":"yes","at":1795000100}],
			 "placements":[{"eventClientId":"Thorgal Marteau-1795000000-1234","character":"Nayra Solen","placement":"confirmed","by":"Thorgal Marteau","at":1795000200}]}
			""";
		var payload = System.Text.Json.JsonSerializer.Deserialize<UploadPayload>(json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;

		Assert.True(Assert.Single(payload.Characters).Officer);
		Assert.Equal(1795100000L, Assert.Single(payload.Events!).StartsAt);
		Assert.Equal(12, Assert.Single(payload.Signups!).EventId);
		Assert.Equal("confirmed", Assert.Single(payload.Placements!).Placement);
	}
}
