using Discord;
using Discord.Interactions;
using GuildProfessions.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Discord;

/// <summary>Propose uniquement les personnages liés au compte Discord de l'appelant.</summary>
public sealed class OwnCharacterAutocompleteHandler : AutocompleteHandler
{
	public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
		IInteractionContext context, IAutocompleteInteraction autocompleteInteraction,
		IParameterInfo parameter, IServiceProvider services)
	{
		var db = services.GetRequiredService<AppDbContext>();
		var discordId = context.User.Id.ToString();
		var typed = (autocompleteInteraction.Data.Current.Value?.ToString() ?? "").Trim().ToLower();

		var names = await db.Characters.AsNoTracking()
			.Where(c => c.Member != null && c.Member.DiscordId == discordId)
			.Where(c => typed == "" || c.Name.ToLower().Contains(typed))
			.OrderBy(c => c.Name)
			.Select(c => c.Name)
			.Take(25)
			.ToListAsync();

		return AutocompletionResult.FromSuccess(names.Select(name => new AutocompleteResult(name, name)));
	}
}
