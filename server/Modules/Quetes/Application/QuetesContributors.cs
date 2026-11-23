using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Quetes.Application;

/// <summary>Part des quêtes dans un envoi : le journal de chaque personnage relevé.</summary>
public sealed class QuetesUploadContributor(QuestService quests) : IUploadContributor
{
	public string Module => "Quêtes";

	public int Order => 40;

	public async Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct)
	{
		await quests.ApplyUploadAsync(context, ct);
		return [];
	}
}

/// <summary>Part des quêtes dans l'export : complète la fiche de chaque personnage exporté par l'annuaire.</summary>
public sealed class QuetesExportContributor(QuestService quests) : IExportContributor
{
	public string Module => "Quêtes";

	public int Order => 40;

	public async Task ContributeAsync(ExportDraft draft, CancellationToken ct)
	{
		// Annuaire absent (en panne) : aucune fiche à compléter, la section reste absente.
		if (draft.Characters is not { } characters)
		{
			return;
		}
		var byCharacter = await quests.ByCharacterAsync(ct);
		draft.Characters = characters
			.Select(c => byCharacter.TryGetValue(c.Name, out var list) ? c with { Quests = list } : c)
			.ToList();
	}
}
