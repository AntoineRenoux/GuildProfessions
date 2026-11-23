using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Tableau.Application;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Discord;

// Commande Discord du module Tableau : /gkeep home show, le même résumé que l'onglet Tableau en jeu.
public sealed partial class GkeepModule
{
	[Group("home", "Your dashboard: what's coming up and what's waiting for you")]
	[RequireRegisteredGuild]
	public sealed class HomeGroup(HomeDashboard dashboard, BotLocalizer l) : GuildkeepGroup
	{
		[SlashCommand("show", "Upcoming events, guild news, your orders, attendance and onboarding")]
		public async Task ShowAsync()
		{
			await DeferAsync(ephemeral: true);
			var view = await dashboard.BuildAsync(new HomeViewer(DiscordId, IsServerAdmin));
			var embed = new EmbedBuilder().WithColor(Amber).WithTitle(l.T("Ton tableau de bord", "Your dashboard"));
			foreach (var card in view.Cards.Take(25))
			{
				var lines = card.Lines.Count == 0 ? "—" : string.Join("\n", card.Lines);
				if (card.Command is { } command)
				{
					lines += $"\n→ `{command}`";
				}
				embed.AddField(card.Title, Text.Ellipsize(lines, 1024));
			}
			if (view.Cards.Count == 0)
			{
				embed.WithDescription(l.T("Rien à afficher pour l'instant.", "Nothing to show yet."));
			}
			if (view.FailedModules.Count > 0)
			{
				embed.WithFooter(l.T("Certaines sections sont momentanément indisponibles.", "Some sections are temporarily unavailable."));
			}
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}
	}
}
