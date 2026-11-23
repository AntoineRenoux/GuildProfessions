using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Modules.Profils.Application;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Discord;

// Commandes Discord du module Profils : /gkeep profile et /gkeep welcome. Mêmes droits qu'en
// jeu : chacun son profil et son accueil ; les officiers suivent l'accueil des nouveaux.
public sealed partial class GkeepModule
{
	// ---- Profils --------------------------------------------------------------------------------

	[Group("profile", "Player profiles: main, alts, roles, availability")]
	[RequireRegisteredGuild]
	public sealed class ProfileGroup(ProfileService profiles, BotLocalizer l) : GuildkeepGroup
	{
		[SlashCommand("show", "Show a player's profile (yours by default)")]
		public async Task ShowAsync([Summary("character", "Any character of the player"), Autocomplete(typeof(CharacterAutocompleteHandler))] string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var target = await profiles.TargetAsync(DiscordId, character);
			var profile = target is null ? null : await profiles.FindProfileAsync(target);
			if (profile is null)
			{
				await FollowupAsync(target is null
					? SharedMessages.NoLinkedCharacter(l)
					: l.T($"**{target}** n'a pas encore de profil : `/gkeep profil modifier` (ou l'onglet Profils en jeu).",
						$"**{target}** has no profile yet: `/gkeep profile edit` (or the Profiles tab in game)."), ephemeral: true);
				return;
			}
			var alts = ProfileService.AltsOf(profile);
			var roles = profile.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => ProfileService.RoleLabel(r, l)).ToList();
			var embed = new EmbedBuilder().WithColor(Amber).WithTitle(profile.Main)
				.AddField(l.T("Rerolls", "Alts"), alts.Count == 0 ? "—" : string.Join(", ", alts), inline: true)
				.AddField(l.T("Rôles", "Roles"), roles.Count == 0 ? "—" : string.Join(", ", roles), inline: true)
				.AddField(l.T("Disponibilités", "Availability"), profile.Availability ?? "—");
			if (profile.Note is { } note)
			{
				embed.AddField(l.T("Note", "Note"), note);
			}
			var talents = new List<string>();
			foreach (var name in alts.Prepend(profile.Main))
			{
				if (await profiles.TalentsAsync(name) is { } t)
				{
					talents.Add($"**{name}** : {(t.Spec is null ? "" : t.Spec + " ")}({t.Summary})");
				}
			}
			if (talents.Count > 0)
			{
				embed.AddField(l.T("Talents", "Talents"), Text.Ellipsize(string.Join("\n", talents), 1024));
			}
			embed.WithFooter(l.T("Mis à jour", "Updated")).WithTimestamp(DateTimeOffset.FromUnixTimeSeconds(profile.UpdatedAt));
			await FollowupAsync(embed: embed.Build(), ephemeral: true);
		}

		[SlashCommand("edit", "Fill in your profile; your other linked characters become your alts")]
		public async Task EditAsync(
			[Summary("role", "Your main role"), Choice("Tank", "tank"), Choice("Healer / Soigneur", "healer"), Choice("Melee DPS / DPS mêlée", "melee"), Choice("Ranged DPS / DPS distance", "ranged")] string? role = null,
			[Summary("second-role", "Another role you can play"), Choice("Tank", "tank"), Choice("Healer / Soigneur", "healer"), Choice("Melee DPS / DPS mêlée", "melee"), Choice("Ranged DPS / DPS distance", "ranged")] string? secondRole = null,
			[Summary("availability", "When you usually play, e.g. weekday evenings"), MaxLength(ProfileService.AvailabilityMax)] string? availability = null,
			[Summary("note", "A word for the officers"), MaxLength(ProfileService.NoteMax)] string? note = null,
			[Summary("main", "Your main character (default: your Discord main)"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))] string? main = null)
		{
			await DeferAsync(ephemeral: true);
			List<string>? roles = role is null && secondRole is null ? null : new[] { role, secondRole }.OfType<string>().Distinct().ToList();
			var result = await profiles.EditFromDiscordAsync(DiscordId, roles, availability, note, main);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("arrivals", "Recent arrivals and departures in the guild")]
		public async Task ArrivalsAsync([Summary("days", "How many days back (default 14)"), MinValue(1), MaxValue(90)] int days = 14)
		{
			await DeferAsync(ephemeral: true);
			var journal = await profiles.JournalAsync(days);
			if (journal.Count == 0)
			{
				await FollowupAsync(l.T($"Aucune arrivée ni départ relevé ces {days} derniers jours.", $"No arrival or departure seen in the last {days} days."), ephemeral: true);
				return;
			}
			var lines = journal.Select(r => $"{(r.Kind == "join" ? "➕" : "➖")} **{r.Character}** · <t:{r.At}:R>");
			await FollowupAsync(Text.Ellipsize(string.Join("\n", lines), 2000), ephemeral: true);
		}

		[SlashCommand("versions", "Addon version of each member (officer)")]
		public async Task VersionsAsync()
		{
			await DeferAsync(ephemeral: true);
			if (!await profiles.IsOfficerAsync(DiscordId, IsServerAdmin))
			{
				await FollowupAsync(l.T("Réservé aux officiers.", "Officers only."), ephemeral: true);
				return;
			}
			var versions = await profiles.VersionsAsync();
			if (versions.Count == 0)
			{
				await FollowupAsync(l.T("Aucune version relevée pour l'instant.", "No version reported yet."), ephemeral: true);
				return;
			}
			var lines = versions.GroupBy(v => v.Version).OrderByDescending(g => g.Key, StringComparer.Ordinal)
				.Select(g => $"**{g.Key}** ({g.Count()}) : {string.Join(", ", g.Select(v => v.Character).Order())}");
			await FollowupAsync(Text.Ellipsize(string.Join("\n", lines), 2000), ephemeral: true);
		}
	}

	// ---- Accueil des nouveaux -------------------------------------------------------------------

	[Group("welcome", "Newcomer onboarding checklist")]
	[RequireRegisteredGuild]
	public sealed class WelcomeGroup(ProfileService profiles, BotLocalizer l) : GuildkeepGroup
	{
		[SlashCommand("status", "Onboarding checklist of a player (yours by default)")]
		public async Task StatusAsync([Summary("character", "The player's character"), Autocomplete(typeof(CharacterAutocompleteHandler))] string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var target = await profiles.TargetAsync(DiscordId, character);
			if (target is null)
			{
				await FollowupAsync(SharedMessages.NoLinkedCharacter(l), ephemeral: true);
				return;
			}
			var all = await profiles.OnboardingAsync();
			var profile = await profiles.FindProfileAsync(target);
			var done = all.TryGetValue(target, out var state) ? state.Done
				: profile is not null && all.TryGetValue(profile.Main, out var mainState) ? mainState.Done : [];
			var lines = ProfileService.Steps.Select(s => $"{(done.Contains(s) ? "✅" : "⬜")} {ProfileService.StepLabel(s, l)} (`{s}`)");
			await FollowupAsync(embed: new EmbedBuilder().WithColor(Amber)
				.WithTitle(l.T($"Accueil de {target}", $"{target}'s onboarding"))
				.WithDescription(string.Join("\n", lines)).Build(), ephemeral: true);
		}

		[SlashCommand("done", "Tick an onboarding step (yours, or a player's for officers)")]
		public async Task DoneAsync(
			[Summary("step", "The step"), Choice("Discord", "discord"), Choice("Main / Principal", "main"), Choice("Profile / Profil", "profile"), Choice("Handbook / Charte", "handbook"), Choice("Introduction / Présentation", "intro")] string step,
			[Summary("character", "Another player (officer)"), Autocomplete(typeof(CharacterAutocompleteHandler))] string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await profiles.SetStepFromDiscordAsync(DiscordId, IsServerAdmin, step, true, character);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("undo", "Untick an onboarding step")]
		public async Task UndoAsync(
			[Summary("step", "The step"), Choice("Discord", "discord"), Choice("Main / Principal", "main"), Choice("Profile / Profil", "profile"), Choice("Handbook / Charte", "handbook"), Choice("Introduction / Présentation", "intro")] string step,
			[Summary("character", "Another player (officer)"), Autocomplete(typeof(CharacterAutocompleteHandler))] string? character = null)
		{
			await DeferAsync(ephemeral: true);
			var result = await profiles.SetStepFromDiscordAsync(DiscordId, IsServerAdmin, step, false, character);
			await FollowupAsync(result.Message, ephemeral: true);
		}

		[SlashCommand("list", "Newcomers whose onboarding is not finished (officer)")]
		public async Task ListAsync()
		{
			await DeferAsync(ephemeral: true);
			if (!await profiles.IsOfficerAsync(DiscordId, IsServerAdmin))
			{
				await FollowupAsync(l.T("Réservé aux officiers.", "Officers only."), ephemeral: true);
				return;
			}
			var incomplete = await profiles.IncompleteAsync();
			await FollowupAsync(incomplete.Count == 0
				? l.T("Tous les nouveaux ont fini leur accueil.", "Every newcomer finished their onboarding.")
				: Text.Ellipsize(string.Join("\n", incomplete.Select(i =>
					$"**{i.Character}** · {ProfileService.Steps.Length - i.Missing.Count}/{ProfileService.Steps.Length} · {l.T("reste", "left")} : {string.Join(", ", i.Missing)}")), 2000),
				ephemeral: true);
		}

		[SlashCommand("remind", "Post a reminder mentioning newcomers with steps left (officer)")]
		public async Task RemindAsync()
		{
			await DeferAsync(ephemeral: true);
			if (!await profiles.IsOfficerAsync(DiscordId, IsServerAdmin))
			{
				await FollowupAsync(l.T("Réservé aux officiers.", "Officers only."), ephemeral: true);
				return;
			}
			var targets = (await profiles.IncompleteAsync()).Where(i => i.DiscordId is not null).Take(20).ToList();
			if (targets.Count == 0)
			{
				await FollowupAsync(l.T("Personne à relancer (ou pas de compte Discord lié).", "Nobody to remind (or no linked Discord account)."), ephemeral: true);
				return;
			}
			var lines = targets.Select(t => $"<@{t.DiscordId}> ({t.Character}) : {string.Join(", ", t.Missing.Select(s => ProfileService.StepLabel(s, l)))}");
			var text = l.T("👋 Bienvenue ! Il vous reste quelques étapes d'accueil (`/gkeep accueil etat`) :\n",
				"👋 Welcome! A few onboarding steps are left (`/gkeep welcome status`):\n") + string.Join("\n", lines);
			await Context.Channel.SendMessageAsync(Text.Ellipsize(text, 2000),
				allowedMentions: new AllowedMentions { UserIds = targets.Select(t => ulong.Parse(t.DiscordId!)).ToList() });
			await FollowupAsync(l.T($"Rappel envoyé à {targets.Count} joueur(s).", $"Reminder sent to {targets.Count} player(s)."), ephemeral: true);
		}
	}
}
