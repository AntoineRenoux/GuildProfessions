using System.Reflection;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Discord;
using NetArchTest.Rules;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// Frontières du monolithe modulaire (comme les ArchitectureTests du CRM) :
/// un module ne connaît des autres que leur contrat public (namespace
/// *.Contracts) ; le noyau partagé ne connaît aucun module ; la couche
/// Application ne dépend ni de sa présentation ni de Discord.
/// </summary>
public sealed class ArchitectureTests
{
	private const string ModulesRoot = "GuildProfessions.Server.Modules";
	private static readonly Assembly Server = typeof(GkeepModule).Assembly;
	private static readonly string[] Internals = ["Application", "Domain", "Infrastructure", "Presentation"];

	private static readonly string[] ModuleNames = ["Annuaire", "Artisanat", "Evenements", "Presence", "Quetes", "Communication", "Profils", "Tableau"];

	public static TheoryData<string> Modules => new(ModuleNames);

	private static string[] InternalsOfOtherModules(string module) =>
		ModuleNames.Where(m => m != module)
			.SelectMany(m => Internals.Select(layer => $"{ModulesRoot}.{m}.{layer}"))
			.ToArray();

	private static void AssertSuccess(TestResult result, string rule) =>
		Assert.True(result.IsSuccessful, $"{rule} : {string.Join(", ", result.FailingTypeNames ?? [])}");

	/// <summary>Garde-fou : les règles voient bien les types des modules et leurs dépendances (sinon elles passeraient à vide).</summary>
	[Fact]
	public void Rules_Should_SeeModulesAndTheirDependencies()
	{
		Assert.All(ModuleNames, module =>
			Assert.NotEmpty(Types.InAssembly(Server).That().ResideInNamespace($"{ModulesRoot}.{module}").GetTypes()));
		Assert.NotEmpty(Types.InAssembly(Server)
			.That().ResideInNamespace($"{ModulesRoot}.Presence")
			.And().HaveDependencyOn($"{ModulesRoot}.Evenements.Contracts")
			.GetTypes());
	}

	[Theory]
	[MemberData(nameof(Modules))]
	public void Module_Should_OnlyUseThePublicContractsOfOtherModules(string module) =>
		AssertSuccess(Types.InAssembly(Server)
			.That().ResideInNamespace($"{ModulesRoot}.{module}")
			.ShouldNot().HaveDependencyOnAny(InternalsOfOtherModules(module))
			.GetResult(), $"{module} dépend des internes d'un autre module");

	[Theory]
	[MemberData(nameof(Modules))]
	public void Application_Should_NotDependOnPresentationOrDiscord(string module) =>
		AssertSuccess(Types.InAssembly(Server)
			.That().ResideInNamespace($"{ModulesRoot}.{module}.Application")
			.ShouldNot().HaveDependencyOnAny($"{ModulesRoot}.{module}.Presentation", "Discord", "GuildProfessions.Server.Discord")
			.GetResult(), $"{module}.Application dépend de la présentation");

	[Theory]
	[MemberData(nameof(Modules))]
	public void Contracts_Should_NotExposeInternals(string module) =>
		AssertSuccess(Types.InAssembly(Server)
			.That().ResideInNamespace($"{ModulesRoot}.{module}.Contracts")
			.ShouldNot().HaveDependencyOnAny([.. Internals.Select(layer => $"{ModulesRoot}.{module}.{layer}"), .. InternalsOfOtherModules(module)])
			.GetResult(), $"le contrat de {module} expose des internes");

	[Fact]
	public void SharedKernel_Should_NotKnowAnyModule() =>
		AssertSuccess(Types.InAssembly(Server)
			.That().ResideInNamespace("GuildProfessions.Server.Shared")
			.ShouldNot().HaveDependencyOnAny(ModulesRoot, "GuildProfessions.Server.Services", "GuildProfessions.Server.Discord")
			.GetResult(), "le noyau partagé dépend d'un module");

	/// <summary>
	/// Entités de chaque module. Elles gardent le namespace GuildProfessions.Server.Data
	/// (noms CLR de l'historique des migrations EF) : les règles par namespace
	/// ne les voient pas, d'où ces règles par type.
	/// </summary>
	private static readonly Dictionary<string, Type[]> ModuleEntities = new()
	{
		["Annuaire"] = [typeof(Member), typeof(Character), typeof(Profession), typeof(Recipe)],
		["Artisanat"] = [typeof(CraftOrderStatus), typeof(CraftOrder), typeof(CraftOffer), typeof(CatalogRecipe)],
		["Evenements"] = [typeof(GuildEvent), typeof(EventSignup), typeof(EventReserve), typeof(EventHardReserve), typeof(EventDuty)],
		["Presence"] = [typeof(RaidSession), typeof(RaidParticipant), typeof(RaidLoot), typeof(AttendanceCorrection)],
		["Quetes"] = [typeof(CharacterQuestLog)],
		["Communication"] = [typeof(Announcement), typeof(AnnouncementTemplate), typeof(HandbookSection), typeof(GuildGoal),
			typeof(GoalContribution), typeof(CommunicationSettings)],
		["Tableau"] = [],
		["Profils"] = [typeof(PlayerProfile), typeof(CharacterTalents), typeof(AddonVersionReport), typeof(RosterChange), typeof(OnboardingStep)],
	};

	/// <summary>Entités et internes (Application, Domain, Infrastructure, Presentation) des modules donnés.</summary>
	private static HashSet<string> InternalsOf(IEnumerable<string> modules)
	{
		var list = modules.ToList();
		var internals = list.SelectMany(m => ModuleEntities[m]).Select(t => t.FullName!).ToHashSet();
		foreach (var type in TypeReferences.AllTypes())
		{
			var name = TypeReferences.ClrName(type);
			if (list.Any(m => Internals.Any(layer => name.StartsWith($"{ModulesRoot}.{m}.{layer}.", StringComparison.Ordinal))))
			{
				internals.Add(name);
			}
		}
		return internals;
	}

	private static List<string> Leaks(Func<string, bool> inScope, HashSet<string> forbidden) =>
		TypeReferences.AllTypes()
			.Where(t => !t.IsNested && inScope(TypeReferences.ClrName(t)))
			.SelectMany(t => TypeReferences.Of(t).Where(forbidden.Contains).Select(name => $"{TypeReferences.ClrName(t)} → {name}"))
			.Distinct()
			.ToList();

	[Fact]
	public void EntityRules_Should_SeeRealDependencies()
	{
		// Garde-fou : la présence lit bien ses propres entités, et l'annuaire les siennes.
		Assert.NotEmpty(Leaks(n => n.StartsWith($"{ModulesRoot}.Presence.", StringComparison.Ordinal), InternalsOf(["Presence"])));
		Assert.Contains(Leaks(n => n.StartsWith($"{ModulesRoot}.Annuaire.", StringComparison.Ordinal), InternalsOf(["Annuaire"])),
			l => l.EndsWith(typeof(Member).FullName!, StringComparison.Ordinal));
	}

	/// <summary>Un module ne touche ni aux entités ni aux internes d'un autre, même par le corps d'une méthode.</summary>
	[Theory]
	[MemberData(nameof(Modules))]
	public void Module_Should_NotUseTheEntitiesOfOtherModules(string module)
	{
		var leaks = Leaks(n => n.StartsWith($"{ModulesRoot}.{module}.", StringComparison.Ordinal),
			InternalsOf(ModuleNames.Where(m => m != module)));
		Assert.True(leaks.Count == 0, $"{module} utilise des entités d'un autre module : {string.Join(", ", leaks)}");
	}

	/// <summary>
	/// Le noyau partagé et l'hôte HTTP (Services, Api) ne connaissent les modules
	/// que par leurs contrats. L'hôte Discord (GkeepModule, SetupModule) n'est pas
	/// concerné : les commandes racines /gkeep lier, principal et token sont
	/// celles de l'annuaire.
	/// </summary>
	[Theory]
	[InlineData("GuildProfessions.Server.Shared")]
	[InlineData("GuildProfessions.Server.Services")]
	[InlineData("GuildProfessions.Server.Api")]
	public void Host_Should_OnlyUseModuleContracts(string host)
	{
		var leaks = Leaks(n => n.StartsWith(host + ".", StringComparison.Ordinal), InternalsOf(ModuleNames));
		Assert.True(leaks.Count == 0, $"{host} dépend des internes d'un module : {string.Join(", ", leaks)}");
	}

	/// <summary>Groupe Discord imbriqué dans /gkeep, /gkeep-raid ou /gkeep-guild → module auquel il appartient.</summary>
	private static readonly Dictionary<string, string> GroupModules = new()
	{
		[nameof(GkeepModule.ProfessionGroup)] = "Annuaire",
		[nameof(GkeepModule.OrderGroup)] = "Artisanat",
		[nameof(GkeepRaidModule.EventGroup)] = "Evenements",
		[nameof(GkeepRaidModule.ReserveGroup)] = "Evenements",
		[nameof(GkeepRaidModule.AttendanceGroup)] = "Presence",
		[nameof(GkeepRaidModule.LootGroup)] = "Presence",
		[nameof(GkeepModule.QuestGroup)] = "Quetes",
		[nameof(GkeepGuildModule.AnnounceGroup)] = "Communication",
		[nameof(GkeepGuildModule.HandbookGroup)] = "Communication",
		[nameof(GkeepGuildModule.GoalGroup)] = "Communication",
		[nameof(GkeepModule.ProfileGroup)] = "Profils",
		[nameof(GkeepModule.WelcomeGroup)] = "Profils",
		[nameof(GkeepModule.HomeGroup)] = "Tableau",
	};

	public static TheoryData<string, string> DiscordGroups
	{
		get
		{
			var data = new TheoryData<string, string>();
			foreach (var (group, module) in GroupModules)
			{
				data.Add(group, module);
			}
			return data;
		}
	}

	/// <summary>
	/// Les groupes restent imbriqués dans GkeepModule (Discord l'impose) : on
	/// vérifie qu'ils ne reçoivent que des services de leur module, et des
	/// suggestions de leur module ou partagées.
	/// </summary>
	[Theory]
	[MemberData(nameof(DiscordGroups))]
	public void DiscordGroup_Should_OnlyUseItsOwnModule(string group, string module)
	{
		var type = DiscordRoots.Group(group);
		var services = type.GetConstructors().SelectMany(c => c.GetParameters()).Select(p => p.ParameterType);
		var handlers = type.GetMethods()
			.SelectMany(m => m.GetParameters())
			.SelectMany(p => p.GetCustomAttributes<AutocompleteAttribute>())
			.Select(a => a.AutocompleteHandlerType!);
		var forbidden = InternalsOfOtherModules(module);

		var leaks = services.Concat(handlers)
			.Where(t => forbidden.Any(ns => t.Namespace?.StartsWith(ns, StringComparison.Ordinal) == true))
			.Select(t => t.FullName)
			.ToList();

		Assert.Empty(leaks);

		// Corps des commandes compris (lambdas, machines d'état async) : rien d'un autre module.
		var definition = TypeReferences.AllTypes().Single(t => TypeReferences.ClrName(t) == type.FullName);
		var forbiddenTypes = InternalsOf(ModuleNames.Where(m => m != module));
		Assert.DoesNotContain(TypeReferences.Of(definition), forbiddenTypes.Contains);
	}

	[Fact]
	public void EveryDiscordGroup_Should_BeMappedToAModule()
	{
		var groups = DiscordRoots.Groups()
			.Select(t => t.Name)
			.Order();

		Assert.Equal(GroupModules.Keys.Order(), groups);
	}
}
