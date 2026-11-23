using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Evenements.Application;
using GuildProfessions.Server.Services;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>Lot 8 : tableau de bord assemblé à partir des cartes des modules, un module en panne n'empêchant pas les autres.</summary>
public sealed class HomeDashboardTests : IDisposable
{
	private static readonly long Tomorrow = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();

	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();

	public HomeDashboardTests()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		_db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _guild);
		_db.Database.EnsureCreated();
		_guild.Use(TestGuilds.Create(_db, "FORMER HOPE", "former-hope"));
	}

	public void Dispose()
	{
		_db.Dispose();
		_connection.Dispose();
	}

	/// <summary>Carte d'un module en panne.</summary>
	private sealed class BrokenCard : IHomeContributor
	{
		public string Module => "Casse";

		public int Order => 1;

		public Task<HomeCard?> BuildAsync(HomeViewer viewer, CancellationToken ct) => throw new InvalidOperationException("panne");
	}

	[Fact]
	public async Task Dashboard_Should_ShowEachModulesCard_InOrder()
	{
		using var modules = TestModules.For(_db, _guild);
		await modules.Roster.LinkCharacterAsync("1", "thorgal", "Thorgal Marteau");
		await modules.Events.CreateFromDiscordAsync("1", false, new EventService.EventFields("Cœur du Magma", "raid", Tomorrow), null);
		await modules.Communication.CreateGoalFromDiscordAsync("1", true, "Flacons", 200, "flacons", null, null);
		await modules.Profiles.EditFromDiscordAsync("1", ["tank"], "Soirs", null, null);

		var view = await modules.Home.BuildAsync(new HomeViewer("1", false));

		Assert.Empty(view.FailedModules);
		var events = view.Cards[0];
		Assert.Contains("Cœur du Magma", events.Lines[0]);
		Assert.Contains(view.Cards, c => c.Lines.Any(line => line.Contains("Flacons")));
		Assert.Contains(view.Cards, c => c.Lines.Any(line => line.Contains("**0**")));
	}

	[Fact]
	public async Task Dashboard_Should_SkipABrokenModule_AndKeepTheOthers()
	{
		using var modules = TestModules.For(_db, _guild, configure: s => s.AddHomeContributor<BrokenCard>());
		await modules.Roster.LinkCharacterAsync("1", "thorgal", "Thorgal Marteau");

		var view = await modules.Home.BuildAsync(new HomeViewer("1", false));

		Assert.Equal(["Casse"], view.FailedModules);
		Assert.NotEmpty(view.Cards);
	}
}
