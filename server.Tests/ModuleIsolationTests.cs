using System.Text.Json;
using GuildProfessions.Server.Api;
using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Evenements.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// Un module en panne ne doit pas emporter les autres : ni à l'envoi (ses
/// écritures sont annulées, les autres modules sont appliqués), ni à
/// l'export (sa section est absente, le reste est servi).
/// </summary>
public sealed class ModuleIsolationTests : IDisposable
{
	private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();

	public ModuleIsolationTests()
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

	private static UploadCharacter Char(string name) =>
		new(name, "WARRIOR", null, null, null, 60, [new UploadProfession("Forge", 300, 300, null, [new UploadRecipe(9999, "Marteau")])]);

	private static readonly Dictionary<string, int> Raiders = new()
	{
		["Thorgal Marteau"] = 120, ["Nayra Solen"] = 120, ["Elaria Brume"] = 120, ["Kael Fourbe"] = 120, ["Mira Vent"] = 120,
	};

	private static UploadPayload FullUpload() => new(
		[Char("Thorgal Marteau")],
		Orders: [new UploadOrder("o-1", "Thorgal Marteau", null, "Flacon des Titans", 1, null, null)],
		Events: [new UploadEvent("e-1", null, "Cœur du Magma", "raid", Now + 86400, null, "Thorgal Marteau", 0, 0, 0, 0, false, 1)],
		Raids: [new UploadRaid("k1", "Cœur du Magma", DateTimeOffset.FromUnixTimeSeconds(Now - 3600).ToString("yyyy-MM-dd"), "Thorgal Marteau",
			Now - 3600, Now, "Thorgal Marteau", Raiders)]);

	/// <summary>Module factice : il écrit dans la base puis échoue, comme un module bogué.</summary>
	private sealed class FailingUploadContributor(AppDbContext db) : IUploadContributor
	{
		public static bool NotificationSent;

		public string Module => "Raid factice";

		public int Order => 25;

		public async Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct)
		{
			db.Events.Add(new GuildEvent { Title = "Fantôme", Organizer = "Personne", CreatedUtc = DateTime.UtcNow });
			await db.SaveChangesAsync(ct);
			afterCommit.Add(() =>
			{
				NotificationSent = true;
				return Task.CompletedTask;
			});
			throw new InvalidOperationException("Panne simulée du module.");
		}
	}

	private sealed class FailingExportContributor : IExportContributor
	{
		public string Module => "Raid factice";

		public int Order => 25;

		public Task ContributeAsync(ExportDraft draft, CancellationToken ct) => throw new InvalidOperationException("Panne simulée de l'export.");
	}

	[Fact]
	public async Task FailingModule_Should_BeRolledBack_WhileOtherModulesAreApplied()
	{
		using var modules = TestModules.For(_db, _guild, configure: s => s.AddUploadContributor<FailingUploadContributor>());

		var outcome = await modules.Ingestion.IngestAsync(null, FullUpload(), UploadSource.GuildToken);
		var result = outcome.Result;

		// Annuaire, artisanat, événements et présence sont appliqués…
		Assert.Equal("Thorgal Marteau", (await _db.Characters.SingleAsync()).Name);
		Assert.Single(await _db.CraftOrders.ToListAsync());
		Assert.Equal("Cœur du Magma", (await _db.Events.SingleAsync()).Title);
		Assert.Single(await _db.RaidSessions.ToListAsync());
		// … l'écriture du module en panne est annulée, sa notification n'est pas partie…
		Assert.DoesNotContain(await _db.Events.ToListAsync(), e => e.Title == "Fantôme");
		Assert.False(FailingUploadContributor.NotificationSent);
		// … et la réponse garde sa forme, avec un avertissement.
		Assert.True(result.Applied);
		Assert.Equal(await modules.DataVersion.CurrentAsync(), result.Version);
		Assert.Contains(result.Warnings, w => w.Contains("Raid factice") && w.Contains("indisponible"));
		// … mais l'envoi est signalé incomplet (HTTP 503) : le compagnon le renverra.
		Assert.False(outcome.Complete);
		Assert.Equal(["Raid factice"], outcome.FailedModules);
	}

	[Fact]
	public async Task RaidWithNullFields_Should_BeIgnored_WithoutBreakingTheUpload()
	{
		// System.Text.Json accepte null dans un champ non nullable : c'était une NRE et un 500.
		const string json = """
			{ "characters": [{ "name": "Thorgal Marteau", "professions": [] }],
			  "orders": [{ "clientId": "o-1", "requester": "Thorgal Marteau", "item": "Flacon", "quantity": 1 }],
			  "raids": [{ "key": "k1", "instance": "MC", "day": "2026-11-20", "startedAt": 1, "endedAt": 2, "observer": "Thorgal Marteau", "participants": null },
			            { "key": null, "instance": "MC", "day": "2026-11-20", "startedAt": 1, "endedAt": 2, "observer": "Thorgal Marteau", "participants": { "Thorgal Marteau": 90 } }] }
			""";
		var payload = JsonSerializer.Deserialize<UploadPayload>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
		using var modules = TestModules.For(_db, _guild);

		var result = (await modules.Ingestion.IngestAsync(null, payload, UploadSource.GuildToken)).Result;

		Assert.Single(await _db.CraftOrders.ToListAsync());
		Assert.Empty(await _db.RaidSessions.ToListAsync());
		Assert.Equal(2, result.Warnings.Count(w => w.Contains("incomplet")));
		Assert.DoesNotContain(result.Warnings, w => w.Contains("indisponible"));
	}

	[Fact]
	public async Task DuplicateCharacter_InUpload_Should_NotFailTheUpload()
	{
		using var modules = TestModules.For(_db, _guild);

		var outcome = await modules.Ingestion.IngestAsync(null, new UploadPayload([Char("Thorgal Marteau"), Char("Thorgal Marteau")]), UploadSource.GuildToken);
		var result = outcome.Result;
		Assert.True(outcome.Complete);

		Assert.Single(await _db.Characters.ToListAsync());
		Assert.DoesNotContain(result.Warnings, w => w.Contains("indisponible"));
	}

	[Fact]
	public async Task FailingExportSection_Should_LeaveTheOtherSections()
	{
		using var modules = TestModules.For(_db, _guild, configure: s => s.AddExportContributor<FailingExportContributor>());
		await modules.Ingestion.IngestAsync(null, FullUpload(), UploadSource.GuildToken);

		var build = await modules.Export.BuildAsync();
		var export = build.Export;

		// Les autres sections sont construites…
		Assert.Equal("Thorgal Marteau", Assert.Single(export.Characters!).Name);
		Assert.Single(export.Orders!);
		Assert.Single(export.Events!);
		Assert.Single(export.Raids!);
		Assert.Equal("Marteau", export.RecipeNames![9999]);
		// … et le module en échec est signalé (en-tête de l'API, message Discord).
		Assert.False(build.Complete);
		Assert.Equal(["Raid factice"], build.FailedModules);
	}

	/// <summary>Module qui remplit une section puis échoue : sa part ne doit pas rester à moitié dans l'export.</summary>
	private sealed class HalfwayExportContributor : IExportContributor
	{
		public string Module => "Artisanat bogué";

		public int Order => 15;

		public Task ContributeAsync(ExportDraft draft, CancellationToken ct)
		{
			draft.Orders = [];
			draft.Catalog = [];
			throw new InvalidOperationException("Panne simulée à mi-chemin.");
		}
	}

	[Fact]
	public async Task FailingExportSection_Should_BeAbsent_EvenIfHalfFilled()
	{
		using var modules = TestModules.For(_db, _guild, configure: s => s.AddExportContributor<HalfwayExportContributor>());
		await modules.Ingestion.IngestAsync(null, FullUpload(), UploadSource.GuildToken);

		var export = (await modules.Export.BuildAsync()).Export;

		// La part annulée est celle du module bogué ; ce que l'Artisanat avait fourni avant lui reste.
		Assert.Single(export.Orders!);
		Assert.Single(export.Raids!);
	}

	[Fact]
	public async Task BrokenPresence_Should_StillServeTheCraftOrders_With200()
	{
		using var modules = TestModules.For(_db, _guild);
		await modules.Ingestion.IngestAsync(null, FullUpload(), UploadSource.GuildToken);
		// Panne réelle du module Présence : sa table ne répond plus.
		await _db.Database.ExecuteSqlRawAsync("ALTER TABLE RaidSessions RENAME TO RaidSessions_EnPanne");

		var build = await modules.Export.BuildAsync();
		var context = new DefaultHttpContext
		{
			RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
			Response = { Body = new MemoryStream() },
		};
		await Endpoints.ExportResult(build, context.Response).ExecuteAsync(context);

		// 200 avec l'export partiel : l'artisanat et les autres modules arrivent en jeu…
		Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
		Assert.Equal(["Présence et butin"], build.FailedModules);
		Assert.Equal(Uri.EscapeDataString("Présence et butin"), context.Response.Headers[Endpoints.FailedModulesHeader].ToString());
		context.Response.Body.Position = 0;
		using var json = await JsonDocument.ParseAsync(context.Response.Body);
		var root = json.RootElement;
		var order = Assert.Single(root.GetProperty("orders").EnumerateArray().ToList());
		Assert.Equal("Flacon des Titans", order.GetProperty("item").GetString());
		Assert.Equal("Thorgal Marteau", Assert.Single(root.GetProperty("characters").EnumerateArray().ToList()).GetProperty("name").GetString());
		Assert.Equal(1, root.GetProperty("events").GetArrayLength());
		// … seule la section Présence est absente (null), même forme JSON qu'un export complet.
		Assert.Equal(JsonValueKind.Null, root.GetProperty("raids").ValueKind);
	}

	[Fact]
	public async Task CompleteExport_Should_HaveNoFailedModulesHeader()
	{
		using var modules = TestModules.For(_db, _guild);
		await modules.Ingestion.IngestAsync(null, FullUpload(), UploadSource.GuildToken);

		var build = await modules.Export.BuildAsync();
		var response = new DefaultHttpContext().Response;
		Endpoints.ExportResult(build, response);

		Assert.True(build.Complete);
		Assert.False(response.Headers.ContainsKey(Endpoints.FailedModulesHeader));
		Assert.NotNull(build.Export.Raids);
	}

	[Fact]
	public async Task RepeatedFailingModule_Should_NotBlockNorDuplicate_TheOthers_OnEachResend()
	{
		using var modules = TestModules.For(_db, _guild, configure: s => s.AddUploadContributor<FailingUploadContributor>());

		// Le compagnon renvoie l'envoi tant qu'il reçoit 503 : trois cycles avec le même module en panne.
		for (var attempt = 0; attempt < 3; attempt++)
		{
			var outcome = await modules.Ingestion.IngestAsync(null, FullUpload(), UploadSource.GuildToken);
			Assert.Equal(["Raid factice"], outcome.FailedModules);
		}

		// Les autres modules sont appliqués, une seule fois.
		Assert.Single(await _db.Characters.ToListAsync());
		Assert.Single(await _db.CraftOrders.ToListAsync());
		Assert.Equal("Cœur du Magma", (await _db.Events.SingleAsync()).Title);
		Assert.Single(await _db.RaidSessions.ToListAsync());
		Assert.Equal(5, (await _db.RaidParticipants.ToListAsync()).Count);
	}

	[Fact]
	public async Task Export_Should_NotWrite_AndShowStaleRequestsAsExpired()
	{
		using var modules = TestModules.For(_db, _guild);
		await modules.Ingestion.IngestAsync(null, FullUpload(), UploadSource.GuildToken);
		var order = await _db.CraftOrders.SingleAsync();
		order.ExpiresUtc = DateTime.UtcNow.AddDays(-1);
		await _db.SaveChangesAsync();
		var version = await modules.DataVersion.CurrentAsync();

		var export = (await modules.Export.BuildAsync()).Export;

		Assert.Equal("expired", Assert.Single(export.Orders!).Status);
		_db.ChangeTracker.Clear();
		Assert.Equal(CraftOrderStatus.Open, (await _db.CraftOrders.SingleAsync()).Status);
		Assert.Equal(version, await modules.DataVersion.CurrentAsync());
	}

	[Fact]
	public async Task DataVersion_Should_CountEveryBump_FromConcurrentContexts()
	{
		// Deux traitements simultanés, chacun avec son contexte : aucun incrément ne se perd.
		using var otherDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _guild);
		var first = new DataVersion(_db, _guild);
		var second = new DataVersion(otherDb, _guild);
		var before = await first.CurrentAsync();

		await Task.WhenAll(first.BumpAsync(), second.BumpAsync());

		Assert.Equal(before + 2, await first.CurrentAsync());
	}

	[Fact]
	public async Task RaidSession_Should_LinkOnlyToRaidEvents()
	{
		using var modules = TestModules.For(_db, _guild);
		var started = Now - 3600;
		await modules.Events.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")],
			Events: [new UploadEvent("pvp", null, "Goulet", "pvp", started - 1800, null, "Thorgal Marteau", 0, 0, 0, 0, false, 1)]));

		await modules.Attendance.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")],
			Raids: [new UploadRaid("k1", "MC", DateTimeOffset.FromUnixTimeSeconds(started).ToString("yyyy-MM-dd"), null, started, Now, "Thorgal Marteau", Raiders)]));

		Assert.Null((await _db.RaidSessions.SingleAsync()).EventId);
	}

	/// <summary>Contrat du module Événements en panne (requête cassée, table en défaut).</summary>
	private sealed class BrokenEvenements : IEvenements
	{
		public Task<int?> EventOfRaidAsync(long startedAt, TimeSpan window, IReadOnlyCollection<string> participants, CancellationToken ct = default) =>
			throw new InvalidOperationException("Panne simulée du module Événements.");

		public Task<EventBrief?> EventBriefAsync(int eventId, CancellationToken ct = default) =>
			throw new InvalidOperationException("Panne simulée du module Événements.");

		public Task<IReadOnlyList<EventBrief>> UpcomingBriefsAsync(int max, CancellationToken ct = default) =>
			throw new InvalidOperationException("Panne simulée du module Événements.");
	}

	[Fact]
	public async Task BrokenEventsContract_Should_NotLoseTheAttendance()
	{
		using var modules = TestModules.For(_db, _guild, configure: s => s.AddScoped<IEvenements, BrokenEvenements>());

		var outcome = await modules.Ingestion.IngestAsync(null, FullUpload(), UploadSource.GuildToken);

		// Le rattachement à l'événement est facultatif : le raid est enregistré sans lui.
		var session = await _db.RaidSessions.SingleAsync();
		Assert.Null(session.EventId);
		Assert.Equal(5, (await _db.RaidParticipants.ToListAsync()).Count);
		Assert.True(outcome.Complete);
	}

	/// <summary>Contributeur dont la construction échoue (enregistrement DI manquant, constructeur qui lève).</summary>
	private sealed class UnbuildableContributor : IUploadContributor, IExportContributor
	{
		public UnbuildableContributor() => throw new InvalidOperationException("Construction impossible.");

		public string Module => "Introuvable";

		public int Order => 5;

		public Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct) =>
			Task.FromResult<IReadOnlyList<string>>([]);

		public Task ContributeAsync(ExportDraft draft, CancellationToken ct) => Task.CompletedTask;
	}

	[Fact]
	public async Task UnbuildableContributor_Should_NotBlockTheOtherModules()
	{
		using var modules = TestModules.For(_db, _guild, configure: s => s
			.AddUploadContributor<UnbuildableContributor>()
			.AddExportContributor<UnbuildableContributor>());

		var outcome = await modules.Ingestion.IngestAsync(null, FullUpload(), UploadSource.GuildToken);
		var build = await modules.Export.BuildAsync();

		Assert.Single(await _db.CraftOrders.ToListAsync());
		Assert.Equal([nameof(UnbuildableContributor)], outcome.FailedModules);
		Assert.Single(build.Export.Orders!);
		Assert.False(build.Complete);
	}

	[Fact]
	public void EveryModuleContributor_Should_BeRegistered()
	{
		using var modules = TestModules.For(_db, _guild);
		var assembly = typeof(UploadIngestion).Assembly;
		var uploadTypes = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IUploadContributor).IsAssignableFrom(t)).ToHashSet();
		var exportTypes = assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IExportContributor).IsAssignableFrom(t)).ToHashSet();

		Assert.Equal(uploadTypes, modules.Get<IEnumerable<IUploadContributor>>().Select(c => c.GetType()).ToHashSet());
		Assert.Equal(exportTypes, modules.Get<IEnumerable<IExportContributor>>().Select(c => c.GetType()).ToHashSet());
		// Les orchestrateurs les résolvent par leur type, un par un.
		Assert.Equal(uploadTypes, modules.Get<IEnumerable<UploadContributorType>>().Select(c => c.Type).ToHashSet());
		Assert.Equal(exportTypes, modules.Get<IEnumerable<ExportContributorType>>().Select(c => c.Type).ToHashSet());
	}
}
