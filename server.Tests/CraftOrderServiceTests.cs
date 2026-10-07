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
		_db = new AppDbContext(options);
		_db.Database.EnsureCreated();
		_roster = new RosterService(_db);
		_orders = new CraftOrderService(_db, _roster, _notifier);
	}

	public void Dispose()
	{
		_db.Dispose();
		_connection.Dispose();
	}

	[Fact]
	public async Task Lifecycle_Should_FlowFromOpenToAcceptedToDone_ByCrafterOnly()
	{
		await _roster.LinkCharacterAsync("10", "demandeur", "Nayra");
		await _roster.LinkCharacterAsync("20", "artisan", "Thorgal");
		var created = await _orders.CreateFromDiscordAsync("10", "demandeur", "Thorgal", "Heaume Cœur de lion", 1, null);
		Assert.True(created.Success);
		var orderId = (await _db.CraftOrders.SingleAsync()).Id;

		// Le demandeur ne peut pas accepter sa propre commande.
		var refusedAccept = await _orders.UpdateStatusFromDiscordAsync("10", orderId, CraftOrderStatus.Accepted);
		Assert.False(refusedAccept.Success);

		Assert.True((await _orders.UpdateStatusFromDiscordAsync("20", orderId, CraftOrderStatus.Accepted)).Success);
		Assert.True((await _orders.UpdateStatusFromDiscordAsync("20", orderId, CraftOrderStatus.Done)).Success);

		// Terminée = terminale : plus aucune transition.
		var refusedCancel = await _orders.UpdateStatusFromDiscordAsync("20", orderId, CraftOrderStatus.Cancelled);
		Assert.False(refusedCancel.Success);
		Assert.Equal(CraftOrderStatus.Done, (await _db.CraftOrders.SingleAsync()).Status);
	}

	[Fact]
	public async Task Cancel_Should_BeAllowedForRequester()
	{
		await _roster.LinkCharacterAsync("10", "demandeur", "Nayra");
		await _orders.CreateFromDiscordAsync("10", "demandeur", "Thorgal", "Sac en étoffe runique", 4, null);
		var orderId = (await _db.CraftOrders.SingleAsync()).Id;

		var result = await _orders.UpdateStatusFromDiscordAsync("10", orderId, CraftOrderStatus.Cancelled);

		Assert.True(result.Success);
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

	#region Helper Methods

	private sealed class RecordingNotifier : ICraftOrderNotifier
	{
		public List<string?> NewOrderRecipients { get; } = [];
		public List<string?> StatusRecipients { get; } = [];

		public Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId)
		{
			NewOrderRecipients.Add(crafterDiscordId);
			return Task.CompletedTask;
		}

		public Task NotifyStatusChangedAsync(CraftOrder order, string? requesterDiscordId)
		{
			StatusRecipients.Add(requesterDiscordId);
			return Task.CompletedTask;
		}
	}

	#endregion
}
