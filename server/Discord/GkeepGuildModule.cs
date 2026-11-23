using Discord;
using Discord.Interactions;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Commandes de la vie de la guilde (annonces, charte, objectifs), à part de
/// /gkeep pour rester sous la limite de taille d'une commande Discord. Les
/// groupes restent dans le dossier Presentation du module Communication.
/// </summary>
[Group("gkeep-guild", "Guildkeep: guild announcements, handbook and shared goals")]
[CommandContextType(InteractionContextType.Guild)]
public sealed partial class GkeepGuildModule : GuildkeepGroup
{
	internal static readonly Color Green = GkeepModule.Green;
	internal static readonly Color Amber = GkeepModule.Amber;

	internal static string Clip(string text, int max) => GkeepModule.Clip(text, max);
}
