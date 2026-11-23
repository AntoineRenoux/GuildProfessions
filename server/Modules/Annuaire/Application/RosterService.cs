using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Annuaire.Application;

/// <summary>
/// Toutes les écritures du roster passent ici, quelle que soit la source
/// (bot Discord ou upload compagnon). Règle de fusion : pour un même couple
/// (personnage, métier), la donnée scannée par l'addon prime sur la donnée
/// déclarée via Discord — sauf la note, toujours éditable via Discord.
/// </summary>
public sealed class RosterService(AppDbContext db, IDataVersion dataVersion, BotLocalizer localizer)
{
	private BotLocalizer L => localizer;

	// ---- Membres & liaison ------------------------------------------------

	public async Task<Member> GetOrCreateMemberAsync(string discordId, string discordName, CancellationToken ct = default)
	{
		var member = await db.Members.SingleOrDefaultAsync(m => m.DiscordId == discordId, ct);
		if (member is null)
		{
			member = new Member { DiscordId = discordId, DiscordName = discordName };
			db.Members.Add(member);
		}
		else
		{
			member.DiscordName = discordName;
		}
		await db.SaveChangesAsync(ct);
		return member;
	}

	public async Task<Result> LinkCharacterAsync(string discordId, string discordName, string characterName, CancellationToken ct = default)
	{
		var member = await GetOrCreateMemberAsync(discordId, discordName, ct);
		var character = await db.Characters.SingleOrDefaultAsync(c => c.Name == characterName, ct);
		if (character is null)
		{
			character = new Character { Name = characterName, Source = DataSource.Discord };
			db.Characters.Add(character);
		}
		else if (character.MemberId is not null && character.MemberId != member.Id)
		{
			return Result.Forbidden(L.T($"**{characterName}** est déjà lié à un autre compte Discord.", $"**{characterName}** is already linked to another Discord account."));
		}
		character.MemberId = member.Id;
		if (member.MainCharacterId is null)
		{
			// Le premier personnage lié devient le principal (son nom = le pseudo Discord).
			member.MainCharacter = character;
		}
		await SaveWithVersionBumpAsync(ct);
		return Result.Success(L.T($"**{characterName}** est maintenant lié à ton compte Discord.", $"**{characterName}** is now linked to your Discord account."));
	}

	public async Task<Result> SetMainCharacterAsync(string discordId, string characterName, CancellationToken ct = default)
	{
		var member = await db.Members.SingleOrDefaultAsync(m => m.DiscordId == discordId, ct);
		var lowered = characterName.ToLower();
		var character = await db.Characters.SingleOrDefaultAsync(c => c.Name.ToLower() == lowered, ct);
		if (character is null)
		{
			return Result.NotFound(L.T($"Personnage **{characterName}** inconnu de l'annuaire.", $"No character named **{characterName}** in the directory."));
		}
		if (member is null || character.MemberId != member.Id)
		{
			return Result.Forbidden(L.T($"**{character.Name}** n'est pas lié à ton compte. Fais d'abord `/gkeep lier personnage:{character.Name}`.", $"**{character.Name}** is not linked to your account. Run `/gkeep link character:{character.Name}` first."));
		}
		member.MainCharacterId = character.Id;
		await db.SaveChangesAsync(ct);
		return Result.Success(L.T($"**{character.Name}** est maintenant ton personnage principal.", $"**{character.Name}** is now your main character."));
	}

	/// <summary>Nom du personnage principal d'un membre, null s'il n'en a pas.</summary>
	public async Task<string?> GetMainCharacterNameAsync(string discordId, CancellationToken ct = default)
	{
		return await db.Members.AsNoTracking()
			.Where(m => m.DiscordId == discordId && m.MainCharacter != null)
			.Select(m => m.MainCharacter!.Name)
			.SingleOrDefaultAsync(ct);
	}

	/// <summary>Couples (membre Discord, nom du principal) pour réaligner tous les pseudos.</summary>
	public async Task<List<(string DiscordId, string CharacterName)>> GetNicknameTargetsAsync(CancellationToken ct = default)
	{
		var rows = await db.Members.AsNoTracking()
			.Where(m => m.MainCharacter != null)
			.OrderBy(m => m.Id)
			.Select(m => new { m.DiscordId, Name = m.MainCharacter!.Name })
			.ToListAsync(ct);
		return rows.Select(r => (r.DiscordId, r.Name)).ToList();
	}

	public async Task<string> GetOrCreateUploadTokenAsync(string discordId, string discordName, CancellationToken ct = default)
	{
		var member = await GetOrCreateMemberAsync(discordId, discordName, ct);
		if (string.IsNullOrEmpty(member.UploadToken))
		{
			member.UploadToken = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
			await db.SaveChangesAsync(ct);
		}
		return member.UploadToken;
	}

	// ---- Écritures Discord --------------------------------------------------

	public async Task<Result> SetProfessionFromDiscordAsync(
		string discordId, string discordName, string characterName, string professionName,
		int level, int max, string? note, CancellationToken ct = default)
	{
		var member = await GetOrCreateMemberAsync(discordId, discordName, ct);
		var character = await db.Characters.Include(c => c.Professions)
			.SingleOrDefaultAsync(c => c.Name == characterName, ct);

		if (character is null)
		{
			character = new Character { Name = characterName, MemberId = member.Id, Source = DataSource.Discord };
			db.Characters.Add(character);
		}
		else if (character.MemberId is not null && character.MemberId != member.Id)
		{
			return Result.Forbidden(L.T($"**{characterName}** appartient à un autre membre : seul lui peut le modifier.", $"**{characterName}** belongs to another member: only they can change it."));
		}

		var profession = character.Professions.SingleOrDefault(p => p.Name == professionName);
		if (profession is null)
		{
			profession = new Profession { Name = professionName, Character = character, Source = DataSource.Discord };
			character.Professions.Add(profession);
		}

		string message;
		if (profession.Source == DataSource.Addon)
		{
			// La donnée scannée prime : Discord ne peut mettre à jour que la note.
			profession.Note = note ?? profession.Note;
			message = L.T(
				$"**{characterName}** · {professionName} est déjà relevé en jeu ({profession.SkillLevel}/{profession.MaxSkill}) : " +
					"le niveau déclaré est ignoré" + (note is null ? "." : ", la note a été mise à jour."),
				$"**{characterName}** · {professionName} is already scanned in game ({profession.SkillLevel}/{profession.MaxSkill}): " +
					"the declared level is ignored" + (note is null ? "." : ", the note was updated."));
		}
		else
		{
			profession.SkillLevel = level;
			profession.MaxSkill = max;
			profession.Note = note ?? profession.Note;
			profession.UpdatedUtc = DateTime.UtcNow;
			message = L.T($"**{characterName}** · {professionName} {level}/{max} enregistré.", $"**{characterName}** · {professionName} {level}/{max} saved.");
		}

		await SaveWithVersionBumpAsync(ct);
		return Result.Success(message);
	}

	public async Task<Result> RemoveProfessionAsync(
		string discordId, string characterName, string professionName, CancellationToken ct = default)
	{
		var member = await db.Members.SingleOrDefaultAsync(m => m.DiscordId == discordId, ct);
		var character = await db.Characters.Include(c => c.Professions)
			.SingleOrDefaultAsync(c => c.Name == characterName, ct);
		if (character is null)
		{
			return Result.NotFound(L.T($"Personnage **{characterName}** inconnu.", $"Unknown character **{characterName}**."));
		}
		if (character.MemberId is not null && character.MemberId != member?.Id)
		{
			return Result.Forbidden(L.T($"**{characterName}** appartient à un autre membre.", $"**{characterName}** belongs to another member."));
		}
		var profession = character.Professions.SingleOrDefault(p => p.Name == professionName);
		if (profession is null)
		{
			return Result.NotFound(L.T($"**{characterName}** n'a pas {professionName} d'enregistré.", $"**{characterName}** has no {professionName} recorded."));
		}
		db.Professions.Remove(profession);
		await SaveWithVersionBumpAsync(ct);
		return Result.Success(L.T($"**{characterName}** · {professionName} retiré.", $"**{characterName}** · {professionName} removed."));
	}

	// ---- Upload compagnon -----------------------------------------------------

	/// <summary>Part de l'annuaire dans un envoi : le membre est retrouvé par son identifiant.</summary>
	public async Task<UploadResult> ApplyUploadAsync(UploadContext context, CancellationToken ct = default)
	{
		var member = context.MemberId is { } memberId ? await db.Members.SingleOrDefaultAsync(m => m.Id == memberId, ct) : null;
		return await ApplyUploadAsync(member, context.Payload, context.Source, ct);
	}

	/// <summary>
	/// member null = upload « partagé » (compagnon zéro-config authentifié par
	/// le token de guilde) : pas de revendication de personnage ni de contrôle
	/// de propriété — le scan vient du jeu et reste la vérité.
	/// Sans canal précisé : token personnel si un membre est donné, token de guilde sinon.
	/// </summary>
	public async Task<UploadResult> ApplyUploadAsync(Member? member, UploadPayload payload, UploadSource? source = null,
		CancellationToken ct = default)
	{
		var channel = source ?? (member is null ? UploadSource.GuildToken : UploadSource.PersonalToken);
		var warnings = new List<string>();
		foreach (var uploaded in payload.Characters)
		{
			var character = await db.Characters
				.Include(c => c.Professions).ThenInclude(p => p.Recipes)
				.SingleOrDefaultAsync(c => c.Name == uploaded.Name, ct);

			if (character is null)
			{
				character = new Character { Name = uploaded.Name, MemberId = member?.Id, Source = DataSource.Addon };
				db.Characters.Add(character);
			}
			else if (member is not null && character.MemberId is not null && character.MemberId != member.Id)
			{
				warnings.Add(L.T($"{uploaded.Name} est lié à un autre membre : envoi ignoré pour ce personnage.", $"{uploaded.Name} is linked to another member: upload ignored for this character."));
				continue;
			}
			else if (member is not null)
			{
				character.MemberId ??= member.Id;
			}

			character.ClassFile = uploaded.ClassFile ?? character.ClassFile;
			character.RaceFile = uploaded.RaceFile ?? character.RaceFile;
			character.RaceId = uploaded.RaceId ?? character.RaceId;
			character.Gender = uploaded.Gender ?? character.Gender;
			character.Level = uploaded.Level ?? character.Level;
			character.LastSeenUtc = DateTime.UtcNow;
			character.Source = DataSource.Addon;
			if (uploaded.Officer is { } officer && MayChangeRank(channel, officer, character.MemberId is not null))
			{
				character.IsOfficer = officer;
			}
			if (uploaded.Equipment is { Count: > 0 })
			{
				character.EquipmentJson = System.Text.Json.JsonSerializer.Serialize(uploaded.Equipment);
			}

			// Le scan est la vérité pour ce personnage : les métiers absents du
			// payload sont retirés, les notes Discord existantes sont conservées.
			var uploadedNames = uploaded.Professions.Select(p => p.Name).ToHashSet();
			foreach (var stale in character.Professions.Where(p => !uploadedNames.Contains(p.Name)).ToList())
			{
				db.Professions.Remove(stale);
				character.Professions.Remove(stale);
			}

			foreach (var uploadedProf in uploaded.Professions)
			{
				var profession = character.Professions.SingleOrDefault(p => p.Name == uploadedProf.Name);
				if (profession is null)
				{
					profession = new Profession { Name = uploadedProf.Name, Character = character };
					character.Professions.Add(profession);
				}
				profession.SkillLevel = uploadedProf.Level;
				profession.MaxSkill = uploadedProf.Max;
				profession.Source = DataSource.Addon;
				profession.ScannedAt = uploadedProf.ScannedAt ?? profession.ScannedAt;
				profession.UpdatedUtc = DateTime.UtcNow;

				if (uploadedProf.Recipes is not null)
				{
					profession.Recipes.Clear();
					foreach (var recipe in uploadedProf.Recipes.DistinctBy(r => r.Id))
					{
						profession.Recipes.Add(new Recipe { SpellId = recipe.Id, Name = recipe.Name });
					}
				}
			}
		}

		var version = await SaveWithVersionBumpAsync(ct);
		return new UploadResult(true, version, warnings);
	}

	/// <summary>
	/// Le rang d'officier en base donne des droits sur Discord. Il ne monte que
	/// par un envoi authentifié par le token personnel du membre (ses
	/// personnages liés). Le token partagé de la guilde, distribué à tous les
	/// membres, ne le donne jamais, et ne le retire qu'à un personnage sans
	/// propriétaire : sinon n'importe quel détenteur du pack pourrait priver un
	/// officier de ses droits. Un import collé dans Discord n'y touche pas.
	/// Limite connue : un membre peut se déclarer officier par son propre token
	/// personnel ; le rang en jeu est visible de toute la guilde, l'abus se voit.
	/// </summary>
	private static bool MayChangeRank(UploadSource source, bool officer, bool linked) => source switch
	{
		UploadSource.PersonalToken => true,
		UploadSource.GuildToken => !officer && !linked,
		_ => false,
	};

	/// <summary>
	/// Retire de l'annuaire les personnages partis dans une autre guilde
	/// (relevé en jeu). Les commandes, qui portent des noms en clair, restent.
	/// </summary>
	public async Task<int> RemoveDepartedCharactersAsync(IReadOnlyCollection<string> names, CancellationToken ct = default)
	{
		if (names.Count == 0)
		{
			return 0;
		}
		var characters = await db.Characters.Where(c => names.Contains(c.Name)).ToListAsync(ct);
		if (characters.Count == 0)
		{
			return 0;
		}
		var ids = characters.Select(c => c.Id).ToList();
		foreach (var member in await db.Members.Where(m => m.MainCharacterId != null && ids.Contains(m.MainCharacterId.Value)).ToListAsync(ct))
		{
			member.MainCharacterId = null;
		}
		db.Characters.RemoveRange(characters);
		await SaveWithVersionBumpAsync(ct);
		return characters.Count;
	}

	// Version propre à la guilde : le compagnon ne réécrit Data.lua que si elle change.
	private Task<long> SaveWithVersionBumpAsync(CancellationToken ct) => dataVersion.SaveAndBumpAsync(ct);
}
