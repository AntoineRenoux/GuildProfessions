using Discord.Interactions;
using GuildProfessions.Server.Discord;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// Commandes racines des membres : /gkeep, /gkeep-raid et /gkeep-guild (Discord
/// limite la taille d'une commande, d'où le partage). Leurs groupes imbriqués
/// sont ceux des modules.
/// </summary>
internal static class DiscordRoots
{
	public static readonly Type[] Types = [typeof(GkeepModule), typeof(GkeepRaidModule), typeof(GkeepGuildModule)];

	public static IEnumerable<Type> Groups() =>
		Types.SelectMany(root => root.GetNestedTypes()).Where(t => typeof(IInteractionModuleBase).IsAssignableFrom(t));

	public static Type Group(string name) => Groups().Single(t => t.Name == name);
}
