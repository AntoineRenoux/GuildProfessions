using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// Services d'une guilde de test, câblés par la même composition que le
/// serveur (AddGuildkeep) : un service ou un contributeur oublié dans un
/// module se voit dès les tests. Seuls la base, la guilde courante et les
/// notifications Discord sont fournis par le test.
/// </summary>
internal sealed class TestModules : IDisposable
{
	private readonly ServiceProvider _provider;
	private readonly IServiceScope _scope;

	private TestModules(ServiceProvider provider)
	{
		_provider = provider;
		_scope = provider.CreateScope();
	}

	public static TestModules For(AppDbContext db, GuildContext guild, ICraftOrderNotifier? notifier = null,
		Action<IServiceCollection>? configure = null)
	{
		var services = new ServiceCollection();
		services.AddLogging();
		services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
		services.AddGuildkeep();
		// Fournis par le test (le dernier enregistrement l'emporte).
		services.AddSingleton(db);
		services.AddSingleton(guild);
		services.AddSingleton(notifier ?? new NullCraftOrderNotifier());
		services.AddSingleton<IAnnouncementPublisher>(new NullAnnouncementPublisher());
		configure?.Invoke(services);
		return new TestModules(services.BuildServiceProvider());
	}

	public T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

	public RosterService Roster => Get<RosterService>();
	public EventService Events => Get<EventService>();
	public AttendanceService Attendance => Get<AttendanceService>();
	public CraftOrderService Orders => Get<CraftOrderService>();
	public CatalogService Catalog => Get<CatalogService>();
	public QuestService Quests => Get<QuestService>();
	public CommunicationService Communication => Get<CommunicationService>();
	public ProfileService Profiles => Get<ProfileService>();
	public UploadIngestion Ingestion => Get<UploadIngestion>();
	public ExportBuilder Export => Get<ExportBuilder>();
	public IDataVersion DataVersion => Get<IDataVersion>();

	public void Dispose()
	{
		_scope.Dispose();
		_provider.Dispose();
	}
}

/// <summary>Notifications Discord ignorées (bot absent des tests).</summary>
internal sealed class NullCraftOrderNotifier : ICraftOrderNotifier
{
	public Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId, string? announceChannelId = null, BotLanguage language = BotLanguage.French) => Task.CompletedTask;
	public Task NotifyStatusChangedAsync(CraftOrder order, string? requesterDiscordId, BotLanguage language = BotLanguage.French) => Task.CompletedTask;
	public Task NotifyOfferAsync(CraftOrder order, CraftOffer offer, string? requesterDiscordId, BotLanguage language = BotLanguage.French) => Task.CompletedTask;
	public Task NotifyCrafterAsync(CraftOrder order, string? crafterDiscordId, BotLanguage language = BotLanguage.French) => Task.CompletedTask;
	public Task NotifyOpenRequestAsync(CraftOrder order, IReadOnlyList<string> crafterDiscordIds, BotLanguage language = BotLanguage.French) => Task.CompletedTask;
}

/// <summary>
/// Raccourcis des tests : un envoi appliqué à un seul module, sans passer par
/// l'orchestrateur (ni filtre de guilde, ni transaction par module). Ils
/// vivent ici et non dans le code de production ; le chemin réel est couvert
/// par ModuleIsolationTests et GuildIsolationTests.
/// </summary>
internal static class ModuleUploadShortcuts
{
	private static UploadContext Context(Member? member, UploadPayload payload) =>
		new(member?.Id, payload, member is null ? UploadSource.GuildToken : UploadSource.PersonalToken, []);

	/// <summary>Commandes d'un envoi ; les messages Discord partent aussitôt.</summary>
	public static async Task<List<string>> ApplyUploadAsync(this CraftOrderService orders, Member? member, UploadPayload payload,
		CancellationToken ct = default)
	{
		var afterCommit = new List<Func<Task>>();
		var warnings = await orders.ApplyUploadAsync(Context(member, payload), afterCommit, ct);
		foreach (var send in afterCommit)
		{
			await send();
		}
		return warnings;
	}

	public static Task<List<string>> ApplyUploadAsync(this EventService events, Member? member, UploadPayload payload,
		CancellationToken ct = default) =>
		events.ApplyUploadAsync(Context(member, payload), ct);

	/// <summary>
	/// Officier reconnu par l'annuaire : le membre déclare le rang avec son token
	/// personnel (seul canal qui le donne). Le token de guilde ne suffit pas.
	/// </summary>
	public static async Task PromoteOfficerAsync(this RosterService roster, string discordId, string character)
	{
		var member = await roster.GetOrCreateMemberAsync(discordId, discordId);
		await roster.ApplyUploadAsync(member, new UploadPayload(
			[new UploadCharacter(character, "MAGE", null, null, null, 60, [], Officer: true)]));
	}

	/// <summary>Communication d'un envoi ; les publications Discord partent aussitôt.</summary>
	public static async Task<List<string>> ApplyUploadAsync(this CommunicationService communication, Member? member, UploadPayload payload,
		CancellationToken ct = default)
	{
		var afterCommit = new List<Func<Task>>();
		var warnings = await communication.ApplyUploadAsync(Context(member, payload), afterCommit, ct);
		foreach (var send in afterCommit)
		{
			await send();
		}
		return warnings;
	}

	public static Task<List<string>> ApplyUploadAsync(this ProfileService profiles, Member? member, UploadPayload payload,
		CancellationToken ct = default) =>
		profiles.ApplyUploadAsync(Context(member, payload), ct);

	public static Task<List<string>> ApplyUploadAsync(this AttendanceService attendance, Member? member, UploadPayload payload,
		CancellationToken ct = default) =>
		attendance.ApplyUploadAsync(Context(member, payload), ct);
}
