using System.Text.RegularExpressions;
using Discord;
using Discord.Net;
using Discord.WebSocket;

namespace GuildProfessions.Server.Discord;

public enum NicknameOutcome
{
	Updated,
	Unchanged,
	NotMember,
	Forbidden,
	Failed,
}

/// <summary>
/// Aligne le pseudo d'un membre sur le nom de son personnage principal, sur le
/// serveur Discord de sa guilde, si l'admin l'a activé dans /setup. Un bot ne
/// peut toucher ni l'avatar ni la bio : le pseudo de serveur est la seule prise.
/// </summary>
public sealed partial class NicknameSync(DiscordSocketClient client, ILogger<NicknameSync> logger)
{
	private const int MaxNicknameLength = 32;

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();

	public static string For(string characterName)
	{
		var cleaned = Whitespace().Replace(characterName, " ").Trim();
		return cleaned.Length <= MaxNicknameLength ? cleaned : cleaned[..MaxNicknameLength].TrimEnd();
	}

	public async Task<List<NicknameOutcome>> SyncAsync(Data.Guild guild, string discordId, string characterName)
	{
		var outcomes = new List<NicknameOutcome>();
		if (!guild.SyncNicknames || !ulong.TryParse(guild.DiscordServerId, out var serverId) ||
			!ulong.TryParse(discordId, out var userId) || client.ConnectionState != ConnectionState.Connected)
		{
			return outcomes;
		}
		var nickname = For(characterName);

		try
		{
			var member = await client.Rest.GetGuildUserAsync(serverId, userId);
			if (member is null)
			{
				outcomes.Add(NicknameOutcome.NotMember);
			}
			else if (member.Nickname == nickname)
			{
				outcomes.Add(NicknameOutcome.Unchanged);
			}
			else
			{
				await member.ModifyAsync(properties => properties.Nickname = nickname);
				outcomes.Add(NicknameOutcome.Updated);
			}
		}
		catch (HttpException exception) when (exception.HttpCode == System.Net.HttpStatusCode.NotFound
			|| exception.DiscordCode == DiscordErrorCode.UnknownMember)
		{
			outcomes.Add(NicknameOutcome.NotMember);
		}
		catch (HttpException exception) when (exception.HttpCode == System.Net.HttpStatusCode.Forbidden
			|| exception.DiscordCode == DiscordErrorCode.MissingPermissions)
		{
			// Propriétaire du serveur, ou rôle du bot placé sous celui du membre.
			outcomes.Add(NicknameOutcome.Forbidden);
		}
		catch (Exception exception)
		{
			logger.LogWarning(exception, "Pseudo de {UserId} non modifiable sur le serveur {GuildId}.", userId, serverId);
			outcomes.Add(NicknameOutcome.Failed);
		}
		return outcomes;
	}

	/// <summary>Phrase à ajouter à la réponse d'une commande, vide si rien à dire.</summary>
	public static string Describe(IReadOnlyCollection<NicknameOutcome> outcomes, string characterName, Services.BotLocalizer l)
	{
		if (outcomes.Contains(NicknameOutcome.Updated))
		{
			return l.T($"\nTon pseudo sur le serveur est maintenant **{For(characterName)}**.",
				$"\nYour server nickname is now **{For(characterName)}**.");
		}
		if (outcomes.Contains(NicknameOutcome.Forbidden))
		{
			return l.T("\nJe n'ai pas pu changer ton pseudo : Discord ne me laisse pas renommer le propriétaire du serveur, " +
					"ni un membre dont le rôle est au-dessus du mien.",
				"\nI couldn't change your nickname: Discord doesn't let me rename the server owner, " +
					"or a member whose role is above mine.");
		}
		if (outcomes.Contains(NicknameOutcome.Failed))
		{
			return l.T("\nJe n'ai pas pu changer ton pseudo pour le moment (erreur Discord), réessaie plus tard.",
				"\nI couldn't change your nickname right now (Discord error), try again later.");
		}
		return "";
	}
}
