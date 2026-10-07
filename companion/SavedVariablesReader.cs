using GuildProfessions.Companion.Lua;

namespace GuildProfessions.Companion;

/// <summary>
/// Traduit le fichier SavedVariables de l'addon (GuildProfessionsDB) en
/// payload d'upload pour le serveur.
/// </summary>
public static class SavedVariablesReader
{
	public static UploadPayload? BuildPayload(string luaContent)
	{
		var db = LuaParser.ParseSavedVariable(luaContent, "GuildProfessionsDB");
		var charactersTable = db.GetTable("characters");
		if (charactersTable is null || charactersTable.Map.Count == 0)
		{
			return null;
		}

		var recipeNames = new Dictionary<int, string>();
		var namesTable = db.GetTable("recipeNames");
		if (namesTable is not null)
		{
			foreach (var (key, value) in namesTable.Map)
			{
				if (int.TryParse(key, out var spellId) && value.AsString() is { } name)
				{
					recipeNames[spellId] = name;
				}
			}
		}

		var characters = new List<UploadCharacter>();
		foreach (var (characterName, characterValue) in charactersTable.Map)
		{
			if (characterValue.AsTable() is not { } characterTable)
			{
				continue;
			}
			var professions = new List<UploadProfession>();
			var professionsTable = characterTable.GetTable("professions");
			if (professionsTable is not null)
			{
				foreach (var (professionName, professionValue) in professionsTable.Map)
				{
					if (professionValue.AsTable() is not { } professionTable)
					{
						continue;
					}
					List<UploadRecipe>? recipes = null;
					var recipesTable = professionTable.GetTable("recipes");
					if (recipesTable is not null)
					{
						recipes = recipesTable.Items
							.Select(item => item.AsInt())
							.Where(id => id is not null)
							.Select(id => new UploadRecipe(id!.Value, recipeNames.GetValueOrDefault(id.Value)))
							.ToList();
					}
					professions.Add(new UploadProfession(
						professionName,
						professionTable.GetInt("level") ?? 0,
						professionTable.GetInt("max") ?? 0,
						professionTable.GetString("scannedAt"),
						recipes));
				}
			}
			characters.Add(new UploadCharacter(
				characterName,
				characterTable.GetString("classFile"),
				characterTable.GetString("raceFile"),
				characterTable.GetInt("raceId"),
				characterTable.GetInt("gender"),
				characterTable.GetInt("level"),
				professions));
		}

		return characters.Count == 0 ? null : new UploadPayload(characters);
	}
}
