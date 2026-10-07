using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace GuildProfessions.Server.Tests;

public sealed class RosterServiceTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly RosterService _roster;

	public RosterServiceTests()
	{
		// SQLite en mémoire : le vrai provider (contraintes uniques comprises),
		// la connexion ouverte garde la base en vie le temps du test.
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite(_connection)
			.Options;
		_db = new AppDbContext(options);
		_db.Database.EnsureCreated();
		_roster = new RosterService(_db);
	}

	public void Dispose()
	{
		_db.Dispose();
		_connection.Dispose();
	}

	[Fact]
	public async Task SetProfessionFromDiscord_Should_CreateCharacterAndProfession_WhenUnknown()
	{
		var result = await _roster.SetProfessionFromDiscordAsync("42", "anto", "Thorgal", "Forge", 300, 300, "dispo mardi");

		Assert.True(result.Success);
		var profession = await _db.Professions.Include(p => p.Character).SingleAsync();
		Assert.Equal("Thorgal", profession.Character.Name);
		Assert.Equal("Forge", profession.Name);
		Assert.Equal(300, profession.SkillLevel);
		Assert.Equal("dispo mardi", profession.Note);
		Assert.Equal(DataSource.Discord, profession.Source);
	}

	[Fact]
	public async Task SetProfessionFromDiscord_Should_KeepScannedLevelButUpdateNote_WhenProfessionIsAddonSourced()
	{
		var member = await _roster.GetOrCreateMemberAsync("42", "anto");
		await _roster.ApplyUploadAsync(member, Payload("Thorgal", ("Forge", 287)));

		var result = await _roster.SetProfessionFromDiscordAsync("42", "anto", "Thorgal", "Forge", 150, 300, "note discord");

		Assert.True(result.Success);
		var profession = await _db.Professions.SingleAsync();
		Assert.Equal(287, profession.SkillLevel); // le scan prime, le 150 déclaré est ignoré
		Assert.Equal("note discord", profession.Note); // la note reste éditable via Discord
		Assert.Equal(DataSource.Addon, profession.Source);
	}

	[Fact]
	public async Task SetProfessionFromDiscord_Should_Refuse_WhenCharacterLinkedToAnotherMember()
	{
		await _roster.LinkCharacterAsync("42", "anto", "Thorgal");

		var result = await _roster.SetProfessionFromDiscordAsync("99", "intrus", "Thorgal", "Forge", 300, 300, null);

		Assert.False(result.Success);
		Assert.Empty(_db.Professions);
	}

	[Fact]
	public async Task ApplyUpload_Should_ReplaceProfessionsAndPreserveDiscordNote()
	{
		await _roster.SetProfessionFromDiscordAsync("42", "anto", "Elaria", "Couture", 200, 300, "sacs gratuits");
		await _roster.SetProfessionFromDiscordAsync("42", "anto", "Elaria", "Minage", 100, 300, null);
		var member = await _db.Members.SingleAsync();

		// Le scan ne remonte que Couture (Minage désappris), à un niveau plus frais.
		await _roster.ApplyUploadAsync(member, Payload("Elaria", ("Couture", 300)));

		var professions = await _db.Professions.ToListAsync();
		var couture = Assert.Single(professions);
		Assert.Equal("Couture", couture.Name);
		Assert.Equal(300, couture.SkillLevel);
		Assert.Equal("sacs gratuits", couture.Note); // la note Discord survit au scan
		Assert.Equal(DataSource.Addon, couture.Source);
	}

	[Fact]
	public async Task ApplyUpload_Should_UpdateLinkedCharacterWithoutClaiming_WhenSharedMode()
	{
		await _roster.LinkCharacterAsync("1", "proprietaire", "Thorgal");
		var linkedMemberId = (await _db.Characters.SingleAsync()).MemberId;

		// member null = upload partagé (token de guilde) : pas de contrôle de
		// propriété, pas de revendication.
		var result = await _roster.ApplyUploadAsync(null, Payload("Thorgal", ("Forge", 290)));

		Assert.True(result.Applied);
		Assert.Empty(result.Warnings);
		var character = await _db.Characters.Include(c => c.Professions).SingleAsync();
		Assert.Equal(linkedMemberId, character.MemberId); // le lien existant est conservé
		Assert.Equal(290, character.Professions.Single().SkillLevel);
	}

	[Fact]
	public async Task ApplyUpload_Should_SkipCharacter_WhenLinkedToAnotherMember()
	{
		await _roster.LinkCharacterAsync("1", "proprietaire", "Thorgal");
		var intrus = await _roster.GetOrCreateMemberAsync("2", "intrus");

		var result = await _roster.ApplyUploadAsync(intrus, Payload("Thorgal", ("Forge", 300)));

		Assert.Single(result.Warnings);
		Assert.Empty(_db.Professions);
	}

	[Fact]
	public async Task ApplyUpload_Should_StoreRecipes_AndBumpVersion()
	{
		var member = await _roster.GetOrCreateMemberAsync("42", "anto");
		var before = await _roster.GetDataVersionAsync();

		var payload = new UploadPayload([
			new UploadCharacter("Grimbart", "PALADIN", "Human", 1, 2, 60, [
				new UploadProfession("Alchimie", 295, 300, "2026-10-07 12:00", [
					new UploadRecipe(17187, "Transmutation : arcanite"),
					new UploadRecipe(17635, "Flacon des Titans"),
				]),
			]),
		]);
		var result = await _roster.ApplyUploadAsync(member, payload);

		Assert.True(result.Applied);
		Assert.Equal(before + 1, result.Version);
		var recipes = await _db.Recipes.OrderBy(r => r.SpellId).ToListAsync();
		Assert.Equal(2, recipes.Count);
		Assert.Equal("Transmutation : arcanite", recipes[0].Name);
	}

	[Fact]
	public async Task ExportBuilder_Should_ExposeRecipeNamesAndSources()
	{
		var member = await _roster.GetOrCreateMemberAsync("42", "anto");
		await _roster.ApplyUploadAsync(member, new UploadPayload([
			new UploadCharacter("Grimbart", "PALADIN", null, null, null, 60, [
				new UploadProfession("Alchimie", 295, 300, null, [new UploadRecipe(17635, "Flacon des Titans")]),
			]),
		]));
		await _roster.SetProfessionFromDiscordAsync("42", "anto", "Nayra", "Couture", 240, 300, null);

		var export = await new ExportBuilder(_db, _roster, new ConfigurationBuilder().Build()).BuildAsync();

		Assert.Equal(2, export.Characters.Count);
		Assert.Equal("Flacon des Titans", export.RecipeNames[17635]);
		var grimbart = export.Characters.Single(c => c.Name == "Grimbart");
		Assert.Equal("addon", grimbart.Professions.Single().Source);
		var nayra = export.Characters.Single(c => c.Name == "Nayra");
		Assert.Equal("discord", nayra.Professions.Single().Source);
	}

	#region Helper Methods

	private static UploadPayload Payload(string characterName, params (string Name, int Level)[] professions)
	{
		return new UploadPayload([
			new UploadCharacter(characterName, null, null, null, null, null,
				professions.Select(p => new UploadProfession(p.Name, p.Level, 300, null, null)).ToList()),
		]);
	}

	#endregion
}
