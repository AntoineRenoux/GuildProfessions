using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>Lot 2 : réservations de butin, réservations fermes, verrouillage, soutien, liste de contrôle.</summary>
public sealed class RaidNightTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();
	private readonly TestModules _modules;
	private readonly RosterService _roster;
	private readonly EventService _events;
	private static readonly long Tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
	private int _eventId;

	public RaidNightTests()
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

	private static UploadCharacter Char(string name, bool officer = false) => new(name, null, null, null, null, 60, [], Officer: officer);

	private async Task CreateEventAsync()
	{
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")],
			Events: [new UploadEvent("c-1", null, "Cœur du Magma", "raid", Tomorrow, null, "Thorgal Marteau", 1, 1, 0, 0, false, 100)],
			Signups: [new UploadSignup(null, "c-1", "Thorgal Marteau", "WARRIOR", "tank", null, "yes", null, 100)]));
		_eventId = (await _db.Events.SingleAsync()).Id;
	}

	private Task<List<string>> UploadAsync(string by, bool officer = false, List<UploadReserve>? reserves = null,
		List<UploadHardReserve>? hard = null, List<UploadDuty>? duties = null, List<UploadEvent>? events = null) =>
		_events.ApplyUploadAsync(null, new UploadPayload([Char(by, officer)], Events: events, Reserves: reserves, HardReserves: hard, Duties: duties));

	private UploadReserve Reserve(string character, string priority, int item, long at, string? by = null) =>
		new(_eventId, null, character, priority, item, $"Objet {item}", by ?? character, at);

	[Fact]
	public async Task Player_Should_ReserveOneItemPerPriority_ForThemselvesOnly()
	{
		await CreateEventAsync();
		Assert.Empty(await UploadAsync("Nayra Solen", reserves: [Reserve("Nayra Solen", "high", 19019, 200), Reserve("Nayra Solen", "normal", 18814, 200)]));
		// Le même objet à une autre priorité, ou pour quelqu'un d'autre : refusé.
		Assert.Single(await UploadAsync("Nayra Solen", reserves: [Reserve("Nayra Solen", "low", 19019, 300)]));
		Assert.Single(await UploadAsync("Nayra Solen", reserves: [Reserve("Elaria Brume", "high", 17076, 300, "Nayra Solen")]));
		// Changer de choix : la plus récente gagne, l'ancienne n'écrase rien.
		await UploadAsync("Nayra Solen", reserves: [Reserve("Nayra Solen", "high", 17076, 400)]);
		await UploadAsync("Nayra Solen", reserves: [Reserve("Nayra Solen", "high", 19019, 350)]);

		var reserves = await _db.EventReserves.OrderBy(r => r.Priority).ToListAsync();
		Assert.Equal([("high", 17076), ("normal", 18814)], reserves.Select(r => (r.Priority, r.ItemId)));
	}

	[Fact]
	public async Task Lock_Should_FreezePlayers_ButNotOfficers_AndKeepOnlyConfirmed()
	{
		await CreateEventAsync();
		await UploadAsync("Nayra Solen", reserves: [Reserve("Nayra Solen", "high", 19019, 200)]);
		await _events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")],
			Placements: [new UploadPlacement(_eventId, null, "Thorgal Marteau", "confirmed", "Thorgal Marteau", 210)]));
		await UploadAsync("Thorgal Marteau", reserves: [Reserve("Thorgal Marteau", "high", 17076, 220)]);
		// L'organisateur verrouille.
		await UploadAsync("Thorgal Marteau", events: [new UploadEvent(null, _eventId, "Cœur du Magma", "raid", Tomorrow, null, "Thorgal Marteau", 1, 1, 0, 0, false, 300, Locked: true)]);

		Assert.Single(await UploadAsync("Nayra Solen", reserves: [Reserve("Nayra Solen", "low", 18814, 400)]));
		// Un officier pose encore une réservation pour un joueur sans addon.
		await _roster.PromoteOfficerAsync("3", "Elaria Brume");
		Assert.Empty(await UploadAsync("Elaria Brume", officer: true, reserves: [Reserve("Kael Fourbe", "high", 18814, 410, "Elaria Brume")]));

		var e = await _events.GetAsync(_eventId);
		Assert.True(e!.ReservesLocked);
		// Seul Thorgal est confirmé : sa réservation est la seule qui compte.
		Assert.Equal(["Thorgal Marteau"], EventService.EffectiveReserves(e).Select(r => r.Character));
	}

	[Fact]
	public async Task HardReserve_Should_BeManagersOnly_AndBlockSoftReserves()
	{
		await CreateEventAsync();
		await UploadAsync("Nayra Solen", reserves: [Reserve("Nayra Solen", "high", 19019, 200)]);
		Assert.Single(await UploadAsync("Nayra Solen", hard: [new UploadHardReserve(_eventId, null, 19019, "Serre-tonnerre", null, true, "Nayra Solen", 250)]));
		Assert.Empty(await UploadAsync("Thorgal Marteau", hard: [new UploadHardReserve(_eventId, null, 19019, "Serre-tonnerre", "Thorgal Marteau", true, "Thorgal Marteau", 260)]));

		Assert.Single(await UploadAsync("Elaria Brume", reserves: [Reserve("Elaria Brume", "high", 19019, 300)]));
		var e = await _events.GetAsync(_eventId);
		Assert.Empty(EventService.EffectiveReserves(e!)); // la réservation de Nayra ne compte plus
	}

	[Fact]
	public async Task Checklist_Should_FlagShortagesConflictsAndDuties()
	{
		await CreateEventAsync();
		await UploadAsync("Nayra Solen", reserves: [Reserve("Nayra Solen", "high", 19019, 200)]);
		await UploadAsync("Elaria Brume", reserves: [Reserve("Elaria Brume", "normal", 19019, 200)]);
		Assert.Single(await UploadAsync("Nayra Solen", duties: [new UploadDuty(_eventId, null, "summoner", "Nayra Solen", "Nayra Solen", 300)]));
		await UploadAsync("Thorgal Marteau", duties: [new UploadDuty(_eventId, null, "summoner", "Nayra Solen", "Thorgal Marteau", 300)]);

		var list = EventService.Checklist((await _events.GetAsync(_eventId))!, BotLocalizer.For(BotLanguage.French));
		var text = string.Join("\n", list.Select(i => i.Text));
		Assert.Contains("tank", text); // 0/1 tank confirmé
		Assert.Contains("soigneur", text);
		Assert.Contains("réservé par 2 joueurs", text); // conflit sur 19019
		Assert.Contains("n'est plus confirmé", text); // invocatrice non confirmée
		Assert.Contains("chef de raid", text);
	}

	[Fact]
	public async Task Discord_Should_OfferTheSameActions()
	{
		await CreateEventAsync();
		await _roster.LinkCharacterAsync("1", "orga", "Thorgal Marteau");
		await _roster.LinkCharacterAsync("2", "nayra", "Nayra Solen");

		Assert.True((await _events.ReserveFromDiscordAsync("2", false, _eventId, "high", 19019, null, null)).IsSuccess);
		Assert.False((await _events.ReserveFromDiscordAsync("2", false, _eventId, "high", 19019, null, "Thorgal Marteau")).IsSuccess);
		Assert.False((await _events.HardReserveFromDiscordAsync("2", false, _eventId, 19019, null, null, true)).IsSuccess);
		Assert.True((await _events.AssignDutyFromDiscordAsync("1", false, _eventId, "lead", "thorgal marteau")).IsSuccess);
		Assert.True((await _events.EditFromDiscordAsync("1", false, _eventId, new EventService.EventFields(Locked: true))).IsSuccess);
		Assert.False((await _events.ReserveFromDiscordAsync("2", false, _eventId, "low", 18814, null, null)).IsSuccess);

		var e = (await _events.GetAsync(_eventId))!;
		Assert.Equal("Thorgal Marteau", e.Duties.Single().Character);
		Assert.Equal(19019, e.Reserves.Single().ItemId);
	}

	[Theory]
	[InlineData("19019", 19019)]
	[InlineData("https://www.wowhead.com/classic/item=19019/thunderfury", 19019)]
	[InlineData("|cffff8000|Hitem:19019::::::::60:::::|h[Thunderfury]|h|r", 19019)]
	[InlineData("Serre-tonnerre", null)]
	public void ParseItemId_Should_ReadNumbersAndLinks(string text, int? expected) =>
		Assert.Equal(expected, EventService.ParseItemId(text));

	[Fact]
	public async Task Export_Should_CarryReservesHardReservesAndDuties()
	{
		await CreateEventAsync();
		await UploadAsync("Nayra Solen", reserves: [Reserve("Nayra Solen", "high", 19019, 200)]);
		await UploadAsync("Thorgal Marteau", hard: [new UploadHardReserve(_eventId, null, 17076, "Bonereaver", null, true, "Thorgal Marteau", 260)],
			duties: [new UploadDuty(_eventId, null, "repair", "Thorgal Marteau", "Thorgal Marteau", 270)]);

		var exported = Assert.Single(await _events.BuildExportAsync());
		Assert.Equal(19019, Assert.Single(exported.Reserves!).ItemId);
		Assert.Equal(17076, Assert.Single(exported.HardReserves!).ItemId);
		Assert.Equal("repair", Assert.Single(exported.Duties!).Duty);
	}
}
