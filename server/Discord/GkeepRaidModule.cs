using Discord;
using Discord.Interactions;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Commandes de raid, à part de /gkeep : Discord limite une commande (noms,
/// descriptions et choix compris) à 8 000 caractères, et les événements,
/// réservations, présence et butin la dépassaient à eux seuls. Les groupes
/// restent dans le dossier Presentation de leur module (Événements, Présence).
/// </summary>
[Group("gkeep-raid", "Guildkeep: events and sign-ups, loot reserves, raid attendance and loot")]
[CommandContextType(InteractionContextType.Guild)]
public sealed partial class GkeepRaidModule : GuildkeepGroup
{
	internal static readonly Color Green = GkeepModule.Green;
	internal static readonly Color Amber = GkeepModule.Amber;

	internal static string Clip(string text, int max) => GkeepModule.Clip(text, max);
}
