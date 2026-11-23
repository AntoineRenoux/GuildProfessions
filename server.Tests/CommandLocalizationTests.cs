using System.Reflection;
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
		var (service, services) = Create();
		await service.AddModulesAsync(typeof(GkeepModule).Assembly, services);
		return service;
	}

	private static (InteractionService Service, IServiceProvider Services) Create()
	{
		var client = new DiscordSocketClient();
		var service = new InteractionService(client, new InteractionServiceConfig { LocalizationManager = French });
		// Discord.Net instancie chaque module à la construction : mêmes services que Program.cs.
		var services = new ServiceCollection()
			.AddLogging()
			.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
			.AddSingleton(client)
			.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=:memory:"))
			.AddGuildkeep()
			.AddSingleton<ICraftOrderNotifier, NullCraftOrderNotifier>()
			.AddSingleton<NicknameSync>()
			.BuildServiceProvider();
		return (service, services.CreateScope().ServiceProvider);
	}

	public static TheoryData<string> ModuleGroups =>
		new(typeof(GkeepModule).GetNestedTypes()
			.Where(t => typeof(IInteractionModuleBase).IsAssignableFrom(t))
			.Select(t => t.Name));

	/// <summary>
	/// Les groupes des modules sont tous sous /gkeep : au démarrage comme à la
	/// publication, un seul groupe invalide (paramètre non pris en charge,
	/// handler d'autocomplétion, nom en double, limites Discord) ferait tomber
	/// toute la commande. Chaque groupe est donc construit seul ici, pour que
	/// l'échec nomme le module fautif avant la mise en production.
	/// </summary>
	[Theory]
	[MemberData(nameof(ModuleGroups))]
	public async Task EachModuleGroup_Should_BuildAlone(string group)
	{
		var (service, services) = Create();
		var type = typeof(GkeepModule).GetNestedType(group)!;

		// Discord.Net ne construit un groupe imbriqué qu'avec son parent : on appelle
		// son constructeur de module interne sur le seul groupe (test seulement).
		// Les limites imposées par Discord à la publication sont vérifiées par les autres tests de cette classe.
		var exception = await Record.ExceptionAsync(() =>
		{
			BuildAlone(service, type, services);
			return Task.CompletedTask;
		});

		Assert.True(exception is null, $"Le groupe Discord {group} ne se construit pas : {exception?.InnerException ?? exception}");
	}

	private static ModuleInfo BuildAlone(InteractionService service, Type type, IServiceProvider services)
	{
		const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
		var classBuilder = typeof(InteractionService).Assembly.GetType("Discord.Interactions.Builders.ModuleClassBuilder", throwOnError: true)!;
		var builder = new global::Discord.Interactions.Builders.ModuleBuilder(service, type.Name);
		classBuilder.GetMethod("BuildModule", Internal)!.Invoke(null, [builder, type.GetTypeInfo(), service, services]);
		var build = typeof(global::Discord.Interactions.Builders.ModuleBuilder).GetMethod("Build", Internal)!;
		return (ModuleInfo)build.Invoke(builder, [service, services, null])!;
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

	/// <summary>
	/// Discord refuse tout l'enregistrement si une seule commande est invalide :
	/// on vérifie ses règles (noms, longueurs) en anglais et en français avant publication.
	/// </summary>
	[Fact]
	public async Task EveryCommandAndOption_Should_RespectDiscordLimits()
	{
		var service = await LoadAsync();
		var problems = new List<string>();
		void Check(List<string> key, LocalizationTarget target, string name, string description)
		{
			var names = new[] { name }.Concat(French.GetAllNames(key, target)?.Values ?? []);
			var descriptions = new[] { description }.Concat(French.GetAllDescriptions(key, target)?.Values ?? []);
			problems.AddRange(names.Where(n => !System.Text.RegularExpressions.Regex.IsMatch(n, @"^[-_\p{Ll}\p{Lo}\p{N}]{1,32}$"))
				.Select(n => $"{string.Join(" ", key)} : nom « {n} »"));
			problems.AddRange(descriptions.Where(d => d.Length is 0 or > 100)
				.Select(d => $"{string.Join(" ", key)} : description de {d.Length} caractères"));
		}
		foreach (var module in service.Modules.Where(m => m.IsSlashGroup))
		{
			Check(ModulePath(module).ToList(), LocalizationTarget.Group, module.SlashGroupName, module.Description);
		}
		foreach (var command in service.SlashCommands)
		{
			var path = ModulePath(command.Module).Append(command.Name).ToList();
			Check(path, LocalizationTarget.Command, command.Name, command.Description);
			foreach (var parameter in command.Parameters)
			{
				Check(path.Append(parameter.Name).ToList(), LocalizationTarget.Parameter, parameter.Name, parameter.Description);
			}
		}

		Assert.Empty(problems);
	}

	[Fact]
	public void BotService_Should_LoadEachTopLevelDiscordModuleSeparately() =>
		Assert.Equal(["GkeepModule", "SetupModule"], DiscordBotService.InteractionModuleTypes().Select(t => t.Name).Order());

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
