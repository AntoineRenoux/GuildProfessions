using Discord;
using Discord.WebSocket;
using GuildProfessions.Server.Modules.Communication.Application;

namespace GuildProfessions.Server.Modules.Communication.Presentation;

/// <summary>Publie une annonce dans le salon Discord choisi par la guilde ; sans bot connecté, ne fait rien.</summary>
public sealed class DiscordAnnouncementPublisher(DiscordSocketClient client) : IAnnouncementPublisher
{
	private static readonly Color Gold = new(0xE0, 0xB0, 0x2B);

	public async Task PublishAsync(string channelId, string title, string body, string author, CancellationToken ct = default)
	{
		if (client.ConnectionState != ConnectionState.Connected || !ulong.TryParse(channelId, out var id))
		{
			return;
		}
		if (await client.GetChannelAsync(id) is IMessageChannel channel)
		{
			var embed = new EmbedBuilder()
				.WithColor(Gold)
				.WithTitle($"📣 {title}")
				.WithDescription(body.Length <= 4000 ? body : body[..3997] + "...")
				.WithFooter(author)
				.Build();
			await channel.SendMessageAsync(embed: embed);
		}
	}
}
