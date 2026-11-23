namespace GuildProfessions.Server.Shared;

/// <summary>Limites de Discord utilisées par plusieurs modules.</summary>
public static class DiscordLimits
{
	/// <summary>Longueur maximale du libellé d'une suggestion d'autocomplétion.</summary>
	public const int AutocompleteName = 100;
}

/// <summary>Coupe des textes saisis ou affichés, partagée par tous les modules.</summary>
public static class Text
{
	/// <summary>Texte saisi, sans espaces autour et coupé à <paramref name="max"/> caractères ; null s'il est vide.</summary>
	public static string? Clip(string? value, int max)
	{
		var trimmed = value?.Trim();
		if (string.IsNullOrEmpty(trimmed))
		{
			return null;
		}
		return trimmed.Length <= max ? trimmed : trimmed[..max];
	}

	/// <summary>Texte affiché, coupé à <paramref name="max"/> caractères avec « ... » s'il dépasse.</summary>
	public static string Ellipsize(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";

	/// <summary>Libellé d'une suggestion d'autocomplétion, dans la limite de Discord.</summary>
	public static string ForAutocomplete(string text) => Ellipsize(text, DiscordLimits.AutocompleteName);
}
