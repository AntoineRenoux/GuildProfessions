using GuildProfessions.Server.Contracts;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Filtre d'appartenance à la guilde, appliqué à toute ingestion (upload
/// compagnon et /import Discord). Un personnage relevé dans une autre guilde
/// n'entre pas dans l'annuaire, et en sort s'il y était (il a changé de
/// guilde). Les noms listés par le compagnon dans DepartedCharacters (le
/// joueur les joue désormais dans une autre guilde) sont traités de même.
/// Sans info de guilde (vieil addon), le personnage est accepté.
/// Un personnage présent deux fois dans l'envoi (deux comptes sur le même
/// poste, import fusionné) n'est gardé qu'une fois : le dernier relevé.
/// </summary>
public static class GuildFilter
{
	public sealed record Rejection(string Character, string Guild);

	public sealed record Result(UploadPayload Payload, List<Rejection> Rejected, List<string> Departed);

	public static Result Apply(UploadPayload payload, string guildName)
	{
		var rejected = new List<Rejection>();
		var departed = new List<string>();
		// System.Text.Json laisse passer null : un envoi sans liste de personnages vaut une liste vide.
		var kept = (payload.Characters ?? []).Where(character =>
		{
			if (character.Guild is null || SameGuild(character.Guild, guildName))
			{
				return true;
			}
			rejected.Add(new Rejection(character.Name, character.Guild));
			departed.Add(character.Name);
			return false;
		})
			.GroupBy(character => character.Name)
			.Select(group => group.Last())
			.ToList();

		var keptNames = kept.Select(c => c.Name).ToHashSet();
		foreach (var name in payload.DepartedCharacters ?? [])
		{
			if (!keptNames.Contains(name) && !departed.Contains(name))
			{
				departed.Add(name);
			}
		}

		return new Result(payload with { Characters = kept, DepartedCharacters = null }, rejected, departed);
	}

	public static bool SameGuild(string a, string b) =>
		string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
