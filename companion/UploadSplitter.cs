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
			// Événements : chacun part à la guilde du personnage qui agit.
			var events = payload.Events?.Where(e => Mine(e.By)).ToList();
			var signups = payload.Signups?.Where(s => Mine(s.Character)).ToList();
			var placements = payload.Placements?.Where(p => Mine(p.By)).ToList();
			var reserves = payload.Reserves?.Where(r => Mine(r.By)).ToList();
			var hardReserves = payload.HardReserves?.Where(h => Mine(h.By)).ToList();
			var duties = payload.Duties?.Where(d => Mine(d.By)).ToList();
			// Présence et butin : à la guilde du personnage qui a relevé ou corrigé.
			var raids = payload.Raids?.Where(r => Mine(r.Observer)).ToList();
			var loot = payload.Loot?.Where(l => Mine(l.Observer)).ToList();
			var attendanceCorrections = payload.AttendanceCorrections?.Where(c => Mine(c.By)).ToList();
			var lootCorrections = payload.LootCorrections?.Where(c => Mine(c.By)).ToList();
			var offers = payload.Offers?.Where(o => Mine(o.Crafter)).ToList();
			var communication = CommunicationLua.ForGuild(payload.Communication, Mine);
			var profiles = ProfilesLua.ForGuild(payload.Profiles, Mine);

			if (characters.Count == 0 && orders is not { Count: > 0 } && actions is not { Count: > 0 } && departed.Count == 0 &&
				events is not { Count: > 0 } && signups is not { Count: > 0 } && placements is not { Count: > 0 } &&
				reserves is not { Count: > 0 } && hardReserves is not { Count: > 0 } && duties is not { Count: > 0 } &&
				raids is not { Count: > 0 } && loot is not { Count: > 0 } && attendanceCorrections is not { Count: > 0 } && lootCorrections is not { Count: > 0 } &&
				offers is not { Count: > 0 } && communication is null && profiles is null)
			{
				continue;
			}
			uploads.Add(new GuildUpload(guild, new UploadPayload(
				characters,
				orders is { Count: > 0 } ? orders : null,
				actions,
				departed.Count > 0 ? departed : null,
				events is { Count: > 0 } ? events : null,
				signups is { Count: > 0 } ? signups : null,
				placements is { Count: > 0 } ? placements : null,
				reserves is { Count: > 0 } ? reserves : null,
				hardReserves is { Count: > 0 } ? hardReserves : null,
				duties is { Count: > 0 } ? duties : null,
				raids is { Count: > 0 } ? raids : null,
				loot is { Count: > 0 } ? loot : null,
				attendanceCorrections is { Count: > 0 } ? attendanceCorrections : null,
				lootCorrections is { Count: > 0 } ? lootCorrections : null,
				offers is { Count: > 0 } ? offers : null,
				communication,
				profiles)));
		}
		return uploads;
	}
}
