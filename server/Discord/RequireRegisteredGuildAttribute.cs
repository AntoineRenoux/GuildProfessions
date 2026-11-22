using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Réserve une commande aux serveurs Discord qui ont créé leur guilde avec
/// /setup. La guilde courante est posée par DiscordBotService avant
/// l'exécution, d'après le serveur d'où vient l'interaction.
/// </summary>
public sealed class RequireRegisteredGuildAttribute : PreconditionAttribute
{
	public const string NotRegistered =
		"Ce serveur n'utilise pas encore Guildkeep : un administrateur doit d'abord lancer `/setup`.";

	public override Task<PreconditionResult> CheckRequirementsAsync(
		IInteractionContext context, ICommandInfo commandInfo, IServiceProvider services)
	{
		var registered = services.GetRequiredService<GuildContext>().GuildId is not null;
		return Task.FromResult(registered ? PreconditionResult.FromSuccess() : PreconditionResult.FromError(NotRegistered));
	}
}
