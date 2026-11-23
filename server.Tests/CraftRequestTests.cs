using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>Lot 4 : demandes ouvertes, offres, livraison, expiration et catalogue.</summary>
public sealed class CraftRequestTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();
	private readonly TestModules _modules;
	private readonly RosterService _roster;
	private readonly CraftOrderService _orders;
	private readonly Recorder _notifier = new();

	public CraftRequestTests()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		_db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _guild);
		_db.Database.EnsureCreated();
		_guild.Use(TestGuilds.Create(_db, "FORMER HOPE", "former-hope"));
		_modules = TestModules.For(_db, _guild, _notifier);
		_roster = _modules.Roster;
		_orders = _modules.Orders;
	}

	public void Dispose()
	{
		_modules.Dispose();
		_db.Dispose();
		_connection.Dispose();
	}

	private static UploadCharacter Char(string name, params UploadProfession[] professions) => new(name, null, null, null, null, 60, [.. professions]);

	private async Task<int> OpenRequestAsync()
	{
		await _orders.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")],
			Orders: [new UploadOrder("n-1", "Nayra Solen", null, "Flacon des Titans", 2, "Composants fournis", null, 17635)]));
		return (await _db.CraftOrders.SingleAsync()).Id;
	}

	[Fact]
	public async Task OpenRequest_Should_ExpireIn5Days_AndPingCraftersWhoKnowTheRecipe()
	{
		await _roster.LinkCharacterAsync("20", "alchi", "Thorgal Marteau");
		await _roster.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau",
			new UploadProfession("Alchimie", 300, 300, null, [new UploadRecipe(17635, "Flacon des Titans")]))]));

		await OpenRequestAsync();

		var order = await _db.CraftOrders.SingleAsync();
		Assert.Null(order.CrafterCharacter);
		Assert.InRange(order.ExpiresUtc!.Value, DateTime.UtcNow.AddDays(4.9), DateTime.UtcNow.AddDays(5.1));
		Assert.Equal(["20"], _notifier.OpenRequestCrafters);
	}

	[Fact]
	public async Task Offers_Should_LetTheRequesterPickACrafter_ThenDeliveryFlows()
	{
		var id = await OpenRequestAsync();
		// Deux artisans proposent ; le demandeur ne peut pas proposer sur sa propre demande.
		await _orders.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")], Offers: [new UploadOffer(id, null, "Thorgal Marteau", "Demain soir", false)]));
		await _orders.ApplyUploadAsync(null, new UploadPayload([Char("Elaria Brume")], Offers: [new UploadOffer(null, "n-1", "Elaria Brume", null, false)]));
		await _orders.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")], Offers: [new UploadOffer(id, null, "Nayra Solen", null, false)]));
		Assert.Equal(2, await _db.CraftOffers.CountAsync());
		Assert.Equal(2, _notifier.Offers);

		// Un artisan ne peut pas s'attribuer la demande ; le demandeur retient l'offre d'Elaria.
		Assert.NotEmpty(await _orders.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau")],
			OrderActions: [new UploadOrderAction(id, null, "accepted")])));
		await _orders.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")],
			OrderActions: [new UploadOrderAction(id, null, "accepted", "Elaria Brume")]));
		var order = await _db.CraftOrders.SingleAsync();
		Assert.Equal((CraftOrderStatus.Accepted, "Elaria Brume"), (order.Status, order.CrafterCharacter));
		Assert.Single(_notifier.CrafterStatuses);

		// Elaria : prête à échanger ; Nayra confirme la réception.
		await _orders.ApplyUploadAsync(null, new UploadPayload([Char("Elaria Brume")], OrderActions: [new UploadOrderAction(id, null, "ready")]));
		await _orders.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")], OrderActions: [new UploadOrderAction(id, null, "done")]));
		Assert.Equal(CraftOrderStatus.Done, (await _db.CraftOrders.SingleAsync()).Status);
		Assert.Contains(CraftOrderStatus.Ready, _notifier.RequesterStatuses);
	}

	[Fact]
	public async Task OpenRequest_Should_Expire_ButNotADirectOrder()
	{
		var id = await OpenRequestAsync();
		await _orders.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen")],
			Orders: [new UploadOrder("n-2", "Nayra Solen", "Thorgal Marteau", "Sac", 1, null, null)]));
		var open = await _db.CraftOrders.SingleAsync(o => o.Id == id);
		open.ExpiresUtc = DateTime.UtcNow.AddMinutes(-1);
		await _db.SaveChangesAsync();

		var export = await _orders.BuildExportAsync();

		Assert.Equal("expired", export.Single(o => o.Id == id).Status);
		Assert.Equal("open", export.Single(o => o.ClientId == "n-2").Status);
		// Plus d'offre possible sur une demande expirée.
		await _roster.LinkCharacterAsync("20", "alchi", "Thorgal Marteau");
		Assert.False((await _orders.OfferFromDiscordAsync("20", id, false, null, null)).IsSuccess);
	}

	[Fact]
	public async Task Discord_Should_OfferAcceptAndReceive()
	{
		await _roster.LinkCharacterAsync("10", "nayra", "Nayra Solen");
		await _roster.LinkCharacterAsync("20", "thorgal", "Thorgal Marteau");
		var created = await _orders.CreateFromDiscordAsync("10", "nayra", null, "Flacon des Titans", 1, null);
		Assert.True(created.IsSuccess);
		var id = (await _db.CraftOrders.SingleAsync()).Id;

		Assert.True((await _orders.OfferFromDiscordAsync("20", id, false, "Ce soir", null)).IsSuccess);
		Assert.False((await _orders.UpdateStatusFromDiscordAsync("10", id, CraftOrderStatus.Accepted)).IsSuccess); // sans choisir d'offre
		Assert.True((await _orders.UpdateStatusFromDiscordAsync("10", id, CraftOrderStatus.Accepted, chosenCrafter: "thorgal marteau")).IsSuccess);
		Assert.True((await _orders.UpdateStatusFromDiscordAsync("20", id, CraftOrderStatus.Mailed)).IsSuccess);
		Assert.True((await _orders.UpdateStatusFromDiscordAsync("10", id, CraftOrderStatus.Done)).IsSuccess);

		var export = Assert.Single(await _orders.BuildExportAsync());
		Assert.Equal(("done", "Thorgal Marteau"), (export.Status, export.Crafter));
		Assert.Equal("Ce soir", Assert.Single(export.Offers!).Note);
	}

	[Fact]
	public async Task Catalog_Should_GrowFromProfessionWindows_AndFindCrafters()
	{
		var catalog = _modules.Catalog;
		await _roster.ApplyUploadAsync(null, new UploadPayload([Char("Thorgal Marteau",
			new UploadProfession("Alchimie", 300, 300, null, [new UploadRecipe(17635, "Flacon des Titans")]))]));
		await catalog.ApplyUploadAsync(new UploadPayload([Char("Thorgal Marteau",
			new UploadProfession("Alchimie", 300, 300, null, [new UploadRecipe(17635, "Flacon des Titans")],
				[new UploadRecipe(17636, "Flacon de sagesse distillée"), new UploadRecipe(17635, "Flacon des Titans")]))]));

		var hits = await catalog.SearchAsync("flacon");
		Assert.Equal(2, hits.Count);
		Assert.Equal(["Thorgal Marteau"], hits.Single(h => h.SpellId == 17635).Crafters);
		Assert.Empty(hits.Single(h => h.SpellId == 17636).Crafters);
		Assert.Equal(2, (await catalog.BuildExportAsync()).Count);
	}

	[Fact]
	public async Task Catalog_Should_UpdateRenamedRecipes_AndNotBumpVersion_WhenNothingChanges()
	{
		var catalog = _modules.Catalog;
		var upload = new UploadPayload([Char("Thorgal Marteau",
			new UploadProfession("Alchimie", 300, 300, null, [], [new UploadRecipe(17635, "  Flacon des Titans  "), new UploadRecipe(0, "Sans id"), new UploadRecipe(17637, " ")]))]);

		Assert.True(await catalog.ApplyUploadAsync(upload));
		var version = await _modules.DataVersion.CurrentAsync();
		// Même envoi : rien ne change, la version non plus (le compagnon ne réécrit pas Data.lua).
		Assert.False(await catalog.ApplyUploadAsync(upload));
		Assert.Equal(version, await _modules.DataVersion.CurrentAsync());

		// La recette renommée (client dans une autre langue) est mise à jour, pas dupliquée.
		Assert.True(await catalog.ApplyUploadAsync(new UploadPayload([Char("Thorgal Marteau",
			new UploadProfession("Alchemy", 300, 300, null, [], [new UploadRecipe(17635, "Flask of the Titans")]))])));
		var row = Assert.Single(await catalog.BuildExportAsync());
		Assert.Equal((17635, "Flask of the Titans", "Alchemy"), (row.SpellId, row.Name, row.Profession));
	}

	[Fact]
	public async Task Catalog_Should_FilterByProfession_AndSuggestKnownRecipesToo()
	{
		var catalog = _modules.Catalog;
		await catalog.ApplyUploadAsync(new UploadPayload([Char("Thorgal Marteau",
			new UploadProfession("Alchimie", 300, 300, null, [], [new UploadRecipe(17635, "Flacon des Titans")]),
			new UploadProfession("Forge", 300, 300, null, [], [new UploadRecipe(16988, "Flacon de forge")]))]));
		// Recette connue de l'annuaire seulement (pas vue dans une fenêtre de métier).
		await _roster.ApplyUploadAsync(null, new UploadPayload([Char("Nayra Solen",
			new UploadProfession("Couture", 300, 300, null, [new UploadRecipe(18560, "Flacon de tissu")]))]));

		Assert.Equal([16988], (await catalog.SearchAsync("flacon", "Forge")).Select(h => h.SpellId));
		Assert.Equal(["Flacon de forge", "Flacon des Titans", "Flacon de tissu"], await catalog.SuggestNamesAsync("FLACON"));
	}

	private sealed class Recorder : ICraftOrderNotifier
	{
		public List<string> OpenRequestCrafters { get; } = [];
		public int Offers { get; private set; }
		public List<CraftOrderStatus> CrafterStatuses { get; } = [];
		public List<CraftOrderStatus> RequesterStatuses { get; } = [];

		public Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId, string? announceChannelId = null, BotLanguage language = BotLanguage.French) => Task.CompletedTask;

		public Task NotifyStatusChangedAsync(CraftOrder order, string? requesterDiscordId, BotLanguage language = BotLanguage.French)
		{
			RequesterStatuses.Add(order.Status);
			return Task.CompletedTask;
		}

		public Task NotifyOfferAsync(CraftOrder order, CraftOffer offer, string? requesterDiscordId, BotLanguage language = BotLanguage.French)
		{
			Offers++;
			return Task.CompletedTask;
		}

		public Task NotifyCrafterAsync(CraftOrder order, string? crafterDiscordId, BotLanguage language = BotLanguage.French)
		{
			CrafterStatuses.Add(order.Status);
			return Task.CompletedTask;
		}

		public Task NotifyOpenRequestAsync(CraftOrder order, IReadOnlyList<string> crafterDiscordIds, BotLanguage language = BotLanguage.French)
		{
			OpenRequestCrafters.AddRange(crafterDiscordIds);
			return Task.CompletedTask;
		}
	}
}
