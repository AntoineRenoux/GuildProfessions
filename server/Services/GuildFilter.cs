using GuildProfessions.Server.Contracts;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Filtre d'appartenance à la guilde, appliqué à toute ingestion (upload
/// compagnon et /import Discord) : un personnage dont la guilde scannée ne
/// correspond pas au nom configuré (Guild:Name) n'entre pas dans l'annuaire —
/// les rerolls hors guilde ne sont pas listés. Sans nom configuré, ou sans
/// info de guilde dans le payload (vieil addon), rien n'est filtré.
/// </summary>
public static class GuildFilter
{
	public static (UploadPayload Payload, List<string> Warnings) Apply(UploadPayload payload, string? expectedGuildName)
	{
		if (string.IsNullOrWhiteSpace(expectedGuildName))
		{
			return (payload, []);
		}

		var warnings = new List<string>();
		var kept = payload.Characters.Where(character =>
		{
			if (character.Guild is null ||
				string.Equals(character.Guild.Trim(), expectedGuildName.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
			warnings.Add($"{character.Name} ignoré : guilde « {character.Guild} » au lieu de « {expectedGuildName} ».");
			return false;
		}).ToList();

		return (payload with { Characters = kept }, warnings);
	}
}
