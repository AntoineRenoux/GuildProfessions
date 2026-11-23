using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// Le point critique du multi-guildes : aucune donnée d'une guilde ne doit
/// être visible ni modifiable depuis une autre.
/// </summary>
public sealed class GuildIsolationTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly DbContextOptions<AppDbContext> _options;
	private readonly Guild _guildA;
	private readonly Guild _guildB;

	public GuildIsolationTests()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		_options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;
		using var setup = new AppDbContext(_options);
		setup.Database.EnsureCreated();
		_guildA = TestGuilds.Create(setup, "FORMER HOPE", "former-hope");
		_guildB = TestGuilds.Create(setup, "Les Autres", "les-autres");
	}

	public void Dispose() => _connection.Dispose();

	private (AppDbContext Db, RosterService Roster, CraftOrderService Orders, IDataVersion Version, IAnnuaire Annuaire) For(Guild guild)
	{
		var context = new GuildContext();
		context.Use(guild);
		var db = new AppDbContext(_options, context);
		var modules = TestModules.For(db, context);
		return (db, modules.Roster, modules.Orders, modules.DataVersion, modules.Get<IAnnuaire>());
	}

	private static UploadPayload Upload(string character, string guild, params int[] recipes) => new(
		[new UploadCharacter(character, "WARRIOR", null, null, null, 60,
			[new UploadProfession("Forge", 300, 300, "2026-11-22 12:00",
				recipes.Select(id => new UploadRecipe(id, "Recette " + id)).ToList())],
			guild)],
		[new UploadOrder("client-1", character, "Quelqu'un", "Lingot", 2, null, null)]);

	[Fact]
	public async Task Data_Should_StayInvisible_ToAnotherGuild()
	{
		var a = For(_guildA);
		await a.Roster.ApplyUploadAsync(null, Upload("Aldrin Lupercal", "FORMER HOPE", 101, 102));
		await a.Orders.ApplyUploadAsync(null, Upload("Aldrin Lupercal", "FORMER HOPE"));
		await a.Roster.LinkCharacterAsync("42", "anto", "Aldrin Lupercal");

		var b = For(_guildB);
		Assert.Empty(await b.Db.Characters.ToListAsync());
		Assert.Empty(await b.Db.Professions.ToListAsync());
		Assert.Empty(await b.Db.Recipes.ToListAsync());
		Assert.Empty(await b.Db.CraftOrders.ToListAsync());
		Assert.Empty(await b.Db.Members.ToListAsync());

		Assert.Single(await a.Db.Characters.ToListAsync());
		Assert.Equal(2, await a.Db.Recipes.CountAsync());
	}

	[Fact]
	public async Task SameCharacterName_Should_BeAllowed_InTwoGuilds()
	{
		await For(_guildA).Roster.ApplyUploadAsync(null, Upload("Mira Solen", "FORMER HOPE"));
		await For(_guildB).Roster.ApplyUploadAsync(null, Upload("Mira Solen", "Les Autres"));

		using var system = new AppDbContext(_options);
		Assert.Equal(2, await system.Characters.IgnoreQueryFilters().CountAsync(c => c.Name == "Mira Solen"));
	}

	[Fact]
	public async Task SameDiscordUser_Should_HaveOneMemberPerGuild()
	{
		await For(_guildA).Roster.LinkCharacterAsync("42", "anto", "Aldrin Lupercal");
		await For(_guildB).Roster.LinkCharacterAsync("42", "anto", "Mira Solen");

		Assert.Equal("Aldrin Lupercal", await For(_guildA).Roster.GetMainCharacterNameAsync("42"));
		Assert.Equal("Mira Solen", await For(_guildB).Roster.GetMainCharacterNameAsync("42"));
	}

	[Fact]
	public async Task DataVersion_Should_BeCountedPerGuild()
	{
		await For(_guildA).Roster.ApplyUploadAsync(null, Upload("Aldrin Lupercal", "FORMER HOPE"));
		await For(_guildA).Roster.ApplyUploadAsync(null, Upload("Aldrin Lupercal", "FORMER HOPE"));

		Assert.Equal(2, await For(_guildA).Version.CurrentAsync());
		Assert.Equal(0, await For(_guildB).Version.CurrentAsync());
	}

	[Fact]
	public async Task Writing_Should_Fail_WithoutCurrentGuild()
	{
		using var db = new AppDbContext(_options, new GuildContext());
		db.Characters.Add(new Character { Name = "Orphelin", Source = DataSource.Addon });

		await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
	}

	[Fact]
	public async Task Modifying_Should_Fail_ForAnotherGuildsData()
	{
		await For(_guildA).Roster.ApplyUploadAsync(null, Upload("Aldrin Lupercal", "FORMER HOPE"));

		var b = For(_guildB);
		var foreign = await b.Db.Characters.IgnoreQueryFilters().SingleAsync();
		foreign.Level = 1;

		await Assert.ThrowsAsync<InvalidOperationException>(() => b.Db.SaveChangesAsync());
	}

	[Fact]
	public async Task NoCurrentGuild_Should_SeeNothing()
	{
		await For(_guildA).Roster.ApplyUploadAsync(null, Upload("Aldrin Lupercal", "FORMER HOPE"));

		using var db = new AppDbContext(_options, new GuildContext());
		Assert.Empty(await db.Characters.ToListAsync());
	}

	[Fact]
	public async Task Ingestion_Should_RemoveCharacter_ThatMovedToAnotherGuild()
	{
		var a = For(_guildA);
		await a.Roster.ApplyUploadAsync(null, Upload("Mira Solen", "FORMER HOPE"));
		await a.Roster.LinkCharacterAsync("42", "anto", "Mira Solen");

		// Le compagnon signale que Mira est maintenant relevée dans une autre guilde.
		var context = new GuildContext();
		context.Use(_guildA);
		using var db = new AppDbContext(_options, context);
		using var modules = TestModules.For(db, context);
		var roster = modules.Roster;
		var memberId = (await roster.GetOrCreateMemberAsync("42", "anto")).Id;
		var outcome = await modules.Ingestion.IngestAsync(memberId, new UploadPayload([], DepartedCharacters: ["Mira Solen"]), UploadSource.PersonalToken);

		Assert.Empty(await db.Characters.ToListAsync());
		Assert.Null(await roster.GetMainCharacterNameAsync("42"));
		Assert.Contains(outcome.Result.Warnings, w => w.Contains("autre guilde"));
	}

	[Theory]
	[InlineData(UploadSource.DiscordImport)]
	[InlineData(UploadSource.GuildToken)]
	[InlineData(UploadSource.PersonalToken)]
	public async Task Ingestion_Should_NotRemove_AnotherMembersCharacter(UploadSource source)
	{
		var a = For(_guildA);
		await a.Roster.ApplyUploadAsync(null, Upload("Mira Solen", "FORMER HOPE"));
		await a.Roster.LinkCharacterAsync("42", "anto", "Mira Solen");

		// Un autre membre (sans token, par /gkeep import, ou avec le pack partagé) signale Mira comme partie.
		var context = new GuildContext();
		context.Use(_guildA);
		using var db = new AppDbContext(_options, context);
		using var modules = TestModules.For(db, context);
		var intruder = source == UploadSource.GuildToken ? (int?)null : (await modules.Roster.GetOrCreateMemberAsync("7", "intrus")).Id;
		await modules.Ingestion.IngestAsync(intruder, new UploadPayload([], DepartedCharacters: ["Mira Solen"]), source);

		Assert.Equal("Mira Solen", (await db.Characters.SingleAsync()).Name);
		Assert.Equal("Mira Solen", await modules.Roster.GetMainCharacterNameAsync("42"));
	}

	[Fact]
	public async Task Directory_Should_FindGuild_ByEachSecret_AndMakeSlugsUnique()
	{
		using var db = new AppDbContext(_options);
		var directory = new GuildDirectory(db);
		var created = await directory.CreateAsync("Former Hope", "123", "42", false, null);

		Assert.Equal("former-hope-2", created.Slug); // « former-hope » existe déjà
		Assert.Equal(created.Id, (await directory.FindByApiTokenAsync(created.ApiToken))!.Id);
		Assert.Equal(created.Id, (await directory.FindByInviteCodeAsync(created.InviteCode))!.Id);
		Assert.Equal(created.Id, (await directory.FindByDiscordServerAsync(123))!.Id);
		Assert.Null(await directory.FindByApiTokenAsync(""));

		var oldToken = created.ApiToken;
		await directory.RegenerateSecretsAsync(created);
		Assert.Null(await directory.FindByApiTokenAsync(oldToken));
	}

	[Fact]
	public async Task Directory_Should_FindGuild_FromPersonalUploadToken()
	{
		var token = await For(_guildB).Roster.GetOrCreateUploadTokenAsync("42", "anto");

		// Lu depuis une autre guilde courante : le propriétaire du token est trouvé toutes guildes confondues.
		var owner = await For(_guildA).Annuaire.UploadTokenOwnerAsync(token);

		Assert.Equal(_guildB.Id, owner!.GuildId);
		Assert.Null(await For(_guildA).Annuaire.UploadTokenOwnerAsync("inconnu"));
	}

	[Fact]
	public async Task DeletingGuild_Should_RemoveAllItsData()
	{
		await For(_guildA).Roster.ApplyUploadAsync(null, Upload("Aldrin Lupercal", "FORMER HOPE", 101));
		await For(_guildB).Roster.ApplyUploadAsync(null, Upload("Mira Solen", "Les Autres", 102));

		using (var db = new AppDbContext(_options))
		{
			await new GuildDirectory(db).DeleteAsync(await db.Guilds.SingleAsync(g => g.Id == _guildA.Id));
		}

		using var system = new AppDbContext(_options);
		Assert.Equal(["Mira Solen"], await system.Characters.IgnoreQueryFilters().Select(c => c.Name).ToListAsync());
		Assert.Equal(1, await system.Recipes.IgnoreQueryFilters().CountAsync());
	}

	[Theory]
	[InlineData("FORMER HOPE", "former-hope")]
	[InlineData("  Les Épées d'Argent ", "les-epees-d-argent")]
	[InlineData("!!!", "guilde")]
	public void Slugify_Should_ProduceReadableIds(string name, string expected) =>
		Assert.Equal(expected, GuildDirectory.Slugify(name));
}
