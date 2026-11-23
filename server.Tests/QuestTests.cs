using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>Lot 5 : quêtes partagées.</summary>
public sealed class QuestTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();
	private readonly TestModules _modules;
	private readonly RosterService _roster;
	private readonly QuestService _quests;

	public QuestTests()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		_db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _guild);
		_db.Database.EnsureCreated();
		_guild.Use(TestGuilds.Create(_db, "FORMER HOPE", "former-hope"));
		_modules = TestModules.For(_db, _guild);
		_roster = _modules.Roster;
		_quests = _modules.Quests;
	}

	public void Dispose()
	{
		_modules.Dispose();
		_db.Dispose();
		_connection.Dispose();
	}

	private static UploadQuest Q(int id, string title, string kind, string location, bool complete = false) =>
		new(id, title, kind, location, 58, complete);

	// Par l'ingestion complète : l'annuaire crée le personnage, le module Quêtes range son journal.
	private Task Upload(string name, List<UploadQuest>? quests) =>
		_modules.Ingestion.IngestAsync(null, new UploadPayload([new UploadCharacter(name, "MAGE", null, null, null, 60, [], Quests: quests)]),
			UploadSource.GuildToken);

	[Fact]
	public async Task Quests_Should_BeGroupedWithHolders_AndCleaned()
	{
		await Upload("Thorgal Marteau", [Q(4903, "Avertir Thrall", "dungeon", "Pic Rochenoire"), Q(5001, "Tueur de géants", "elite", "Maleterre", true),
			Q(1, "Quête normale", "normal", "Elwynn"), Q(4903, "Doublon", "dungeon", "Pic Rochenoire")]);
		await Upload("Nayra Solen", [Q(4903, "Avertir Thrall", "dungeon", "Pic Rochenoire")]);

		var all = await _quests.AllAsync();
		Assert.Equal(2, all.Count); // quête normale et doublon écartés
		var shared = all.First();
		Assert.Equal((4903, 2), (shared.Id, shared.Holders.Count));
		Assert.True(all.Single(q => q.Id == 5001).Holders.Single().Complete);
	}

	[Fact]
	public async Task NextScan_Should_ReplaceTheLog_ButMissingScanKeepsIt()
	{
		await Upload("Thorgal Marteau", [Q(4903, "Avertir Thrall", "dungeon", "Pic Rochenoire")]);
		await Upload("Thorgal Marteau", null); // envoi sans relevé de quêtes : on garde
		Assert.Single(await _quests.AllAsync());
		await Upload("Thorgal Marteau", []); // quête rendue : journal vide
		Assert.Empty(await _quests.AllAsync());
	}

	[Fact]
	public async Task ForCharacter_Should_ListOthersOnTheSameQuests()
	{
		await Upload("Thorgal Marteau", [Q(4903, "Avertir Thrall", "dungeon", "Pic Rochenoire"), Q(5001, "Tueur de géants", "elite", "Maleterre")]);
		await Upload("Nayra Solen", [Q(4903, "Avertir Thrall", "dungeon", "Pic Rochenoire")]);

		var (name, quests) = await _quests.ForCharacterAsync("thorgal marteau");
		Assert.Equal("Thorgal Marteau", name);
		Assert.Equal(2, quests.Count);
		Assert.Contains(quests.Single(q => q.Id == 4903).Holders, h => h.Character == "Nayra Solen");
	}

	[Fact]
	public async Task Export_Should_CarryEachCharactersQuests()
	{
		await Upload("Thorgal Marteau", [Q(4903, "Avertir Thrall", "dungeon", "Pic Rochenoire")]);
		var export = (await _modules.Export.BuildAsync()).Export;
		Assert.Equal(4903, Assert.Single(export.Characters.Single().Quests!).Id);
	}
}
