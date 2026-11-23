using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Quetes.Application;
using GuildProfessions.Server.Modules.Quetes.Presentation;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

// Commandes Discord du module Quêtes : /gkeep quest (quêtes partagées).
// Le groupe reste imbriqué dans GkeepModule (Discord ne connaît les sous-commandes que
// par imbrication) et garde son namespace : même hiérarchie /gkeep <groupe> <commande>,
// mêmes clés de traduction dans commands.fr.json. Il ne dépend que de son module.
public sealed partial class GkeepModule
{
	// ---- Quêtes partagées --------------------------------------------------------------------

	[Group("quest", "Elite, dungeon and raid quests guild members have in their quest log")]
	[RequireRegisteredGuild]
	public sealed class QuestGroup(QuestService quests, BotLocalizer l)
		: GuildkeepGroup
	{
		[SlashCommand("list", "Shared quests by zone or dungeon, with who has them")]
		public async Task ListAsync(
			[Summary("kind", "Kind of quest")] [Choice("Elite / Élite", "elite"), Choice("Dungeon / Donjon", "dungeon"), Choice("Raid", "raid")] string? kind = null,
			[Summary("location", "Zone or dungeon (text to find)")] string? location = null)
		{
			await DeferAsync(ephemeral: true);
			var all = (await quests.AllAsync())
				.Where(q => kind is null || q.Kind == kind)
				.Where(q => location is null || (q.Location ?? "").Contains(location.Trim(), StringComparison.OrdinalIgnoreCase))
				.ToList();
			if (all.Count == 0)
			{
				await FollowupAsync(l.T(
					"Aucune quête partagée relevée. L'addon note les quêtes élites, de donjon et de raid du journal de chaque membre.",
					"No shared quest recorded. The addon notes the elite, dungeon and raid quests in each member's quest log."), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder().WithColor(Green).WithTitle(l.T("Quêtes partagées", "Shared quests"));
			foreach (var group in all.GroupBy(q => (q.Kind, q.Location)).OrderBy(g => g.Key.Kind).ThenBy(g => g.Key.Location).Take(20))
			{
				var lines = group.Take(10).Select(q =>
					$"**{q.Title}**{(q.Level is { } lvl ? $" [{lvl}]" : "")} — {string.Join(", ", q.Holders.Select(h => h.Character + (h.Complete ? " ✓" : "")))}");
				embed.AddField($"{QuestService.KindLabel(group.Key.Kind, l)} · {group.Key.Location ?? "?"}", Clip(string.Join("\n", lines), 1024));
			}
			embed.WithFooter(l.T("✓ = objectifs remplis · qui a une quête : /gkeep quete qui", "✓ = objectives done · who has a quest: /gkeep quest who"));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("who", "Who has this quest")]
		public async Task WhoAsync([Summary("quest", "The quest"), Autocomplete(typeof(QuestAutocompleteHandler))] string quest)
		{
			await DeferAsync(ephemeral: true);
			var all = await quests.AllAsync();
			var found = int.TryParse(quest, out var id)
				? all.FirstOrDefault(q => q.Id == id)
				: all.FirstOrDefault(q => q.Title.Contains(quest.Trim(), StringComparison.OrdinalIgnoreCase));
			if (found is null)
			{
				await FollowupAsync(l.T("Personne n'a cette quête dans son journal.", "Nobody has this quest in their log."), ephemeral: true);
				return;
			}
			var lines = found.Holders.Select(h => $"{(h.Complete ? "✅" : "⬜")} **{h.Character}**" +
				(h.SeenUtc is { } seen ? l.T($" · vu <t:{new DateTimeOffset(seen, TimeSpan.Zero).ToUnixTimeSeconds()}:R>", $" · seen <t:{new DateTimeOffset(seen, TimeSpan.Zero).ToUnixTimeSeconds()}:R>") : ""));
			await FollowupAsync(embed: new EmbedBuilder().WithColor(Green)
				.WithTitle($"{found.Title}{(found.Level is { } lvl ? $" [{lvl}]" : "")}")
				.WithDescription($"{QuestService.KindLabel(found.Kind, l)} · {found.Location ?? "?"}\n\n" + string.Join("\n", lines))
				.WithFooter(l.T("✅ objectifs remplis · le partage de quête se fait en jeu", "✅ objectives done · quest sharing happens in game"))
				.Build(), ephemeral: true);
		}

		[SlashCommand("mine", "Your shared quests, and who else has each one")]
		public async Task MineAsync(
			[Summary("character", "One of your characters (default: all of them)"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))] string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var discordId = DiscordId;
			IReadOnlyList<string> names = character is not null
				? [character.Trim()]
				: await quests.MemberCharactersAsync(discordId);
			if (names.Count == 0)
			{
				await FollowupAsync(SharedMessages.NoLinkedCharacter(l), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder().WithColor(Green).WithTitle(l.T("Tes quêtes partagées", "Your shared quests"));
			foreach (var name in names.Take(10))
			{
				var (exact, mine) = await quests.ForCharacterAsync(name);
				if (exact is null)
				{
					continue;
				}
				var lines = mine.Take(15).Select(q =>
				{
					var others = q.Holders.Where(h => h.Character != exact).Select(h => h.Character).ToList();
					return $"**{q.Title}** · {q.Location ?? "?"} — " + (others.Count == 0 ? l.T("_personne d'autre_", "_nobody else_") : string.Join(", ", others));
				});
				embed.AddField(exact, Clip(string.Join("\n", lines), 1024));
			}
			if (embed.Fields.Count == 0)
			{
				embed.WithDescription(l.T("Aucune quête élite, de donjon ou de raid relevée pour tes personnages.", "No elite, dungeon or raid quest recorded for your characters."));
			}
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}
	}
}
