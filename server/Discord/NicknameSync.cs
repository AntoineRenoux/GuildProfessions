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
/// Aligne le pseudo d'un membre sur le nom de son personnage principal, sur
/// chaque serveur configuré. Un bot ne peut toucher ni l'avatar ni la bio d'un
/// utilisateur : le pseudo de serveur est la seule prise possible.
/// </summary>
public sealed partial class NicknameSync(DiscordSocketClient client, IConfiguration config, ILogger<NicknameSync> logger)
{
	private const int MaxNicknameLength = 32;

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();

	public static string For(string characterName)
	{
		var cleaned = Whitespace().Replace(characterName, " ").Trim();
		return cleaned.Length <= MaxNicknameLength ? cleaned : cleaned[..MaxNicknameLength].TrimEnd();
	}

	public async Task<List<NicknameOutcome>> SyncAsync(string discordId, string characterName)
	{
		var outcomes = new List<NicknameOutcome>();
		if (!ulong.TryParse(discordId, out var userId) || client.ConnectionState != ConnectionState.Connected)
		{
			return outcomes;
		}
		var nickname = For(characterName);

		foreach (var guildId in DiscordGuildIds.Parse(config))
		{
			try
			{
				var member = await client.Rest.GetGuildUserAsync(guildId, userId);
				if (member is null)
				{
					outcomes.Add(NicknameOutcome.NotMember);
					continue;
				}
				if (member.Nickname == nickname)
				{
					outcomes.Add(NicknameOutcome.Unchanged);
					continue;
				}
				await member.ModifyAsync(properties => properties.Nickname = nickname);
				outcomes.Add(NicknameOutcome.Updated);
			}
			catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.UnknownMember)
			{
				outcomes.Add(NicknameOutcome.NotMember);
			}
			catch (HttpException exception) when (exception.DiscordCode == DiscordErrorCode.MissingPermissions)
			{
				// Propriétaire du serveur, ou rôle du bot placé sous celui du membre.
				outcomes.Add(NicknameOutcome.Forbidden);
			}
			catch (Exception exception)
			{
				logger.LogWarning(exception, "Pseudo de {UserId} non modifiable sur le serveur {GuildId}.", userId, guildId);
				outcomes.Add(NicknameOutcome.Failed);
			}
		}
		return outcomes;
	}

	/// <summary>Phrase à ajouter à la réponse d'une commande, vide si rien à dire.</summary>
	public static string Describe(IReadOnlyCollection<NicknameOutcome> outcomes, string characterName)
	{
		if (outcomes.Contains(NicknameOutcome.Updated))
		{
			return $"\nTon pseudo sur le serveur est maintenant **{For(characterName)}**.";
		}
		if (outcomes.Contains(NicknameOutcome.Forbidden))
		{
			return "\nJe n'ai pas pu changer ton pseudo : Discord ne me laisse pas renommer le propriétaire du serveur, " +
				"ni un membre dont le rôle est au-dessus du mien.";
		}
		if (outcomes.Contains(NicknameOutcome.Failed))
		{
			return "\nJe n'ai pas pu changer ton pseudo pour le moment (erreur Discord), réessaie plus tard.";
		}
		return "";
	}
}
