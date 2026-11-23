using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Annuaire.Contracts;

/// <summary>Personnage lié à un membre Discord, tel que l'annuaire le connaît.</summary>
public sealed record LinkedCharacter(int Id, string Name, string? ClassFile, bool IsOfficer);

/// <summary>
/// Qui agit sur Discord : ses personnages liés, son principal, et s'il est
/// officier (un de ses personnages a le rang d'officier, ou il gère le serveur).
/// </summary>
public sealed record DiscordActor(string DiscordId, string? DiscordName, IReadOnlyList<LinkedCharacter> Characters,
	LinkedCharacter? Main, bool IsOfficer)
{
	public bool Owns(string character) => Characters.Any(c => c.Name == character);

	/// <summary>Un de ses personnages, sans tenir compte de la casse.</summary>
	public LinkedCharacter? Find(string character) =>
		Characters.FirstOrDefault(c => string.Equals(c.Name, character.Trim(), StringComparison.OrdinalIgnoreCase));

	/// <summary>Le personnage nommé, sinon le principal.</summary>
	public LinkedCharacter? Choose(string? character) => character is not null ? Find(character) : Main;
}

/// <summary>
/// Identité d'un envoi : les personnages pour lesquels il peut agir, et ceux
/// qui y sont officiers. Règle commune à tous les modules.
/// </summary>
public sealed record UploadIdentity(IReadOnlySet<string> Characters, IReadOnlySet<string> Officers)
{
	public bool Owns(string? character) => character is not null && Characters.Contains(character);

	public bool IsOfficer(string? character) => character is not null && Officers.Contains(character);
}

/// <summary>Fiche courte d'un personnage de l'annuaire.</summary>
public sealed record CharacterCard(string Name, string? ClassFile, DateTime? LastSeenUtc);

/// <summary>Propriétaire d'un token personnel : sa guilde et son identifiant de membre.</summary>
public sealed record UploadTokenOwner(int GuildId, int MemberId);

/// <summary>Membre Discord et nom de son personnage principal, pour réaligner son pseudo.</summary>
public sealed record NicknameTarget(string DiscordId, string CharacterName);

/// <summary>Taille de l'annuaire de la guilde.</summary>
public sealed record DirectorySize(int Members, int Characters);

/// <summary>Recettes connues des personnages d'un membre.</summary>
public sealed record MemberRecipes(IReadOnlySet<int> SpellIds, IReadOnlySet<string> LowerNames);

/// <summary>
/// Contrat public du module Annuaire : la seule porte par laquelle les autres
/// modules lisent les membres, personnages et recettes connues. Aucun autre
/// module n'interroge directement ses tables.
/// </summary>
public interface IAnnuaire
{
	/// <summary>
	/// Identité d'un envoi : les personnages du membre (token personnel ou
	/// import Discord), sinon ceux de l'envoi. Les officiers sont ceux que
	/// l'annuaire reconnaît comme tels en base : le rang déclaré dans l'envoi
	/// n'en donne jamais à lui seul.
	/// </summary>
	Task<UploadIdentity> UploadIdentityAsync(UploadContext context, CancellationToken ct = default);

	/// <summary>
	/// Parmi les personnages signalés partis (autre guilde), ceux que cet envoi
	/// a le droit de retirer : jamais pour un import Discord ; avec un token
	/// personnel, ceux du membre ou sans propriétaire ; avec le token de guilde,
	/// ceux sans propriétaire. Un nom inconnu de l'annuaire est gardé (rien à
	/// protéger : il ne peut viser qu'un journal orphelin).
	/// </summary>
	Task<IReadOnlyList<string>> RemovableDepartedAsync(int? memberId, UploadSource source, IReadOnlyCollection<string> departed,
		CancellationToken ct = default);

	/// <summary>
	/// Propriétaire d'un token personnel, toutes guildes confondues (la guilde
	/// courante n'est pas encore connue) ; null si le token est inconnu.
	/// L'annuaire est le socle d'identité : sans lui, le token personnel ne
	/// peut pas être authentifié (le token de guilde, lui, reste utilisable).
	/// </summary>
	Task<UploadTokenOwner?> UploadTokenOwnerAsync(string token, CancellationToken ct = default);

	/// <summary>Inscrit le membre Discord s'il est inconnu, met à jour son nom sinon ; renvoie son identifiant.</summary>
	Task<int> EnsureMemberAsync(string discordId, string discordName, CancellationToken ct = default);

	/// <summary>Membres ayant un personnage principal, pour réaligner tous les pseudos.</summary>
	Task<IReadOnlyList<NicknameTarget>> NicknameTargetsAsync(CancellationToken ct = default);

	Task<DiscordActor> DiscordActorAsync(string discordId, bool isServerAdmin, CancellationToken ct = default);

	/// <summary>Comme <see cref="DiscordActorAsync"/>, en inscrivant le membre s'il est inconnu.</summary>
	Task<DiscordActor> EnsureDiscordActorAsync(string discordId, string discordName, CancellationToken ct = default);

	/// <summary>Nom exact d'un personnage de l'annuaire, sans tenir compte de la casse ; null s'il est inconnu.</summary>
	Task<string?> FindCharacterNameAsync(string name, CancellationToken ct = default);

	Task<string?> DiscordIdOfCharacterAsync(string? characterName, CancellationToken ct = default);

	Task<IReadOnlyList<string>> CharacterNamesOfMemberAsync(string discordId, CancellationToken ct = default);

	Task<IReadOnlyDictionary<string, CharacterCard>> CharacterCardsAsync(IReadOnlyCollection<string> names, CancellationToken ct = default);

	Task<IReadOnlyList<string>> DiscordIdsKnowingRecipeAsync(int spellId, string exceptCharacter, int max, CancellationToken ct = default);

	Task<int?> RecipeIdByNameAsync(string name, CancellationToken ct = default);

	Task<IReadOnlyList<string>> RecipeNamesContainingAsync(string text, int max, CancellationToken ct = default);

	Task<MemberRecipes> MemberRecipesAsync(string discordId, CancellationToken ct = default);

	Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> CraftersOfRecipesAsync(IReadOnlyCollection<int> spellIds, CancellationToken ct = default);

	Task<DirectorySize> SizeAsync(CancellationToken ct = default);

	// ---- Suggestions Discord partagées par tous les modules (personnages, métiers) ----

	/// <summary>Métiers de la guilde puis ceux du jeu, contenant le texte tapé.</summary>
	Task<IReadOnlyList<string>> ProfessionSuggestionsAsync(string typed, BotLanguage language, int max, CancellationToken ct = default);

	/// <summary>Personnages de la guilde contenant le texte, ceux qui ont des recettes relevées d'abord.</summary>
	Task<IReadOnlyList<string>> CharacterSuggestionsAsync(string typed, int max, CancellationToken ct = default);

	/// <summary>Personnages liés à un membre Discord contenant le texte.</summary>
	Task<IReadOnlyList<string>> OwnCharacterSuggestionsAsync(string discordId, string typed, int max, CancellationToken ct = default);
}
