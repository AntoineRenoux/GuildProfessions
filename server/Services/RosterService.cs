using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Services;

public sealed record OperationResult(bool Success, string Message);

/// <summary>
/// Toutes les écritures du roster passent ici, quelle que soit la source
/// (bot Discord ou upload compagnon). Règle de fusion : pour un même couple
/// (personnage, métier), la donnée scannée par l'addon prime sur la donnée
/// déclarée via Discord — sauf la note, toujours éditable via Discord.
/// </summary>
public sealed class RosterService(AppDbContext db)
{
	private const string DataVersionKey = "DataVersion";

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

	public async Task<OperationResult> LinkCharacterAsync(string discordId, string discordName, string characterName, CancellationToken ct = default)
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
			return new OperationResult(false, $"**{characterName}** est déjà lié à un autre compte Discord.");
		}
		character.MemberId = member.Id;
		await SaveWithVersionBumpAsync(ct);
		return new OperationResult(true, $"**{characterName}** est maintenant lié à ton compte Discord.");
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

	public async Task<OperationResult> SetProfessionFromDiscordAsync(
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
			return new OperationResult(false, $"**{characterName}** appartient à un autre membre — seul lui peut le modifier.");
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
			message = $"**{characterName}** — {professionName} est déjà scanné en jeu ({profession.SkillLevel}/{profession.MaxSkill}) : " +
				"le niveau déclaré est ignoré" + (note is null ? "." : ", la note a été mise à jour.");
		}
		else
		{
			profession.SkillLevel = level;
			profession.MaxSkill = max;
			profession.Note = note ?? profession.Note;
			profession.UpdatedUtc = DateTime.UtcNow;
			message = $"**{characterName}** — {professionName} {level}/{max} enregistré.";
		}

		await SaveWithVersionBumpAsync(ct);
		return new OperationResult(true, message);
	}

	public async Task<OperationResult> RemoveProfessionAsync(
		string discordId, string characterName, string professionName, CancellationToken ct = default)
	{
		var member = await db.Members.SingleOrDefaultAsync(m => m.DiscordId == discordId, ct);
		var character = await db.Characters.Include(c => c.Professions)
			.SingleOrDefaultAsync(c => c.Name == characterName, ct);
		if (character is null)
		{
			return new OperationResult(false, $"Personnage **{characterName}** inconnu.");
		}
		if (character.MemberId is not null && character.MemberId != member?.Id)
		{
			return new OperationResult(false, $"**{characterName}** appartient à un autre membre.");
		}
		var profession = character.Professions.SingleOrDefault(p => p.Name == professionName);
		if (profession is null)
		{
			return new OperationResult(false, $"**{characterName}** n'a pas {professionName} d'enregistré.");
		}
		db.Professions.Remove(profession);
		await SaveWithVersionBumpAsync(ct);
		return new OperationResult(true, $"**{characterName}** — {professionName} retiré.");
	}

	// ---- Upload compagnon -----------------------------------------------------

	/// <summary>
	/// member null = upload « partagé » (compagnon zéro-config authentifié par
	/// le token de guilde) : pas de revendication de personnage ni de contrôle
	/// de propriété — le scan vient du jeu et reste la vérité.
	/// </summary>
	public async Task<UploadResult> ApplyUploadAsync(Member? member, UploadPayload payload, CancellationToken ct = default)
	{
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
				warnings.Add($"{uploaded.Name} est lié à un autre membre : upload ignoré pour ce personnage.");
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
			if (uploaded.Equipment is { Count: > 0 })
			{
				character.EquipmentJson = System.Text.Json.JsonSerializer.Serialize(uploaded.Equipment);
			}

			// Le scan est la vérité pour ce personnage : les métiers absents du
			// payload sont retirés, les notes Discord existantes sont conservées.
			var uploadedNames = payload.Characters
				.Single(c => c.Name == uploaded.Name).Professions.Select(p => p.Name).ToHashSet();
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

	// ---- Version de l'export -------------------------------------------------

	public async Task<long> GetDataVersionAsync(CancellationToken ct = default)
	{
		var meta = await db.Metas.FindAsync([DataVersionKey], ct);
		return meta is null ? 0 : long.Parse(meta.Value);
	}

	public async Task<long> BumpVersionAndSaveAsync(CancellationToken ct = default) => await SaveWithVersionBumpAsync(ct);

	private async Task<long> SaveWithVersionBumpAsync(CancellationToken ct)
	{
		var meta = await db.Metas.FindAsync([DataVersionKey], ct);
		long version;
		if (meta is null)
		{
			version = 1;
			db.Metas.Add(new Meta { Key = DataVersionKey, Value = "1" });
		}
		else
		{
			version = long.Parse(meta.Value) + 1;
			meta.Value = version.ToString();
		}
		await db.SaveChangesAsync(ct);
		return version;
	}
}
