using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Réserve une commande aux serveurs Discord qui ont créé leur guilde avec
/// /setup. La guilde courante est posée par DiscordBotService avant
/// l'exécution, d'après le serveur d'où vient l'interaction.
/// </summary>
public sealed class RequireRegisteredGuildAttribute : PreconditionAttribute
{
	public static string NotRegistered(BotLocalizer l) => l.T(
		"Ce serveur n'utilise pas encore Guildkeep : un administrateur doit d'abord lancer `/gkeep-setup`.",
		"This server doesn't use Guildkeep yet: a server admin needs to run `/gkeep-setup` first.");

	public override Task<PreconditionResult> CheckRequirementsAsync(
		IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
	{
		var registered = services.GetRequiredService<GuildContext>().GuildId is not null;
		return Task.FromResult(registered
			? PreconditionResult.FromSuccess()
			: PreconditionResult.FromError(NotRegistered(services.GetRequiredService<BotLocalizer>())));
	}
}
