using Discord.Interactions;
using Discord.WebSocket;

namespace GuildProfessions.Server.Discord;

/// <summary>
/// Base des commandes Guildkeep (racine /gkeep et groupes des modules) : qui
/// agit, et s'il gère le serveur Discord (mêmes droits qu'un officier en jeu).
/// </summary>
public abstract class GuildkeepGroup : InteractionModuleBase<SocketInteractionContext>
{
	/// <summary>Identifiant Discord de l'utilisateur, tel que l'annuaire le stocke.</summary>
	protected string DiscordId => Context.User.Id.ToString();

	/// <summary>L'utilisateur a le droit « Gérer le serveur ».</summary>
	protected bool IsServerAdmin => Context.User is SocketGuildUser user && user.GuildPermissions.ManageGuild;
}
