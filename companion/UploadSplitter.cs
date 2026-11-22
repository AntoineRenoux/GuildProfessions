namespace GuildProfessions.Companion;

/// <summary>
/// Répartit le contenu d'un fichier SavedVariables entre les guildes du poste,
/// d'après la guilde relevée en jeu pour chaque personnage. Un personnage
/// n'est jamais envoyé à une autre guilde que la sienne ; chaque guilde reçoit
/// en plus le nom des personnages du joueur partis ailleurs, pour les retirer.
/// </summary>
public static class UploadSplitter
{
	public sealed record GuildUpload(GuildEntry Guild, UploadPayload Payload);

	public static List<GuildUpload> Split(UploadPayload payload, IReadOnlyList<GuildEntry> guilds, out List<string> unregisteredGuilds)
	{
		var guildOf = payload.Characters
			.Where(c => !string.IsNullOrWhiteSpace(c.Guild))
			.ToDictionary(c => c.Name, c => c.Guild!.Trim());
		unregisteredGuilds = guildOf.Values
			.Where(name => !guilds.Any(g => GuildStore.SameGuild(g.Name, name)))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();

		var uploads = new List<GuildUpload>();
		foreach (var guild in guilds.Where(g => g.Name.Length > 0))
		{
			bool Mine(string? character) =>
				character is not null && guildOf.TryGetValue(character, out var name) && GuildStore.SameGuild(name, guild.Name);

			var characters = payload.Characters.Where(c => Mine(c.Name)).ToList();
			var departed = guildOf.Keys.Where(name => !Mine(name)).Order().ToList();
			var orders = payload.Orders?.Where(o => Mine(o.Requester)).ToList();
			// Les actions visent un numéro de commande unique sur le serveur, qui
			// ignore celles des autres guildes : elles partent partout.
			var actions = payload.OrderActions;

			if (characters.Count == 0 && orders is not { Count: > 0 } && actions is not { Count: > 0 } && departed.Count == 0)
			{
				continue;
			}
			uploads.Add(new GuildUpload(guild, new UploadPayload(
				characters,
				orders is { Count: > 0 } ? orders : null,
				actions,
				departed.Count > 0 ? departed : null)));
		}
		return uploads;
	}
}
