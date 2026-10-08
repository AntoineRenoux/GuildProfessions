namespace GuildProfessions.Server.Services;

public sealed record RecipeField(string Title, string Body);

public sealed record RecipeListLayout(List<RecipeField> Fields, int HiddenCount);

/// <summary>
/// Découpe des listes de recettes en champs d'embed Discord : 1024 caractères
/// par champ, 25 champs et ~6000 caractères par embed. Ce qui ne rentre pas
/// est compté dans HiddenCount plutôt que tronqué au milieu d'un nom.
/// </summary>
public static class RecipeListFormatter
{
	private const int FieldLimit = 1024;
	private const int MaxFields = 25;
	// Marge sous les 6000 caractères pour le titre, la description et le pied de l'embed.
	private const int EmbedBudget = 5200;

	public static RecipeListLayout Layout(IEnumerable<(string Profession, IReadOnlyList<string> Recipes)> professions)
	{
		var fields = new List<RecipeField>();
		var used = 0;
		var hidden = 0;

		foreach (var (profession, recipes) in professions)
		{
			var sorted = recipes.OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList();
			var title = $"{profession} ({sorted.Count})";
			var chunk = new List<string>();
			var chunkLength = 0;
			var part = 0;

			bool Flush()
			{
				if (chunk.Count == 0)
				{
					return true;
				}
				var fieldTitle = part == 0 ? title : $"{profession} (suite)";
				var body = string.Join("\n", chunk);
				if (fields.Count >= MaxFields || used + fieldTitle.Length + body.Length > EmbedBudget)
				{
					return false;
				}
				fields.Add(new RecipeField(fieldTitle, body));
				used += fieldTitle.Length + body.Length;
				part++;
				chunk.Clear();
				chunkLength = 0;
				return true;
			}

			for (var i = 0; i < sorted.Count; i++)
			{
				var line = $"• {sorted[i]}";
				if (chunkLength + line.Length + 1 > FieldLimit && !Flush())
				{
					hidden += sorted.Count - i + chunk.Count;
					chunk.Clear();
					break;
				}
				chunk.Add(line);
				chunkLength += line.Length + 1;
			}
			if (!Flush())
			{
				hidden += chunk.Count;
			}
		}

		return new RecipeListLayout(fields, hidden);
	}
}
