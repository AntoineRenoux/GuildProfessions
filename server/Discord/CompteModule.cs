using Discord.Interactions;
using GuildProfessions.Server.Services;

namespace GuildProfessions.Server.Discord;

public sealed class CompteModule(RosterService roster) : InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("link", "Lier un personnage à ton compte Discord")]
	public async Task LinkAsync(
		[Summary("personnage", "Nom du personnage en jeu")] string personnage)
	{
		await DeferAsync();
		var result = await roster.LinkCharacterAsync(
			Context.User.Id.ToString(), Context.User.Username, personnage.Trim());
		await FollowupAsync(result.Message);
	}

	[SlashCommand("token", "Obtenir ton token personnel pour l'app compagnon")]
	public async Task TokenAsync()
	{
		await DeferAsync(ephemeral: true);
		var token = await roster.GetOrCreateUploadTokenAsync(
			Context.User.Id.ToString(), Context.User.Username);
		await FollowupAsync(
			"Ton token pour l'app compagnon (à coller dans son `config.json`, ne le partage pas) :\n" +
			$"```\n{token}\n```", ephemeral: true);
	}
}
