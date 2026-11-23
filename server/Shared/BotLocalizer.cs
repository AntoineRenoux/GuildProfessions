namespace GuildProfessions.Server.Shared;

public enum BotLanguage
{
	French,
	English,
}

/// <summary>
/// Langue des réponses du bot pour un traitement : celle de l'utilisateur
/// Discord pour une interaction, celle de la guilde sinon (API, messages
/// privés). Les textes sont écrits en paire au point d'usage : T(fr, en).
/// </summary>
public sealed class BotLocalizer
{
	public BotLanguage Language { get; set; } = BotLanguage.French;

	public bool IsFrench => Language == BotLanguage.French;

	public string T(string french, string english) => IsFrench ? french : english;

	/// <summary>Code de langue Discord (« fr », « en-US »…) → langue du bot ; tout sauf le français donne l'anglais.</summary>
	public static BotLanguage FromLocale(string? locale) =>
		locale is not null && locale.StartsWith("fr", StringComparison.OrdinalIgnoreCase) ? BotLanguage.French : BotLanguage.English;

	public static string ToLocale(BotLanguage language) => language == BotLanguage.French ? "fr" : "en";

	public static BotLocalizer For(BotLanguage language) => new() { Language = language };
}
