using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Annuaire.Application;

/// <summary>Part de l'annuaire dans un envoi : personnages, métiers, recettes, puis retrait des partis.</summary>
public sealed class AnnuaireUploadContributor(RosterService roster, BotLocalizer localizer) : IUploadContributor
{
	public string Module => "Annuaire";

	public int Order => 0;

	public async Task<IReadOnlyList<string>> ApplyAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct)
	{
		var result = await roster.ApplyUploadAsync(context, ct);
		List<string> warnings = [.. result.Warnings];
		var removed = await roster.RemoveDepartedCharactersAsync(context.Departed, ct);
		if (removed > 0)
		{
			warnings.Add(localizer.T(
				$"{removed} personnage(s) retiré(s) de l'annuaire : ils sont maintenant dans une autre guilde.",
				$"{removed} character(s) removed from the directory: they are now in another guild."));
		}
		return warnings;
	}
}
