using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GuildProfessions.Server.Tests;

public sealed class CraftOrderServiceTests : IDisposable
{
	private readonly SqliteConnection _connection;
	private readonly AppDbContext _db;
	private readonly GuildContext _guild = new();
	private readonly TestModules _modules;
	private readonly RosterService _roster;
	private readonly CraftOrderService _orders;
	private readonly RecordingNotifier _notifier = new();

	public CraftOrderServiceTests()
	{
		_connection = new SqliteConnection("Data Source=:memory:");
		_connection.Open();
		var options = new DbContextOptionsBuilder<AppDbContext>()
			.UseSqlite(_connection)
			.Options;
		_db = new AppDbContext(options, _guild);
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

	[Fact]
	public async Task Lifecycle_Should_FlowFromAcceptedToMailedToReceived()
	{
		await _roster.LinkCharacterAsync("10", "demandeur", "Nayra");
		await _roster.LinkCharacterAsync("20", "artisan", "Thorgal");
		var created = await _orders.CreateFromDiscordAsync("10", "demandeur", "Thorgal", "Heaume Cœur de lion", 1, null);
		Assert.True(created.IsSuccess);
		var orderId = (await _db.CraftOrders.SingleAsync()).Id;

		// Le demandeur ne peut pas accepter sa propre commande.
		var refusedAccept = await _orders.UpdateStatusFromDiscordAsync("10", orderId, CraftOrderStatus.Accepted);
		Assert.False(refusedAccept.IsSuccess);

		Assert.True((await _orders.UpdateStatusFromDiscordAsync("20", orderId, CraftOrderStatus.Accepted)).IsSuccess);
		Assert.True((await _orders.UpdateStatusFromDiscordAsync("20", orderId, CraftOrderStatus.Mailed)).IsSuccess);
		// C'est le demandeur qui confirme la réception, pas l'artisan.
		Assert.False((await _orders.UpdateStatusFromDiscordAsync("20", orderId, CraftOrderStatus.Done)).IsSuccess);
		Assert.True((await _orders.UpdateStatusFromDiscordAsync("10", orderId, CraftOrderStatus.Done)).IsSuccess);

		// Terminée = terminale : plus aucune transition.
		var refusedCancel = await _orders.UpdateStatusFromDiscordAsync("20", orderId, CraftOrderStatus.Cancelled);
		Assert.False(refusedCancel.IsSuccess);
		Assert.Equal(CraftOrderStatus.Done, (await _db.CraftOrders.SingleAsync()).Status);
	}

	[Fact]
	public async Task Cancel_Should_BeAllowedForRequester()
	{
		await _roster.LinkCharacterAsync("10", "demandeur", "Nayra");
		await _orders.CreateFromDiscordAsync("10", "demandeur", "Thorgal", "Sac en étoffe runique", 4, null);
		var orderId = (await _db.CraftOrders.SingleAsync()).Id;

		var result = await _orders.UpdateStatusFromDiscordAsync("10", orderId, CraftOrderStatus.Cancelled);

		Assert.True(result.IsSuccess);
		Assert.Equal(CraftOrderStatus.Cancelled, (await _db.CraftOrders.SingleAsync()).Status);
	}

	[Fact]
	public async Task ApplyUpload_Should_CreateOrderOnce_AndNotifyLinkedCrafter()
	{
		var requester = await _roster.GetOrCreateMemberAsync("10", "demandeur");
		await _roster.LinkCharacterAsync("10", "demandeur", "Elaria");
		await _roster.LinkCharacterAsync("20", "artisan", "Thorgal");

		var payload = new UploadPayload([],
			Orders: [new UploadOrder("Elaria-123-abc", "Elaria", "Thorgal", "Marteau de Sulfuron", 1, "mats fournis", null)]);
		await _orders.ApplyUploadAsync(requester, payload);
		await _orders.ApplyUploadAsync(requester, payload); // rejoué : même ClientId

		var order = await _db.CraftOrders.SingleAsync();
		Assert.Equal("Elaria-123-abc", order.ClientId);
		Assert.Equal(DataSource.Addon, order.Source);
		Assert.Equal("20", Assert.Single(_notifier.NewOrderRecipients));
	}

	[Fact]
	public async Task ApplyUpload_Should_IgnoreOrder_WhenRequesterNotOwnedByUploader()
	{
		var uploader = await _roster.GetOrCreateMemberAsync("99", "intrus");

		var warnings = await _orders.ApplyUploadAsync(uploader, new UploadPayload([],
			Orders: [new UploadOrder("x-1", "Elaria", "Thorgal", "Flacon des Titans", 1, null, null)]));

		Assert.Single(warnings);
		Assert.Empty(_db.CraftOrders);
	}

	[Fact]
	public async Task ApplyUpload_Should_ApplyStatusAction_ByServerIdForCrafter()
	{
		await _roster.LinkCharacterAsync("10", "demandeur", "Nayra");
		var crafterMember = await _roster.GetOrCreateMemberAsync("20", "artisan");
		await _roster.LinkCharacterAsync("20", "artisan", "Thorgal");
		await _orders.CreateFromDiscordAsync("10", "demandeur", "Thorgal", "Heaume Cœur de lion", 1, null);
		var orderId = (await _db.CraftOrders.SingleAsync()).Id;

		await _orders.ApplyUploadAsync(crafterMember, new UploadPayload([],
			OrderActions: [new UploadOrderAction(orderId, null, "accepted")]));

		Assert.Equal(CraftOrderStatus.Accepted, (await _db.CraftOrders.SingleAsync()).Status);
		Assert.Equal("10", Assert.Single(_notifier.StatusRecipients));
	}

	[Fact]
	public async Task ApplyUpload_SharedMode_Should_UsePayloadCharactersAsIdentity()
	{
		await _roster.LinkCharacterAsync("10", "demandeur", "Nayra");
		await _roster.LinkCharacterAsync("20", "artisan", "Thorgal");
		await _orders.CreateFromDiscordAsync("10", "demandeur", "Thorgal", "Heaume Cœur de lion", 1, null);
		var orderId = (await _db.CraftOrders.SingleAsync()).Id;

		// Upload partagé contenant le scan de Thorgal : l'action de l'artisan passe.
		var crafterPayload = new UploadPayload(
			[new UploadCharacter("Thorgal", "WARRIOR", null, null, null, 60, [])],
			OrderActions: [new UploadOrderAction(orderId, null, "accepted")]);
		await _orders.ApplyUploadAsync(null, crafterPayload);
		Assert.Equal(CraftOrderStatus.Accepted, (await _db.CraftOrders.SingleAsync()).Status);

		// Upload partagé d'un AUTRE compte (sans Thorgal) : action refusée.
		var strangerPayload = new UploadPayload(
			[new UploadCharacter("Inconnu", null, null, null, null, null, [])],
			OrderActions: [new UploadOrderAction(orderId, null, "done")]);
		var warnings = await _orders.ApplyUploadAsync(null, strangerPayload);
		Assert.Single(warnings);
		Assert.Equal(CraftOrderStatus.Accepted, (await _db.CraftOrders.SingleAsync()).Status);
	}

	#region Helper Methods

	[Fact]
	public async Task NewOrder_Should_BeAnnounced_InTheGuildChannel_WhenConfigured()
	{
		_guild.Required.OrdersChannelId = "555";
		await _roster.LinkCharacterAsync("10", "demandeur", "Nayra");

		await _orders.CreateFromDiscordAsync("10", "demandeur", "Thorgal", "Lingot de thorium", 5, null);

		Assert.Equal(["555"], _notifier.AnnounceChannels);
	}

	private sealed class RecordingNotifier : ICraftOrderNotifier
	{
		public List<string?> NewOrderRecipients { get; } = [];
		public List<string?> StatusRecipients { get; } = [];
		public List<string?> AnnounceChannels { get; } = [];

		public Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId, string? announceChannelId = null, BotLanguage language = BotLanguage.French)
		{
			NewOrderRecipients.Add(crafterDiscordId);
			AnnounceChannels.Add(announceChannelId);
			return Task.CompletedTask;
		}

		public Task NotifyStatusChangedAsync(CraftOrder order, string? requesterDiscordId, BotLanguage language = BotLanguage.French)
		{
			StatusRecipients.Add(requesterDiscordId);
			return Task.CompletedTask;
		}

		public Task NotifyOfferAsync(CraftOrder order, CraftOffer offer, string? requesterDiscordId, BotLanguage language = BotLanguage.French) => Task.CompletedTask;

		public Task NotifyCrafterAsync(CraftOrder order, string? crafterDiscordId, BotLanguage language = BotLanguage.French) => Task.CompletedTask;

		public Task NotifyOpenRequestAsync(CraftOrder order, IReadOnlyList<string> crafterDiscordIds, BotLanguage language = BotLanguage.French) => Task.CompletedTask;
	}

	#endregion
}
