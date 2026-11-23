using GuildProfessions.Server.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// Le modèle est maintenant déclaré module par module : il doit rester celui
/// de l'historique des migrations, et une base existante doit monter de
/// version sans perdre de données.
/// </summary>
public sealed class MigrationTests : IDisposable
{
	private readonly SqliteConnection _connection = new("Data Source=:memory:");

	public MigrationTests() => _connection.Open();

	public void Dispose() => _connection.Dispose();

	private AppDbContext NewContext(GuildContext? guild = null) =>
		new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, guild);

	[Fact]
	public void Model_Should_MatchTheMigrationHistory()
	{
		using var db = NewContext();
		Assert.False(db.Database.HasPendingModelChanges());
	}

	[Fact]
	public async Task ExistingDatabase_Should_KeepItsQuests_WhenMovedToTheQuestModule()
	{
		using (var db = NewContext())
		{
			// Base telle qu'en production avant la séparation des modules.
			await db.GetService<IMigrator>().MigrateAsync("20261123093830_CharacterQuests");
			await using (var command = _connection.CreateCommand())
			{
				command.CommandText = """
					INSERT INTO Guilds (Id, Name, Slug, ApiToken, InviteCode, SyncNicknames, Locale, DataVersion, CreatedUtc)
					VALUES (1, 'FORMER HOPE', 'former-hope', 'jeton', 'code', 0, 'fr', 7, '2026-11-01 00:00:00');
					INSERT INTO Characters (GuildId, Name, Source, IsOfficer, QuestsJson, QuestsUpdatedUtc)
					VALUES (1, 'Thorgal Marteau', 1, 0, '[{"Id":4903,"Title":"Avertir Thrall","Kind":"dungeon","Location":"Pic Rochenoire","Level":58,"Complete":false}]', '2026-11-20 20:00:00'),
					       (1, 'Nayra Solen', 1, 0, NULL, NULL);
					""";
				await command.ExecuteNonQueryAsync();
			}

			await db.Database.MigrateAsync();
		}

		var guild = new GuildContext();
		using var migrated = NewContext(guild);
		guild.Use(await migrated.Guilds.SingleAsync());
		var log = await migrated.CharacterQuestLogs.SingleAsync();
		Assert.Equal("Thorgal Marteau", log.Character);
		Assert.Contains("Avertir Thrall", log.QuestsJson);
		Assert.Equal(2, await migrated.Characters.CountAsync());
		Assert.Equal(7, guild.Required.DataVersion);
	}
}
