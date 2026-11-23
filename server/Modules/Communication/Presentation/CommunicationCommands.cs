using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Communication.Application;
using GuildProfessions.Server.Modules.Communication.Presentation;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

// Commandes Discord du module Communication : /gkeep announce, /gkeep handbook, /gkeep goal.
// Groupes imbriqués dans GkeepModule (Discord ne connaît les sous-commandes que par
// imbrication) ; ils ne dépendent que de leur module (les événements proposés viennent du
// contrat public du module Événements).
public sealed partial class GkeepModule
{
	// ---- Annonces -------------------------------------------------------------------------------

	[Group("announce", "Guild announcements and their templates")]
	[RequireRegisteredGuild]
	public sealed class AnnounceGroup(CommunicationService communication, BotLocalizer l) : GuildkeepGroup
	{
		[SlashCommand("post", "Post an announcement to the guild (officer)")]
		public async Task PostAsync(
			[Summary("title", "Title (or leave empty with a template)"), MaxLength(CommunicationService.TitleMax)] string? title = null,
			[Summary("text", "Text; {event}, {date} and {guild} are replaced"), MaxLength(CommunicationService.BodyMax)] string? text = null,
			[Summary("template", "Start from a saved template"), Autocomplete(typeof(TemplateAutocompleteHandler))] string? template = null,
			[Summary("event", "Event this announcement is about"), Autocomplete(typeof(AnnouncementEventAutocompleteHandler))] int? @event = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.PostFromDiscordAsync(DiscordId, IsServerAdmin, title, text, template, @event);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("list", "Recent announcements")]
		public async Task ListAsync()
		{
			await DeferAsync(ephemeral: true);
			var list = await communication.RecentAnnouncementsAsync();
			if (list.Count == 0)
			{
				await FollowupAsync(l.T("Aucune annonce pour l'instant.", "No announcement yet."), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder().WithColor(Amber).WithTitle(l.T("Annonces de la guilde", "Guild announcements"));
			foreach (var a in list)
			{
				embed.AddField($"#{a.Id} · {a.Title}", Text.Ellipsize($"{a.Body}\n— {a.Author} · <t:{a.CreatedAt}:R>", 1024));
			}
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("delete", "Remove an announcement (officer)")]
		public async Task DeleteAsync([Summary("announcement", "The announcement"), Autocomplete(typeof(AnnouncementAutocompleteHandler))] int announcement)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.DeleteAnnouncementFromDiscordAsync(DiscordId, IsServerAdmin, announcement);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("channel", "Channel where announcements are posted (server managers)")]
		public async Task ChannelAsync([Summary("channel", "The channel (leave empty to stop posting)")] ITextChannel? channel = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.SetChannelFromDiscordAsync(IsServerAdmin, channel?.Id.ToString());
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("template-save", "Save an announcement template (officer)")]
		public async Task TemplateSaveAsync(
			[Summary("template", "Template name"), Autocomplete(typeof(TemplateAutocompleteHandler)), MaxLength(CommunicationService.KeyMax)] string template,
			[Summary("title", "Title"), MaxLength(CommunicationService.TitleMax)] string title,
			[Summary("text", "Text; {event}, {date} and {guild} are replaced"), MaxLength(CommunicationService.BodyMax)] string text)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.SaveTemplateFromDiscordAsync(DiscordId, IsServerAdmin, template, title, text, false);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("template-list", "Saved announcement templates")]
		public async Task TemplateListAsync()
		{
			await DeferAsync(ephemeral: true);
			var templates = await communication.TemplatesAsync();
			await FollowupAsync(templates.Count == 0
				? l.T("Aucun modèle : `/gkeep annonce modele-enregistrer`.", "No template: `/gkeep announce template-save`.")
				: Text.Ellipsize(string.Join("\n", templates.Select(t => $"**{t.Key}** · {t.Title}")), 2000), ephemeral: true);
		}

		[SlashCommand("template-delete", "Delete an announcement template (officer)")]
		public async Task TemplateDeleteAsync([Summary("template", "Template name"), Autocomplete(typeof(TemplateAutocompleteHandler))] string template)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.SaveTemplateFromDiscordAsync(DiscordId, IsServerAdmin, template, null, null, true);
			await FollowupAsync(result.Message, ephemeral: true);
		}
	}

	// ---- Charte ---------------------------------------------------------------------------------

	[Group("handbook", "The guild handbook: rules, loot rules...")]
	[RequireRegisteredGuild]
	public sealed class HandbookGroup(CommunicationService communication, BotLocalizer l) : GuildkeepGroup
	{
		[SlashCommand("show", "Read the handbook, or one of its sections")]
		public async Task ShowAsync([Summary("section", "Only this section"), Autocomplete(typeof(HandbookAutocompleteHandler))] string? section = null)
		{
			await DeferAsync(ephemeral: true);
			var sections = await communication.HandbookAsync();
			var key = CommunicationService.NormalizeKey(section);
			var shown = key is null ? sections : sections.Where(s => s.Key == key).ToList();
			if (shown.Count == 0)
			{
				await FollowupAsync(l.T("La charte est vide (ou cette section n'existe pas).", "The handbook is empty (or this section doesn't exist)."), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder().WithColor(Amber).WithTitle(l.T("Charte de la guilde", "Guild handbook"));
			foreach (var s in shown.Take(25))
			{
				embed.AddField(s.Title, Text.Ellipsize(s.Body, 1024));
			}
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("edit", "Write or update a handbook section (officer)")]
		public async Task EditAsync(
			[Summary("section", "Section key, e.g. rules or loot"), Autocomplete(typeof(HandbookAutocompleteHandler)), MaxLength(CommunicationService.KeyMax)] string section,
			[Summary("text", "Section text"), MaxLength(CommunicationService.HandbookBodyMax)] string text,
			[Summary("title", "Section title (default: unchanged)"), MaxLength(CommunicationService.TitleMax)] string? title = null,
			[Summary("order", "Position in the handbook (0 first)"), MinValue(0), MaxValue(100)] int? order = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.EditHandbookFromDiscordAsync(DiscordId, IsServerAdmin, section, title, text, order, false);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("remove", "Remove a handbook section (officer)")]
		public async Task RemoveAsync([Summary("section", "The section"), Autocomplete(typeof(HandbookAutocompleteHandler))] string section)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.EditHandbookFromDiscordAsync(DiscordId, IsServerAdmin, section, null, null, null, true);
			await FollowupAsync(result.Message, ephemeral: true);
		}
	}

	// ---- Objectifs ------------------------------------------------------------------------------

	[Group("goal", "Guild goals and members' contributions")]
	[RequireRegisteredGuild]
	public sealed class GoalGroup(CommunicationService communication, GuildContext guildContext, BotLocalizer l) : GuildkeepGroup
	{
		[SlashCommand("list", "Guild goals and their progress")]
		public async Task ListAsync([Summary("closed", "Include closed goals")] bool closed = false)
		{
			await DeferAsync(ephemeral: true);
			var goals = await communication.GoalsAsync(closed);
			if (goals.Count == 0)
			{
				await FollowupAsync(l.T("Aucun objectif en cours.", "No goal in progress."), ephemeral: true);
				return;
			}
			var embed = new EmbedBuilder().WithColor(Green).WithTitle(l.T("Objectifs de la guilde", "Guild goals"));
			foreach (var g in goals.Take(20))
			{
				var progress = g.Contributions.Sum(c => c.Amount);
				embed.AddField($"#{g.Id} · {g.Title}{(g.Closed ? l.T(" (clos)", " (closed)") : "")}",
					$"{CommunicationService.ProgressBar(progress, g.Target)} — {progress}/{g.Target} {g.Unit}" +
					(g.Deadline is { } d ? $" · ⏳ <t:{d}:R>" : ""));
			}
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("show", "A goal and who contributed")]
		public async Task ShowAsync([Summary("goal", "The goal"), Autocomplete(typeof(GoalAutocompleteHandler))] int goal)
		{
			await DeferAsync(ephemeral: true);
			var g = await communication.GoalAsync(goal);
			if (g is null)
			{
				await FollowupAsync(l.T($"Objectif #{goal} introuvable.", $"Goal #{goal} not found."), ephemeral: true);
				return;
			}
			var progress = g.Contributions.Sum(c => c.Amount);
			var top = g.Contributions.GroupBy(c => c.Character)
				.Select(x => (Name: x.Key, Total: x.Sum(c => c.Amount)))
				.OrderByDescending(x => x.Total).Take(15)
				.Select(x => $"**{x.Name}** : {x.Total} {g.Unit}");
			var embed = new EmbedBuilder().WithColor(Green)
				.WithTitle($"#{g.Id} · {g.Title}")
				.WithDescription($"{g.Description}\n\n{CommunicationService.ProgressBar(progress, g.Target)} — {progress}/{g.Target} {g.Unit}" +
					(g.Deadline is { } d ? l.T($"\nÉchéance : <t:{d}:F>", $"\nDeadline: <t:{d}:F>") : ""))
				.AddField(l.T("Contributions", "Contributions"), string.Join("\n", top) is { Length: > 0 } t ? Text.Ellipsize(t, 1024) : "—");
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("create", "Create a guild goal (officer)")]
		public async Task CreateAsync(
			[Summary("title", "What to reach"), MaxLength(CommunicationService.TitleMax)] string title,
			[Summary("target", "Target amount"), MinValue(1), MaxValue(CommunicationService.TargetMax)] int target,
			[Summary("unit", "Unit (flasks, gold...)"), MaxLength(CommunicationService.UnitMax)] string? unit = null,
			[Summary("deadline", "Deadline day: 29/11, 29/11/2026 or 2026-11-29")] string? deadline = null,
			[Summary("details", "Details"), MaxLength(CommunicationService.BodyMax)] string? details = null)
		{
			await DeferAsync(ephemeral: true);
			long? due = null;
			if (deadline is not null)
			{
				var zone = GuildTime.DefaultTimeZone(guildContext.Guild?.Locale);
				if (!GuildTime.TryParseStart(deadline, "23:59", zone, DateTimeOffset.UtcNow, out var parsed))
				{
					await FollowupAsync(l.T($"Date illisible (« {deadline} »).", $"Unreadable date (\"{deadline}\")."), ephemeral: true);
					return;
				}
				due = parsed;
			}
			var result = await communication.CreateGoalFromDiscordAsync(DiscordId, IsServerAdmin, title, target, unit, due, details);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("contribute", "Add your contribution to a goal")]
		public async Task ContributeAsync(
			[Summary("goal", "The goal"), Autocomplete(typeof(GoalAutocompleteHandler))] int goal,
			[Summary("amount", "How much you bring"), MinValue(1), MaxValue(CommunicationService.TargetMax)] int amount,
			[Summary("note", "A word (optional)"), MaxLength(CommunicationService.NoteMax)] string? note = null,
			[Summary("character", "One of your characters (default: your main)"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))] string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.ContributeFromDiscordAsync(DiscordId, goal, amount, note, character);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("close", "Close a goal (officer)")]
		public async Task CloseAsync([Summary("goal", "The goal"), Autocomplete(typeof(GoalAutocompleteHandler))] int goal)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.SetGoalClosedFromDiscordAsync(DiscordId, IsServerAdmin, goal, true);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("reopen", "Reopen a closed goal (officer)")]
		public async Task ReopenAsync([Summary("goal", "The goal"), Autocomplete(typeof(GoalAutocompleteHandler))] int goal)
		{
			await DeferAsync(ephemeral: true);
			var result = await communication.SetGoalClosedFromDiscordAsync(DiscordId, IsServerAdmin, goal, false);
			await FollowupAsync(result.Message, ephemeral: true);
		}
	}
}
