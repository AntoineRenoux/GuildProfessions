using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>Sur Discord, les mêmes actions qu'en jeu, avec les mêmes droits.</summary>
public sealed class EventDiscordTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();
	private readonly TestModules _modules;
	private readonly RosterService _roster;
	private readonly EventService _events;
	private static readonly long Tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

	public EventDiscordTests()
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

	private async Task<int> CreateAsOrganizerAsync()
	{
		await _roster.LinkCharacterAsync("1", "orga", "Thorgal Marteau");
		var created = await _events.CreateFromDiscordAsync("1", false,
			new EventService.EventFields("Cœur du Magma", "raid", Tomorrow, "Flacons", 2, 6, 8, 8), null);
		Assert.True(created.IsSuccess, created.Message);
		return (await _db.Events.SingleAsync()).Id;
	}

	[Fact]
	public async Task Create_Should_UseMainCharacterAsOrganizer()
	{
		await CreateAsOrganizerAsync();
		var saved = await _db.Events.SingleAsync();
		Assert.Equal(("Thorgal Marteau", 6, DataSource.Discord), (saved.Organizer, saved.TargetHealer, saved.Source));
		Assert.True(saved.UpdatedAt > 0);
	}

	[Fact]
	public async Task Create_Should_RequireALinkedCharacter()
	{
		var result = await _events.CreateFromDiscordAsync("404", false, new EventService.EventFields("X", "raid", Tomorrow), null);
		Assert.False(result.IsSuccess);
	}

	[Fact]
	public async Task Edit_Cancel_Place_Should_FollowInGameRights()
	{
		var id = await CreateAsOrganizerAsync();
		await _roster.LinkCharacterAsync("2", "membre", "Nayra Solen");
		await _events.RespondFromDiscordAsync("2", id, "yes", "healer", null, null, "Sacré");

		// Un simple membre ne touche ni à l'événement ni au roster.
		Assert.False((await _events.EditFromDiscordAsync("2", false, id, new EventService.EventFields(Title: "Piraté"))).IsSuccess);
		Assert.False((await _events.SetCancelledFromDiscordAsync("2", false, id, true)).IsSuccess);
		Assert.False((await _events.PlaceFromDiscordAsync("2", false, id, "Nayra Solen", "confirmed")).IsSuccess);

		// L'organisateur, oui.
		Assert.True((await _events.EditFromDiscordAsync("1", false, id, new EventService.EventFields(Title: "Onyxia", Tank: 1))).IsSuccess);
		Assert.True((await _events.PlaceFromDiscordAsync("1", false, id, "nayra solen", "confirmed")).IsSuccess);
		Assert.True((await _events.SetCancelledFromDiscordAsync("1", false, id, true)).IsSuccess);
		Assert.True((await _events.SetCancelledFromDiscordAsync("1", false, id, false)).IsSuccess);

		var saved = await _db.Events.Include(e => e.Signups).SingleAsync();
		Assert.Equal(("Onyxia", 1, 6, false), (saved.Title, saved.TargetTank, saved.TargetHealer, saved.Cancelled));
		var signup = Assert.Single(saved.Signups);
		Assert.Equal(("Nayra Solen", "confirmed", "yes", "Sacré"), (signup.Character, signup.Placement, signup.Response, signup.Spec));
	}

	[Fact]
	public async Task Officers_Should_ManageEvents_FromInGameRankOrServerAdmin()
	{
		var id = await CreateAsOrganizerAsync();
		await _roster.LinkCharacterAsync("3", "officier", "Elaria Brume");
		await _roster.LinkCharacterAsync("4", "admin", "Kael Fourbe");

		// Rang d'officier relevé par l'addon, envoyé avec le token personnel du membre.
		Assert.False((await _events.SetCancelledFromDiscordAsync("3", false, id, true)).IsSuccess);
		var officer = await _roster.GetOrCreateMemberAsync("3", "officier");
		await _roster.ApplyUploadAsync(officer, new UploadPayload([new UploadCharacter("Elaria Brume", "MAGE", null, null, null, 60, [], Officer: true)]));
		Assert.True((await _events.SetCancelledFromDiscordAsync("3", false, id, true)).IsSuccess);

		// Gérer le serveur Discord suffit aussi.
		Assert.True((await _events.SetCancelledFromDiscordAsync("4", true, id, false)).IsSuccess);
	}

	[Fact]
	public async Task Discord_Change_Should_BeatAnOlderInGameEdit()
	{
		var id = await CreateAsOrganizerAsync();
		await _events.EditFromDiscordAsync("1", false, id, new EventService.EventFields(Title: "Depuis Discord"));

		// Une modification en jeu faite avant (horodatage plus ancien) arrive ensuite.
		await _events.ApplyUploadAsync(null, new UploadPayload([new UploadCharacter("Thorgal Marteau", null, null, null, null, null, [])],
			Events: [new UploadEvent(null, id, "Depuis le jeu", "raid", Tomorrow, null, "Thorgal Marteau", 0, 0, 0, 0, false, 10)]));

		Assert.Equal("Depuis Discord", (await _db.Events.SingleAsync()).Title);
	}

	[Theory]
	[InlineData("29/11/2026", "20:45", "Europe/Paris", "2026-11-29T19:45:00Z")]
	[InlineData("2026-07-14", "21h", "Europe/Paris", "2026-07-14T19:00:00Z")]
	[InlineData("29/11/26", "20h45", "UTC", "2026-11-29T20:45:00Z")]
	[InlineData("1/12", "8:05", "America/New_York", "2026-12-01T13:05:00Z")]
	public void TryParseStart_Should_ReadCommonFormats(string date, string time, string zone, string expected)
	{
		var now = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
		Assert.True(EventSchedule.TryParseStart(date, time, zone, now, out var startsAt));
		Assert.Equal(DateTimeOffset.Parse(expected).ToUnixTimeSeconds(), startsAt);
	}

	[Fact]
	public void TryParseStart_Should_RollToNextYear_AndRejectNonsense()
	{
		var now = new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero);
		Assert.True(EventSchedule.TryParseStart("05/01", "20:00", "UTC", now, out var january));
		Assert.Equal(2027, DateTimeOffset.FromUnixTimeSeconds(january).Year);
		Assert.False(EventSchedule.TryParseStart("31/02/2027", "20:00", "UTC", now, out _));
		Assert.False(EventSchedule.TryParseStart("demain", "20:00", "UTC", now, out _));
		Assert.False(EventSchedule.TryParseStart("29/11", "25:00", "UTC", now, out _));
	}

	[Theory]
	[InlineData(UploadSource.GuildToken)]
	[InlineData(UploadSource.DiscordImport)]
	public async Task DeclaredRank_Should_NotGrantDiscordRights_WithoutPersonalToken(UploadSource source)
	{
		var id = await CreateAsOrganizerAsync();
		var intruder = await _roster.GetOrCreateMemberAsync("5", "intrus");
		await _roster.LinkCharacterAsync("5", "intrus", "Mira Vent");

		// Chaîne fabriquée à la main (/gkeep import) ou token de guilde partagé : Officer=true est ignoré.
		var member = source == UploadSource.DiscordImport ? intruder : null;
		await _roster.ApplyUploadAsync(member, new UploadPayload([new UploadCharacter("Mira Vent", "MAGE", null, null, null, 60, [], Officer: true)]), source);

		Assert.False((await _events.SetCancelledFromDiscordAsync("5", false, id, true)).IsSuccess);
		Assert.False((await _db.Characters.SingleAsync(c => c.Name == "Mira Vent")).IsOfficer);
	}

	[Fact]
	public async Task GuildToken_Should_NotChangeRankOfLinkedCharacter_AndMissingRank_ShouldKeepIt()
	{
		var officer = await _roster.GetOrCreateMemberAsync("3", "officier");
		await _roster.ApplyUploadAsync(officer, new UploadPayload([new UploadCharacter("Elaria Brume", "MAGE", null, null, null, 60, [], Officer: true)]));

		// Un ancien client n'envoie pas le rang : il est conservé.
		await _roster.ApplyUploadAsync(null, new UploadPayload([new UploadCharacter("Elaria Brume", "MAGE", null, null, null, 60, [])]));
		Assert.True((await _db.Characters.SingleAsync(c => c.Name == "Elaria Brume")).IsOfficer);

		// N'importe quel détenteur du pack ne peut pas priver un officier lié de ses droits…
		await _roster.ApplyUploadAsync(null, new UploadPayload([new UploadCharacter("Elaria Brume", "MAGE", null, null, null, 60, [], Officer: false)]));
		Assert.True((await _db.Characters.SingleAsync(c => c.Name == "Elaria Brume")).IsOfficer);

		// … seul son propre token personnel retire le rang perdu en jeu.
		await _roster.ApplyUploadAsync(officer, new UploadPayload([new UploadCharacter("Elaria Brume", "MAGE", null, null, null, 60, [], Officer: false)]));
		Assert.False((await _db.Characters.SingleAsync(c => c.Name == "Elaria Brume")).IsOfficer);
	}
}
