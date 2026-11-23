using System.Reflection;
using Discord;
using Discord.Interactions;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// Discord refuse une commande de premier niveau dont les noms, descriptions
/// et choix (sous-commandes comprises) dépassent 8 000 caractères, en
/// comptant pour chaque champ la plus longue de ses traductions ; il garde
/// alors l'ancienne version sans rien casser de visible. /gkeep a dépassé
/// cette limite en production : chaque racine est mesurée ici, avec une marge.
/// </summary>
public sealed class CommandSizeTests
{
	private const int DiscordLimit = 8000;

	/// <summary>Marge pour les prochaines commandes : au-delà, découper encore plutôt que frôler la limite.</summary>
	private const int Budget = 7000;

	private const int MaxOptions = 25;

	private static int Longest(string? value, IEnumerable<KeyValuePair<string, string>>? localized) =>
		Math.Max(value?.Length ?? 0, localized?.Select(v => v.Value.Length).DefaultIfEmpty(0).Max() ?? 0);

	private static int Size(ApplicationCommandOptionProperties option)
	{
		var size = Longest(option.Name, option.NameLocalizations) + Longest(option.Description, option.DescriptionLocalizations);
		foreach (var choice in option.Choices ?? [])
		{
			size += Longest(choice.Name, choice.NameLocalizations) + (choice.Value?.ToString()?.Length ?? 0);
		}
		return size + (option.Options ?? []).Sum(Size);
	}

	private static async Task<List<(string Name, int Size, int Options)>> MeasureAsync()
	{
		var service = await CommandLocalizationTests.LoadAsync();
		// Conversion interne de Discord.Net vers ce qui est réellement envoyé à Discord.
		var util = typeof(InteractionService).Assembly.GetType("Discord.Interactions.ApplicationCommandRestUtil", throwOnError: true)!;
		var toProps = util.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
			.First(m => m.Name == "ToApplicationCommandProps" && m.GetParameters()[0].ParameterType == typeof(ModuleInfo));
		var result = new List<(string, int, int)>();
		foreach (var module in service.Modules.Where(m => m.Parent is null))
		{
			foreach (var command in ((IEnumerable<ApplicationCommandProperties>)toProps.Invoke(null, [module, false])!).OfType<SlashCommandProperties>())
			{
				var options = command.Options.GetValueOrDefault() ?? [];
				var size = Longest(command.Name.GetValueOrDefault(), command.NameLocalizations) +
					Longest(command.Description.GetValueOrDefault(), command.DescriptionLocalizations) + options.Sum(Size);
				result.Add((command.Name.GetValueOrDefault(), size, options.Count));
			}
		}
		return result;
	}

	[Fact]
	public async Task EveryRootCommand_Should_StayUnderDiscordSizeLimit_WithMargin()
	{
		var commands = await MeasureAsync();

		Assert.Contains(commands, c => c.Name == "gkeep-raid");
		Assert.All(commands, c =>
		{
			Assert.True(c.Size <= Budget, $"/{c.Name} : {c.Size} caractères (limite Discord {DiscordLimit}, marge {Budget}) : découper la commande.");
			Assert.True(c.Options <= MaxOptions, $"/{c.Name} : {c.Options} sous-commandes et groupes (maximum {MaxOptions}).");
		});
	}
}
