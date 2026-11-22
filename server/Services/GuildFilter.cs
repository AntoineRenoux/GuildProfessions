using GuildProfessions.Server.Contracts;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Filtre d'appartenance à la guilde, appliqué à toute ingestion (upload
/// compagnon et /import Discord). Un personnage relevé dans une autre guilde
/// n'entre pas dans l'annuaire, et en sort s'il y était (il a changé de
/// guilde). Les noms listés par le compagnon dans DepartedCharacters (le
/// joueur les joue désormais dans une autre guilde) sont traités de même.
/// Sans info de guilde (vieil addon), le personnage est accepté.
/// </summary>
public static class GuildFilter
{
	public sealed record Result(UploadPayload Payload, List<string> Warnings, List<string> Departed);

	public static Result Apply(UploadPayload payload, string guildName)
	{
		var warnings = new List<string>();
		var departed = new List<string>();
		var kept = payload.Characters.Where(character =>
		{
			if (character.Guild is null || SameGuild(character.Guild, guildName))
			{
				return true;
			}
			warnings.Add($"{character.Name} ignoré : guilde « {character.Guild} » au lieu de « {guildName} ».");
			departed.Add(character.Name);
			return false;
		}).ToList();

		var keptNames = kept.Select(c => c.Name).ToHashSet();
		foreach (var name in payload.DepartedCharacters ?? [])
		{
			if (!keptNames.Contains(name) && !departed.Contains(name))
			{
				departed.Add(name);
			}
		}

		return new Result(payload with { Characters = kept, DepartedCharacters = null }, warnings, departed);
	}

	public static bool SameGuild(string a, string b) =>
		string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
