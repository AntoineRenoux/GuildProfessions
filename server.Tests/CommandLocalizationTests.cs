using Discord.Interactions;
using Discord.WebSocket;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Discord;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// Toutes les commandes du bot sont sous /gkeep (ou /gkeep-setup) et ont un
/// nom et une description en français : une clé oubliée ou mal orthographiée
/// dans commands.fr.json laisserait la commande en anglais sans prévenir.
/// </summary>
public sealed class CommandLocalizationTests
{
	private static readonly ILocalizationManager French =
		new JsonLocalizationManager(Path.Combine(AppContext.BaseDirectory, "Localization"), "commands");

	private static async Task<InteractionService> LoadAsync()
	{
		var client = new DiscordSocketClient();
		var service = new InteractionService(client, new InteractionServiceConfig { LocalizationManager = French });
		// Discord.Net instancie chaque module à la construction : mêmes services que Program.cs.
		var services = new ServiceCollection()
			.AddLogging()
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
			.AddSingleton(client)
			.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=:memory:"))
			.AddScoped<GuildContext>()
			.AddScoped<BotLocalizer>()
			.AddScoped<GuildDirectory>()
			.AddScoped<RosterService>()
			.AddScoped<ExportBuilder>()
			.AddScoped<CraftOrderService>()
			.AddScoped<UploadIngestion>()
			.AddSingleton<ICraftOrderNotifier, NullCraftOrderNotifier>()
			.AddSingleton<NicknameSync>()
			.AddSingleton<SiteLinks>()
			.BuildServiceProvider();
		await service.AddModulesAsync(typeof(GkeepModule).Assembly, services.CreateScope().ServiceProvider);
		return service;
	}

	[Fact]
	public async Task EveryCommand_Should_StartWithGkeep()
	{
		var service = await LoadAsync();
		var roots = service.Modules.Where(m => m.Parent is null)
			.SelectMany(m => m.IsSlashGroup ? [m.SlashGroupName] : m.SlashCommands.Select(c => c.Name))
			.ToList();

		Assert.NotEmpty(roots);
		Assert.All(roots, name => Assert.StartsWith("gkeep", name));
	}

	[Fact]
	public async Task EveryCommandAndOption_Should_HaveAFrenchNameAndDescription()
	{
		var service = await LoadAsync();
		var missing = new List<string>();
		var keys = new List<(List<string> Key, LocalizationTarget Target)>();
		foreach (var module in service.Modules.Where(m => m.IsSlashGroup))
		{
			keys.Add((ModulePath(module).ToList(), LocalizationTarget.Group));
		}
		foreach (var command in service.SlashCommands)
		{
			var path = ModulePath(command.Module).Append(command.Name).ToList();
			keys.Add((path, LocalizationTarget.Command));
			keys.AddRange(command.Parameters.Select(p => (path.Append(p.Name).ToList(), LocalizationTarget.Parameter)));
		}
		foreach (var (key, target) in keys)
		{
			if (!Translated(French.GetAllNames(key, target)) || !Translated(French.GetAllDescriptions(key, target)))
			{
				missing.Add(string.Join(" ", key));
			}
		}

		Assert.NotEmpty(service.SlashCommands);
		Assert.Empty(missing);
	}

	private static bool Translated(IDictionary<string, string>? values) =>
		values is not null && values.TryGetValue("fr", out var value) && !string.IsNullOrWhiteSpace(value);

	private static IEnumerable<string> ModulePath(ModuleInfo? module)
	{
		var names = new List<string>();
		for (var current = module; current is not null; current = current.Parent)
		{
			if (current.IsSlashGroup)
			{
				names.Insert(0, current.SlashGroupName);
			}
		}
		return names;
	}
}
