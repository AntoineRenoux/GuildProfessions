using Discord.Interactions;
using GuildProfessions.Server.Services;

namespace GuildProfessions.Server.Discord;

public sealed class CompteModule(RosterService roster, NicknameSync nicknames) : InteractionModuleBase<SocketInteractionContext>
{
	[SlashCommand("link", "Lier un personnage à ton compte Discord (le premier devient ton pseudo)")]
	public async Task LinkAsync(
		[Summary("personnage", "Nom du personnage en jeu")] string personnage)
	{
		await DeferAsync();
		var discordId = Context.User.Id.ToString();
		var result = await roster.LinkCharacterAsync(discordId, Context.User.Username, personnage.Trim());
		var nicknameNote = "";
		if (result.Success && await roster.GetMainCharacterNameAsync(discordId) is { } main)
		{
			nicknameNote = NicknameSync.Describe(await nicknames.SyncAsync(discordId, main), main);
		}
		await FollowupAsync(result.Message + nicknameNote);
	}

	[SlashCommand("main", "Choisir le personnage dont le nom devient ton pseudo sur le serveur")]
	public async Task MainAsync(
		[Summary("personnage", "Un de tes personnages liés"), Autocomplete(typeof(OwnCharacterAutocompleteHandler))] string personnage)
	{
		await DeferAsync(ephemeral: true);
		var discordId = Context.User.Id.ToString();
		var result = await roster.SetMainCharacterAsync(discordId, personnage.Trim());
		var nicknameNote = "";
		if (result.Success && await roster.GetMainCharacterNameAsync(discordId) is { } main)
		{
			nicknameNote = NicknameSync.Describe(await nicknames.SyncAsync(discordId, main), main) +
				"\n_Ce pseudo est réappliqué automatiquement : pour le changer, utilise `/main` plutôt que les réglages Discord._";
		}
		await FollowupAsync(result.Message + nicknameNote, ephemeral: true);
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
