using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Communication.Application;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>Lot 6 : annonces et modèles, charte, objectifs et contributions.</summary>
public sealed class CommunicationTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();
	private readonly TestModules _modules;
	private readonly RecordingPublisher _publisher = new();
	private readonly RosterService _roster;
	private readonly CommunicationService _communication;

	public CommunicationTests()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		_db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _guild);
		_db.Database.EnsureCreated();
		_guild.Use(TestGuilds.Create(_db, "FORMER HOPE", "former-hope"));
		_modules = TestModules.For(_db, _guild, configure: s => s.AddSingleton<IAnnouncementPublisher>(_publisher));
		_roster = _modules.Roster;
		_communication = _modules.Communication;
	}

	public void Dispose()
	{
		_modules.Dispose();
		_db.Dispose();
		_connection.Dispose();
	}

	private static UploadPayload Payload(string by, UploadCommunication communication) =>
		new([new UploadCharacter(by, "MAGE", null, null, null, 60, [])], Communication: communication);

	private async Task<Member> OfficerAsync(string discordId, string character)
	{
		await _roster.PromoteOfficerAsync(discordId, character);
		return await _db.Members.SingleAsync(m => m.DiscordId == discordId);
	}

	[Fact]
	public async Task Announcements_Should_BeOfficersOnly_AndPublishedOnce()
	{
		await _communication.SetChannelFromDiscordAsync(true, "555");
		// Un simple membre (token de guilde) ne publie rien.
		var refused = await _communication.ApplyUploadAsync(null, Payload("Nayra Solen", new(Announcements:
			[new UploadAnnouncement("a-1", null, "Piraté", "Texte", null, "Nayra Solen", false, 100)])));
		Assert.Single(refused);
		Assert.Empty(await _db.Announcements.ToListAsync());

		var officer = await OfficerAsync("1", "Thorgal Marteau");
		var announcement = new UploadAnnouncement("a-2", null, "Raid ce soir", "Rendez-vous à 20 h 30", null, "Thorgal Marteau", false, 200);
		Assert.Empty(await _communication.ApplyUploadAsync(officer, Payload("Thorgal Marteau", new(Announcements: [announcement]))));
		await _communication.ApplyUploadAsync(officer, Payload("Thorgal Marteau", new(Announcements: [announcement]))); // renvoyée

		Assert.Single(await _db.Announcements.ToListAsync());
		Assert.Equal(["555|Raid ce soir"], _publisher.Published);

		// Une suppression plus récente gagne ; une modification plus ancienne n'y change rien.
		await _communication.ApplyUploadAsync(officer, Payload("Thorgal Marteau", new(Announcements:
			[announcement with { Deleted = true, At = 300 }, announcement with { Title = "Ancien", At = 250 }])));
		Assert.True((await _db.Announcements.SingleAsync()).Deleted);
		Assert.Empty((await _communication.BuildExportAsync()).Announcements);
	}

	[Fact]
	public async Task Handbook_And_Templates_Should_KeepTheMostRecentOfficerEdit()
	{
		var officer = await OfficerAsync("1", "Thorgal Marteau");
		await _communication.ApplyUploadAsync(officer, Payload("Thorgal Marteau", new(
			Templates: [new UploadTemplate("Raid", "Raid {event}", "Le {date}", "Thorgal Marteau", false, 100)],
			Handbook: [new UploadHandbookSection("Butin", "Règles de butin", "Réservations d'abord", 1, "Thorgal Marteau", false, 100),
				new UploadHandbookSection("butin", "Règles de butin", "Version plus ancienne", 1, "Thorgal Marteau", false, 50)])));

		var export = await _communication.BuildExportAsync();
		Assert.Equal("raid", Assert.Single(export.Templates).Key); // clé normalisée
		Assert.Equal("Réservations d'abord", Assert.Single(export.Handbook).Body);
	}

	[Fact]
	public async Task Contributions_Should_BeOwnCharactersOnly_Idempotent_AndRefusedOnClosedGoals()
	{
		var officer = await OfficerAsync("1", "Thorgal Marteau");
		await _communication.ApplyUploadAsync(officer, Payload("Thorgal Marteau", new(Goals:
			[new UploadGoal("g-1", null, "Flacons pour le Cœur", null, 200, "flacons", null, false, "Thorgal Marteau", 100)])));

		var mine = new UploadContribution("c-1", null, "g-1", "Nayra Solen", 20, "Titans", 150);
		await _communication.ApplyUploadAsync(null, Payload("Nayra Solen", new(Contributions: [mine, mine])));
		await _communication.ApplyUploadAsync(null, Payload("Nayra Solen", new(Contributions: [mine])));
		// Pour le personnage d'un autre : refusé.
		Assert.Single(await _communication.ApplyUploadAsync(null, Payload("Nayra Solen", new(Contributions:
			[new UploadContribution("c-2", null, "g-1", "Elaria Brume", 50, null, 160)]))));

		var goal = Assert.Single((await _communication.BuildExportAsync()).Goals);
		Assert.Equal((20, 1), (goal.Progress, goal.Contributions.Count));

		Assert.True((await _communication.SetGoalClosedFromDiscordAsync("1", false, goal.Id, true)).IsSuccess);
		Assert.Single(await _communication.ApplyUploadAsync(null, Payload("Nayra Solen", new(Contributions:
			[new UploadContribution("c-3", goal.Id, null, "Nayra Solen", 5, null, 200)]))));
	}

	[Fact]
	public async Task Discord_Should_OfferTheSameActions_WithTheSameRights()
	{
		await _roster.LinkCharacterAsync("2", "nayra", "Nayra Solen");
		await OfficerAsync("1", "Thorgal Marteau");

		Assert.False((await _communication.PostFromDiscordAsync("2", false, "Titre", "Texte", null, null)).IsSuccess);
		Assert.True((await _communication.SaveTemplateFromDiscordAsync("1", false, "raid", "Raid à {guild}", "Venez nombreux", false)).IsSuccess);
		var posted = await _communication.PostFromDiscordAsync("1", false, null, null, "raid", null);
		Assert.True(posted.IsSuccess, posted.Message);
		Assert.Equal("Raid à FORMER HOPE", (await _db.Announcements.SingleAsync()).Title);
		Assert.False((await _communication.EditHandbookFromDiscordAsync("2", false, "reglement", "Règlement", "Soyez sympas", null, false)).IsSuccess);
		Assert.True((await _communication.EditHandbookFromDiscordAsync("1", false, "reglement", "Règlement", "Soyez sympas", 0, false)).IsSuccess);
		Assert.False((await _communication.CreateGoalFromDiscordAsync("2", false, "Or", 1000, "po", null, null)).IsSuccess);
		Assert.True((await _communication.CreateGoalFromDiscordAsync("1", false, "Or", 1000, "po", null, null)).IsSuccess);
		var goalId = (await _db.GuildGoals.SingleAsync()).Id;
		Assert.True((await _communication.ContributeFromDiscordAsync("2", goalId, 250, null, null)).IsSuccess);
		Assert.False((await _communication.ContributeFromDiscordAsync("2", goalId, 10, null, "Thorgal Marteau")).IsSuccess);
		Assert.Equal(250, Assert.Single((await _communication.BuildExportAsync()).Goals).Progress);
		// Régler le salon : réservé à qui gère le serveur Discord.
		Assert.False((await _communication.SetChannelFromDiscordAsync(false, "555")).IsSuccess);
	}

	[Fact]
	public async Task Publication_Failure_Should_NotLoseTheAnnouncement()
	{
		_publisher.Fail = true;
		await _communication.SetChannelFromDiscordAsync(true, "555");
		await OfficerAsync("1", "Thorgal Marteau");

		var result = await _communication.PostFromDiscordAsync("1", false, "Titre", "Texte", null, null);

		Assert.True(result.IsSuccess);
		Assert.Single(await _db.Announcements.ToListAsync());
	}

	[Fact]
	public void Render_Should_ReplacePlaceholders()
	{
		var text = CommunicationService.Render("{event} · {date} · {guild}", new(12, "Onyxia", 1795000000, false), "FORMER HOPE");
		Assert.Equal("Onyxia · <t:1795000000:F> · FORMER HOPE", text);
	}

	[Fact]
	public async Task Ingestion_Should_CarryCommunication_IntoTheExport()
	{
		var officer = await OfficerAsync("1", "Thorgal Marteau");
		await _modules.Ingestion.IngestAsync(officer.Id, Payload("Thorgal Marteau", new(
			Handbook: [new UploadHandbookSection("reglement", "Règlement", "Respect", 0, "Thorgal Marteau", false, 100)])), UploadSource.PersonalToken);

		var export = await _modules.Export.BuildAsync();

		Assert.Equal("Respect", Assert.Single(export.Export.Communication!.Handbook).Body);
	}

	private sealed class RecordingPublisher : IAnnouncementPublisher
	{
		public List<string> Published { get; } = [];
		public bool Fail { get; set; }

		public Task PublishAsync(string channelId, string title, string body, string author, CancellationToken ct = default)
		{
			if (Fail)
			{
				throw new InvalidOperationException("Salon supprimé.");
			}
			Published.Add($"{channelId}|{title}");
			return Task.CompletedTask;
		}
	}
}
