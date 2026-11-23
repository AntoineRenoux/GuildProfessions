using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Annuaire.Application;

/// <summary>Implémentation du contrat public <see cref="IAnnuaire"/>.</summary>
public sealed class AnnuaireQueries(AppDbContext db, RosterService roster) : IAnnuaire
{
	public async Task<UploadIdentity> UploadIdentityAsync(UploadContext context, CancellationToken ct = default)
	{
		var characters = context.MemberId is { } memberId
			? await db.Characters.Where(c => c.MemberId == memberId).Select(c => c.Name).ToHashSetAsync(ct)
			: context.Payload.Characters.Select(c => c.Name).ToHashSet();
		// Officiers reconnus en base (l'annuaire a déjà appliqué l'envoi et décidé
		// seul de l'évolution du rang) : déclarer « officer » dans l'envoi ne suffit pas.
		var names = characters.ToList();
		var officers = await db.Characters
			.Where(c => c.IsOfficer && names.Contains(c.Name))
			.Select(c => c.Name)
			.ToHashSetAsync(ct);
		return new UploadIdentity(characters, officers);
	}

	public async Task<IReadOnlyList<string>> RemovableDepartedAsync(int? memberId, UploadSource source,
		IReadOnlyCollection<string> departed, CancellationToken ct = default)
	{
		if (departed.Count == 0 || source == UploadSource.DiscordImport)
		{
			return [];
		}
		var owners = await db.Characters.AsNoTracking()
			.Where(c => departed.Contains(c.Name))
			.Select(c => new { c.Name, c.MemberId })
			.ToDictionaryAsync(c => c.Name, c => c.MemberId, ct);
		var allowedOwner = source == UploadSource.PersonalToken ? memberId : null;
		return departed
			.Where(name => !owners.TryGetValue(name, out var owner) || owner is null || owner == allowedOwner)
			.ToList();
	}

	public async Task<UploadTokenOwner?> UploadTokenOwnerAsync(string token, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return null;
		}
		return await db.Members.IgnoreQueryFilters().AsNoTracking()
			.Where(m => m.UploadToken == token)
			.Select(m => new UploadTokenOwner(m.GuildId, m.Id))
			.SingleOrDefaultAsync(ct);
	}

	public async Task<int> EnsureMemberAsync(string discordId, string discordName, CancellationToken ct = default) =>
		(await roster.GetOrCreateMemberAsync(discordId, discordName, ct)).Id;

	public async Task<IReadOnlyList<NicknameTarget>> NicknameTargetsAsync(CancellationToken ct = default) =>
		(await roster.GetNicknameTargetsAsync(ct)).Select(t => new NicknameTarget(t.DiscordId, t.CharacterName)).ToList();

	public async Task<DiscordActor> DiscordActorAsync(string discordId, bool isServerAdmin, CancellationToken ct = default)
	{
		var member = await db.Members.AsNoTracking()
			.Include(m => m.Characters)
			.SingleOrDefaultAsync(m => m.DiscordId == discordId, ct);
		return ToActor(discordId, member, isServerAdmin);
	}

	public async Task<DiscordActor> EnsureDiscordActorAsync(string discordId, string discordName, CancellationToken ct = default)
	{
		// Une seule règle d'inscription du membre, celle du roster.
		await roster.GetOrCreateMemberAsync(discordId, discordName, ct);
		return await DiscordActorAsync(discordId, false, ct);
	}

	private static DiscordActor ToActor(string discordId, Member? member, bool isServerAdmin)
	{
		var characters = (member?.Characters ?? [])
			.OrderBy(c => c.Id)
			.Select(c => new LinkedCharacter(c.Id, c.Name, c.ClassFile, c.IsOfficer))
			.ToList();
		var main = characters.FirstOrDefault(c => c.Id == member?.MainCharacterId) ?? characters.FirstOrDefault();
		return new DiscordActor(discordId, member?.DiscordName, characters, main, isServerAdmin || characters.Any(c => c.IsOfficer));
	}

	public async Task<string?> FindCharacterNameAsync(string name, CancellationToken ct = default)
	{
		var lowered = name.Trim().ToLower();
		return await db.Characters.AsNoTracking()
			.Where(c => c.Name.ToLower() == lowered)
			.Select(c => c.Name)
			.FirstOrDefaultAsync(ct);
	}

	public async Task<string?> DiscordIdOfCharacterAsync(string? characterName, CancellationToken ct = default)
	{
		if (characterName is null)
		{
			return null;
		}
		return await db.Characters.AsNoTracking()
			.Where(c => c.Name == characterName && c.Member != null)
			.Select(c => c.Member!.DiscordId)
			.SingleOrDefaultAsync(ct);
	}

	public async Task<IReadOnlyList<string>> CharacterNamesOfMemberAsync(string discordId, CancellationToken ct = default) =>
		await db.Characters.AsNoTracking()
			.Where(c => c.Member != null && c.Member.DiscordId == discordId)
			.OrderBy(c => c.Name)
			.Select(c => c.Name)
			.ToListAsync(ct);

	public async Task<IReadOnlyDictionary<string, CharacterCard>> CharacterCardsAsync(IReadOnlyCollection<string> names, CancellationToken ct = default) =>
		await db.Characters.AsNoTracking()
			.Where(c => names.Contains(c.Name))
			.Select(c => new CharacterCard(c.Name, c.ClassFile, c.LastSeenUtc))
			.ToDictionaryAsync(c => c.Name, ct);

	public async Task<IReadOnlyList<string>> DiscordIdsKnowingRecipeAsync(int spellId, string exceptCharacter, int max, CancellationToken ct = default) =>
		await db.Recipes.AsNoTracking()
			.Where(r => r.SpellId == spellId && r.Profession.Character.Member != null && r.Profession.Character.Name != exceptCharacter)
			.Select(r => r.Profession.Character.Member!.DiscordId)
			.Distinct()
			.Take(max)
			.ToListAsync(ct);

	public async Task<int?> RecipeIdByNameAsync(string name, CancellationToken ct = default)
	{
		var lowered = name.Trim().ToLower();
		return await db.Recipes.AsNoTracking()
			.Where(r => r.Name != null && r.Name.ToLower() == lowered)
			.Select(r => (int?)r.SpellId)
			.FirstOrDefaultAsync(ct);
	}

	public async Task<IReadOnlyList<string>> RecipeNamesContainingAsync(string text, int max, CancellationToken ct = default)
	{
		var lowered = text.Trim().ToLower();
		return await db.Recipes.AsNoTracking()
			.Where(r => r.Name != null && r.Name.ToLower().Contains(lowered))
			.Select(r => r.Name!)
			.Distinct()
			.OrderBy(n => n)
			.Take(max)
			.ToListAsync(ct);
	}

	public async Task<MemberRecipes> MemberRecipesAsync(string discordId, CancellationToken ct = default)
	{
		var recipes = await db.Recipes.AsNoTracking()
			.Where(r => r.Profession.Character.Member != null && r.Profession.Character.Member.DiscordId == discordId)
			.Select(r => new { r.SpellId, r.Name })
			.ToListAsync(ct);
		return new MemberRecipes(
			recipes.Select(r => r.SpellId).ToHashSet(),
			recipes.Where(r => r.Name != null).Select(r => r.Name!.ToLower()).ToHashSet());
	}

	public async Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> CraftersOfRecipesAsync(IReadOnlyCollection<int> spellIds, CancellationToken ct = default)
	{
		var rows = await db.Recipes.AsNoTracking()
			.Where(r => spellIds.Contains(r.SpellId))
			.Select(r => new { r.SpellId, r.Profession.Character.Name })
			.ToListAsync(ct);
		return rows.GroupBy(r => r.SpellId)
			.ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(r => r.Name).Distinct().Order().ToList());
	}

	public async Task<DirectorySize> SizeAsync(CancellationToken ct = default) =>
		new(await db.Members.CountAsync(ct), await db.Characters.CountAsync(ct));

	/// <summary>Métiers de la guilde puis ceux du jeu, contenant le texte tapé.</summary>
	public async Task<IReadOnlyList<string>> ProfessionSuggestionsAsync(string typed, BotLanguage language, int max, CancellationToken ct = default)
	{
		var inGuild = await db.Professions.AsNoTracking().Select(p => p.Name).Distinct().ToListAsync(ct);
		return inGuild.OrderBy(n => n)
			.Concat(ProfessionChoices.For(language))
			.Distinct()
			.Where(p => p.Contains(typed, StringComparison.OrdinalIgnoreCase))
			.Take(max)
			.ToList();
	}

	/// <summary>Personnages de la guilde contenant le texte, ceux qui ont des recettes relevées d'abord.</summary>
	public async Task<IReadOnlyList<string>> CharacterSuggestionsAsync(string typed, int max, CancellationToken ct = default)
	{
		var lowered = typed.Trim().ToLower();
		return await db.Characters.AsNoTracking()
			.Where(c => lowered == "" || c.Name.ToLower().Contains(lowered))
			.OrderByDescending(c => c.Professions.Any(p => p.Recipes.Any()))
			.ThenBy(c => c.Name)
			.Select(c => c.Name)
			.Take(max)
			.ToListAsync(ct);
	}

	/// <summary>Personnages liés à un membre Discord contenant le texte.</summary>
	public async Task<IReadOnlyList<string>> OwnCharacterSuggestionsAsync(string discordId, string typed, int max, CancellationToken ct = default)
	{
		var lowered = typed.Trim().ToLower();
		return await db.Characters.AsNoTracking()
			.Where(c => c.Member != null && c.Member.DiscordId == discordId)
			.Where(c => lowered == "" || c.Name.ToLower().Contains(lowered))
			.OrderBy(c => c.Name)
			.Select(c => c.Name)
			.Take(max)
			.ToListAsync(ct);
	}
}
