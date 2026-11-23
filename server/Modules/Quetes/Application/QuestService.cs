using System.Text.Json;
using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Quetes.Application;

/// <summary>
/// Quêtes partagées : les quêtes élites, de donjon et de raid que les membres
/// ont dans leur journal, pour trouver qui les fait aussi. Relevées en jeu à
/// chaque changement du journal ; une quête rendue disparaît du relevé suivant.
/// Stockées dans la table propre au module ; l'annuaire n'est lu que par son
/// contrat public (personnages encore dans la guilde, classe, dernière visite).
/// </summary>
public sealed class QuestService(AppDbContext db, IAnnuaire annuaire, IDataVersion dataVersion)
{
	public static readonly string[] Kinds = ["elite", "dungeon", "raid"];
	private const int MaxQuests = 40;

	/// <summary>Garde les quêtes valides d'un relevé (types connus, textes bornés, sans doublon).</summary>
	public static List<UploadQuest> Clean(IEnumerable<UploadQuest> quests) => quests
		.Where(q => q.Id > 0 && Kinds.Contains(q.Kind) && !string.IsNullOrWhiteSpace(q.Title))
		.GroupBy(q => q.Id).Select(g => g.First())
		.Take(MaxQuests)
		.Select(q => q with
		{
			Title = Text.Clip(q.Title, 100) ?? "",
			Location = Text.Clip(q.Location, 60),
		})
		.ToList();

	public static List<UploadQuest>? Read(string? questsJson)
	{
		if (questsJson is null)
		{
			return null;
		}
		try
		{
			return JsonSerializer.Deserialize<List<UploadQuest>>(questsJson);
		}
		catch (JsonException)
		{
			return null; // colonne corrompue : comme si rien n'était relevé
		}
	}

	/// <summary>Relevés de l'envoi, pour les personnages dont il est responsable ; journaux des partis retirés.</summary>
	public async Task ApplyUploadAsync(UploadContext context, CancellationToken ct = default)
	{
		var identity = await annuaire.UploadIdentityAsync(context, ct);
		var uploaded = context.Payload.Characters
			.Where(c => c.Quests is not null && identity.Owns(c.Name))
			.ToList();
		var names = uploaded.Select(c => c.Name).Concat(context.Departed).ToList();
		if (names.Count == 0)
		{
			return;
		}
		var logs = await db.CharacterQuestLogs.Where(q => names.Contains(q.Character)).ToDictionaryAsync(q => q.Character, ct);
		foreach (var character in uploaded)
		{
			if (!logs.TryGetValue(character.Name, out var log))
			{
				log = new CharacterQuestLog { Character = character.Name, QuestsJson = "[]" };
				db.CharacterQuestLogs.Add(log);
			}
			var json = JsonSerializer.Serialize(Clean(character.Quests!));
			if (log.QuestsJson != json)
			{
				log.QuestsJson = json;
				log.UpdatedUtc = DateTime.UtcNow;
			}
		}
		db.CharacterQuestLogs.RemoveRange(context.Departed.Where(logs.ContainsKey).Select(n => logs[n]));
		if (db.ChangeTracker.HasChanges())
		{
			await dataVersion.SaveAndBumpAsync(ct);
		}
	}

	/// <summary>Quêtes de chaque personnage encore dans l'annuaire (null : rien de relevé).</summary>
	public async Task<Dictionary<string, List<UploadQuest>>> ByCharacterAsync(CancellationToken ct = default)
	{
		var logs = await db.CharacterQuestLogs.AsNoTracking().ToListAsync(ct);
		var result = new Dictionary<string, List<UploadQuest>>();
		foreach (var log in logs)
		{
			if (Read(log.QuestsJson) is { } quests)
			{
				result[log.Character] = quests;
			}
		}
		return result;
	}

	public sealed record Holder(string Character, string? ClassFile, bool Complete, DateTime? SeenUtc);

	public sealed record SharedQuest(int Id, string Title, string Kind, string? Location, int? Level, List<Holder> Holders);

	/// <summary>Toutes les quêtes relevées dans la guilde, avec qui les a ; les plus partagées d'abord.</summary>
	public async Task<List<SharedQuest>> AllAsync(CancellationToken ct = default)
	{
		var byCharacter = await ByCharacterAsync(ct);
		// Un personnage parti de l'annuaire ne compte plus, même si son journal traîne encore.
		var cards = await annuaire.CharacterCardsAsync(byCharacter.Keys.ToList(), ct);
		var byQuest = new Dictionary<int, (UploadQuest Quest, List<Holder> Holders)>();
		foreach (var (name, quests) in byCharacter)
		{
			if (!cards.TryGetValue(name, out var card))
			{
				continue;
			}
			foreach (var quest in quests)
			{
				if (!byQuest.TryGetValue(quest.Id, out var entry))
				{
					entry = (quest, []);
					byQuest[quest.Id] = entry;
				}
				entry.Holders.Add(new Holder(card.Name, card.ClassFile, quest.Complete, card.LastSeenUtc));
			}
		}
		return byQuest.Values
			.Select(e => new SharedQuest(e.Quest.Id, e.Quest.Title, e.Quest.Kind, e.Quest.Location, e.Quest.Level,
				e.Holders.OrderBy(h => h.Character).ToList()))
			.OrderByDescending(q => q.Holders.Count).ThenBy(q => q.Location).ThenBy(q => q.Title)
			.ToList();
	}

	/// <summary>Quêtes d'un personnage, chacune avec les autres membres qui l'ont.</summary>
	public async Task<(string? Character, List<SharedQuest> Quests)> ForCharacterAsync(string name, CancellationToken ct = default)
	{
		var all = await AllAsync(ct);
		var exact = all.SelectMany(q => q.Holders).Select(h => h.Character)
			.FirstOrDefault(n => string.Equals(n, name.Trim(), StringComparison.OrdinalIgnoreCase));
		return exact is null ? (null, []) : (exact, all.Where(q => q.Holders.Any(h => h.Character == exact)).ToList());
	}

	/// <summary>Personnages liés à un membre Discord (pour « mes quêtes »).</summary>
	public Task<IReadOnlyList<string>> MemberCharactersAsync(string discordId, CancellationToken ct = default) =>
		annuaire.CharacterNamesOfMemberAsync(discordId, ct);

	public static string KindLabel(string kind, BotLocalizer l) => kind switch
	{
		"dungeon" => l.T("Donjon", "Dungeon"),
		"raid" => "Raid",
		_ => l.T("Élite", "Elite"),
	};
}
