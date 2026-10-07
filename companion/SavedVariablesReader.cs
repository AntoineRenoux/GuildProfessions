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
		var orders = ReadOrders(db.GetTable("orders"));
		var orderActions = ReadOrderActions(db.GetTable("orderActions"));
		if (charactersTable is null || charactersTable.Map.Count == 0)
		{
			return orders is null && orderActions is null
				? null
				: new UploadPayload([], orders, orderActions);
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

		return characters.Count == 0 && orders is null && orderActions is null
			? null
			: new UploadPayload(characters, orders, orderActions);
	}

	// GuildProfessionsDB.orders : [localId] = { requester, crafter, item, qty, note, createdAt }
	private static List<UploadOrder>? ReadOrders(LuaTable? ordersTable)
	{
		if (ordersTable is null || ordersTable.Map.Count == 0)
		{
			return null;
		}
		var orders = new List<UploadOrder>();
		foreach (var (clientId, value) in ordersTable.Map)
		{
			if (value.AsTable() is not { } order ||
				order.GetString("requester") is not { } requester ||
				order.GetString("crafter") is not { } crafter ||
				order.GetString("item") is not { } item)
			{
				continue;
			}
			orders.Add(new UploadOrder(
				clientId, requester, crafter, item,
				order.GetInt("qty") ?? 1,
				order.GetString("note"),
				order.GetString("createdAt")));
		}
		return orders.Count == 0 ? null : orders;
	}

	// GuildProfessionsDB.orderActions : [cléServeurOuLocale] = { status = "accepted"|"done"|"cancelled" }
	private static List<UploadOrderAction>? ReadOrderActions(LuaTable? actionsTable)
	{
		if (actionsTable is null || actionsTable.Map.Count == 0)
		{
			return null;
		}
		var actions = new List<UploadOrderAction>();
		foreach (var (key, value) in actionsTable.Map)
		{
			if (value.AsTable()?.GetString("status") is not { } status)
			{
				continue;
			}
			actions.Add(int.TryParse(key, out var serverId)
				? new UploadOrderAction(serverId, null, status)
				: new UploadOrderAction(null, key, status));
		}
		return actions.Count == 0 ? null : actions;
	}
}
