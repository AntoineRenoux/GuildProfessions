using GuildProfessions.Companion.Lua;

namespace GuildProfessions.Companion;

/// <summary>
/// Traduit le fichier SavedVariables de l'addon (GuildkeepDB) en
/// payload d'upload pour le serveur.
/// </summary>
public static class SavedVariablesReader
{
	public static UploadPayload? BuildPayload(string luaContent)
	{
		var db = LuaParser.ParseSavedVariable(luaContent, "GuildkeepDB");
		var charactersTable = db.GetTable("characters");
		var orders = ReadOrders(db.GetTable("orders"));
		var orderActions = ReadOrderActions(db.GetTable("orderActions"));
		var offers = ReadOffers(db.GetTable("orderOffers"));
		var events = ReadEvents(db.GetTable("eventEdits"));
		var signups = ReadSignups(db.GetTable("eventSignups"));
		var placements = ReadPlacements(db.GetTable("eventPlacements"));
		var reserves = ReadReserves(db.GetTable("eventReserves"));
		var hardReserves = ReadHardReserves(db.GetTable("eventHardReserves"));
		var duties = ReadDuties(db.GetTable("eventDuties"));
		var raids = ReadRaids(db.GetTable("raidSessions"));
		var loot = ReadLoot(db.GetTable("raidLoot"));
		var attendanceCorrections = ReadAttendanceCorrections(db.GetTable("attendanceCorrections"));
		var lootCorrections = ReadLootCorrections(db.GetTable("lootCorrections"));
		var hasEventData = events is not null || signups is not null || placements is not null ||
			reserves is not null || hardReserves is not null || duties is not null ||
			raids is not null || loot is not null || attendanceCorrections is not null || lootCorrections is not null ||
			offers is not null;
		if (charactersTable is null || charactersTable.Map.Count == 0)
		{
			return orders is null && orderActions is null && !hasEventData
				? null
				: new UploadPayload([], orders, orderActions, null, events, signups, placements, reserves, hardReserves, duties,
					raids, loot, attendanceCorrections, lootCorrections, offers);
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
					// Toutes les recettes de la fenêtre du métier (catalogue de la guilde), avec leur nom.
					List<UploadRecipe>? catalog = null;
					if (professionTable.GetTable("catalog") is { } catalogTable)
					{
						catalog = catalogTable.Items
							.Select(item => item.AsInt())
							.Where(id => id is not null && recipeNames.ContainsKey(id.Value))
							.Select(id => new UploadRecipe(id!.Value, recipeNames[id.Value]))
							.ToList();
					}
					professions.Add(new UploadProfession(
						professionName,
						professionTable.GetInt("level") ?? 0,
						professionTable.GetInt("max") ?? 0,
						professionTable.GetString("scannedAt"),
						recipes,
						catalog is { Count: > 0 } ? catalog : null));
				}
			}
			Dictionary<int, string>? equipment = null;
			var equipmentTable = characterTable.GetTable("equipment");
			if (equipmentTable is not null)
			{
				equipment = [];
				foreach (var (slotKey, linkValue) in equipmentTable.Map)
				{
					if (int.TryParse(slotKey, out var slot) && linkValue.AsString() is { } link)
					{
						equipment[slot] = link;
					}
				}
			}

			characters.Add(new UploadCharacter(
				characterName,
				characterTable.GetString("classFile"),
				characterTable.GetString("raceFile"),
				characterTable.GetInt("raceId"),
				characterTable.GetInt("gender"),
				characterTable.GetInt("level"),
				professions,
				characterTable.GetString("guild"),
				equipment,
				characterTable.GetBool("officer"),
				ReadQuests(characterTable.GetTable("quests"))));
		}

		return characters.Count == 0 && orders is null && orderActions is null && !hasEventData
			? null
			: new UploadPayload(characters, orders, orderActions, null, events, signups, placements, reserves, hardReserves, duties,
				raids, loot, attendanceCorrections, lootCorrections, offers);
	}

	// Clé d'un événement : numérique = id serveur, sinon id client (créé en jeu, pas encore synchronisé).
	private static (int? ServerId, string? ClientId) EventKey(string? key) =>
		key is null ? (null, null) : int.TryParse(key, out var id) ? (id, null) : (null, key);

	// GuildkeepDB.eventEdits : [cléÉvénement] = { title, kind, startsAt, description, by, tank, healer, melee, ranged, cancelled, at }
	private static List<UploadEvent>? ReadEvents(LuaTable? table)
	{
		var events = new List<UploadEvent>();
		foreach (var (key, value) in table?.Map ?? [])
		{
			if (value.AsTable() is not { } e || e.GetString("title") is not { } title || e.GetString("by") is not { } by)
			{
				continue;
			}
			var (serverId, clientId) = EventKey(key);
			events.Add(new UploadEvent(clientId, serverId, title, e.GetString("kind") ?? "raid", e.GetLong("startsAt") ?? 0,
				e.GetString("description"), by, e.GetInt("tank") ?? 0, e.GetInt("healer") ?? 0, e.GetInt("melee") ?? 0,
				e.GetInt("ranged") ?? 0, e.GetBool("cancelled"), e.GetLong("at") ?? 0, e.GetBool("locked")));
		}
		return events.Count == 0 ? null : events;
	}

	// GuildkeepDB.eventSignups : [« clé|personnage »] = { event, character, classFile, role, spec, response, note, at }
	private static List<UploadSignup>? ReadSignups(LuaTable? table)
	{
		var signups = new List<UploadSignup>();
		foreach (var value in (IEnumerable<LuaValue>?)table?.Map.Values ?? [])
		{
			if (value.AsTable() is not { } s || s.GetString("character") is not { } character || s.GetString("response") is not { } response)
			{
				continue;
			}
			var (eventId, eventClientId) = EventKey(s.GetString("event"));
			if (eventId is null && eventClientId is null)
			{
				continue;
			}
			signups.Add(new UploadSignup(eventId, eventClientId, character, s.GetString("classFile"), s.GetString("role") ?? "melee",
				s.GetString("spec"), response, s.GetString("note"), s.GetLong("at") ?? 0));
		}
		return signups.Count == 0 ? null : signups;
	}

	// GuildkeepDB.eventReserves : [« clé|personnage|priorité »] = { event, character, priority, itemId, itemName, by, at }
	private static List<UploadReserve>? ReadReserves(LuaTable? table)
	{
		var list = new List<UploadReserve>();
		foreach (var value in (IEnumerable<LuaValue>?)table?.Map.Values ?? [])
		{
			if (value.AsTable() is not { } r || r.GetString("character") is not { } character ||
				r.GetString("priority") is not { } priority || r.GetString("by") is not { } by)
			{
				continue;
			}
			var (eventId, eventClientId) = EventKey(r.GetString("event"));
			if (eventId is null && eventClientId is null)
			{
				continue;
			}
			list.Add(new UploadReserve(eventId, eventClientId, character, priority, r.GetInt("itemId") ?? 0,
				r.GetString("itemName"), by, r.GetLong("at") ?? 0));
		}
		return list.Count == 0 ? null : list;
	}

	// GuildkeepDB.eventHardReserves : [« clé|objet »] = { event, itemId, itemName, holder, active, by, at }
	private static List<UploadHardReserve>? ReadHardReserves(LuaTable? table)
	{
		var list = new List<UploadHardReserve>();
		foreach (var value in (IEnumerable<LuaValue>?)table?.Map.Values ?? [])
		{
			if (value.AsTable() is not { } h || h.GetInt("itemId") is not { } itemId || h.GetString("by") is not { } by)
			{
				continue;
			}
			var (eventId, eventClientId) = EventKey(h.GetString("event"));
			if (eventId is null && eventClientId is null)
			{
				continue;
			}
			list.Add(new UploadHardReserve(eventId, eventClientId, itemId, h.GetString("itemName"), h.GetString("holder"),
				h.GetBool("active"), by, h.GetLong("at") ?? 0));
		}
		return list.Count == 0 ? null : list;
	}

	// GuildkeepDB.eventDuties : [« clé|rôle »] = { event, duty, character, by, at } (character absent = poste libéré)
	private static List<UploadDuty>? ReadDuties(LuaTable? table)
	{
		var list = new List<UploadDuty>();
		foreach (var value in (IEnumerable<LuaValue>?)table?.Map.Values ?? [])
		{
			if (value.AsTable() is not { } d || d.GetString("duty") is not { } duty || d.GetString("by") is not { } by)
			{
				continue;
			}
			var (eventId, eventClientId) = EventKey(d.GetString("event"));
			if (eventId is null && eventClientId is null)
			{
				continue;
			}
			list.Add(new UploadDuty(eventId, eventClientId, duty, d.GetString("character"), by, d.GetLong("at") ?? 0));
		}
		return list.Count == 0 ? null : list;
	}

	private static IEnumerable<LuaTable> Rows(LuaTable? table) =>
		((IEnumerable<LuaValue>?)table?.Map.Values ?? []).Select(v => v.AsTable()).OfType<LuaTable>();

	// GuildkeepDB.raidSessions : [clé] = { key, instance, day, leader, startedAt, endedAt, observer, participants = { [nom] = minutes }, classes = { [nom] = classe } }
	private static List<UploadRaid>? ReadRaids(LuaTable? table)
	{
		var list = new List<UploadRaid>();
		foreach (var r in Rows(table))
		{
			if (r.GetString("key") is not { } key || r.GetString("instance") is not { } instance || r.GetString("day") is not { } day ||
				r.GetString("observer") is not { } observer || r.GetTable("participants") is not { } participantsTable)
			{
				continue;
			}
			var participants = new Dictionary<string, int>();
			foreach (var (name, minutes) in participantsTable.Map)
			{
				if (minutes.AsInt() is { } m)
				{
					participants[name] = m;
				}
			}
			Dictionary<string, string>? classes = null;
			if (r.GetTable("classes") is { } classesTable)
			{
				classes = [];
				foreach (var (name, classFile) in classesTable.Map)
				{
					if (classFile.AsString() is { } c)
					{
						classes[name] = c;
					}
				}
			}
			list.Add(new UploadRaid(key, instance, day, r.GetString("leader"), r.GetLong("startedAt") ?? 0, r.GetLong("endedAt") ?? 0,
				observer, participants, classes));
		}
		return list.Count == 0 ? null : list;
	}

	// GuildkeepDB.raidLoot : [« raid|personnage|objet|n »] = { raid, character, itemId, itemName, quality, ordinal, at, observer }
	private static List<UploadRaidLoot>? ReadLoot(LuaTable? table)
	{
		var list = Rows(table)
			.Where(l => l.GetString("raid") is not null && l.GetString("character") is not null && l.GetInt("itemId") is not null && l.GetString("observer") is not null)
			.Select(l => new UploadRaidLoot(l.GetString("raid")!, l.GetString("character")!, l.GetInt("itemId")!.Value, l.GetString("itemName"),
				l.GetInt("quality") ?? 0, l.GetInt("ordinal") ?? 1, l.GetLong("at") ?? 0, l.GetString("observer")!))
			.ToList();
		return list.Count == 0 ? null : list;
	}

	// GuildkeepDB.attendanceCorrections : [« raid|personnage »] = { raid, character, status, note, by, at }
	private static List<UploadAttendanceCorrection>? ReadAttendanceCorrections(LuaTable? table)
	{
		var list = Rows(table)
			.Where(c => c.GetString("raid") is not null && c.GetString("character") is not null && c.GetString("status") is not null && c.GetString("by") is not null)
			.Select(c => new UploadAttendanceCorrection(c.GetString("raid")!, c.GetString("character")!, c.GetString("status")!,
				c.GetString("note"), c.GetString("by")!, c.GetLong("at") ?? 0))
			.ToList();
		return list.Count == 0 ? null : list;
	}

	// GuildkeepDB.lootCorrections : [« raid|personnage|objet|n »] = { raid, character, itemId, ordinal, void, note, by, at }
	private static List<UploadLootCorrection>? ReadLootCorrections(LuaTable? table)
	{
		var list = Rows(table)
			.Where(c => c.GetString("raid") is not null && c.GetString("character") is not null && c.GetInt("itemId") is not null && c.GetString("by") is not null)
			.Select(c => new UploadLootCorrection(c.GetString("raid")!, c.GetString("character")!, c.GetInt("itemId")!.Value,
				c.GetInt("ordinal") ?? 1, c.GetBool("void"), c.GetString("note"), c.GetString("by")!, c.GetLong("at") ?? 0))
			.ToList();
		return list.Count == 0 ? null : list;
	}

	// GuildkeepDB.eventPlacements : [« clé|personnage »] = { event, character, placement, by, at }
	private static List<UploadPlacement>? ReadPlacements(LuaTable? table)
	{
		var placements = new List<UploadPlacement>();
		foreach (var value in (IEnumerable<LuaValue>?)table?.Map.Values ?? [])
		{
			if (value.AsTable() is not { } p || p.GetString("character") is not { } character ||
				p.GetString("placement") is not { } placement || p.GetString("by") is not { } by)
			{
				continue;
			}
			var (eventId, eventClientId) = EventKey(p.GetString("event"));
			if (eventId is null && eventClientId is null)
			{
				continue;
			}
			placements.Add(new UploadPlacement(eventId, eventClientId, character, placement, by, p.GetLong("at") ?? 0));
		}
		return placements.Count == 0 ? null : placements;
	}

	// characters[nom].quests : { { id, title, kind, location, level, complete }, … } ; absent = pas relevé
	private static List<UploadQuest>? ReadQuests(LuaTable? table)
	{
		if (table is null)
		{
			return null;
		}
		return table.Items
			.Select(item => item.AsTable())
			.OfType<LuaTable>()
			.Where(q => q.GetInt("id") is not null && q.GetString("title") is not null && q.GetString("kind") is not null)
			.Select(q => new UploadQuest(q.GetInt("id")!.Value, q.GetString("title")!, q.GetString("kind")!,
				q.GetString("location"), q.GetInt("level"), q.GetBool("complete")))
			.ToList();
	}

	// GuildkeepDB.orders : [localId] = { requester, crafter (absent = demande ouverte), item, qty, note, createdAt, recipeId }
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
				order.GetString("item") is not { } item)
			{
				continue;
			}
			orders.Add(new UploadOrder(
				clientId, requester, order.GetString("crafter"), item,
				order.GetInt("qty") ?? 1,
				order.GetString("note"),
				order.GetString("createdAt"),
				order.GetInt("recipeId")));
		}
		return orders.Count == 0 ? null : orders;
	}

	// GuildkeepDB.orderActions : [cléServeurOuLocale] = { status = "accepted"|"ready"|"mailed"|"done"|"cancelled", crafter (offre retenue) }
	private static List<UploadOrderAction>? ReadOrderActions(LuaTable? actionsTable)
	{
		if (actionsTable is null || actionsTable.Map.Count == 0)
		{
			return null;
		}
		var actions = new List<UploadOrderAction>();
		foreach (var (key, value) in actionsTable.Map)
		{
			if (value.AsTable() is not { } action || action.GetString("status") is not { } status)
			{
				continue;
			}
			var crafter = action.GetString("crafter");
			actions.Add(int.TryParse(key, out var serverId)
				? new UploadOrderAction(serverId, null, status, crafter)
				: new UploadOrderAction(null, key, status, crafter));
		}
		return actions.Count == 0 ? null : actions;
	}

	// GuildkeepDB.orderOffers : [« commande|artisan »] = { order, crafter, note, withdrawn }
	private static List<UploadOffer>? ReadOffers(LuaTable? table)
	{
		var offers = new List<UploadOffer>();
		foreach (var value in (IEnumerable<LuaValue>?)table?.Map.Values ?? [])
		{
			if (value.AsTable() is not { } offer || offer.GetString("order") is not { } order || offer.GetString("crafter") is not { } crafter)
			{
				continue;
			}
			offers.Add(int.TryParse(order, out var orderId)
				? new UploadOffer(orderId, null, crafter, offer.GetString("note"), offer.GetBool("withdrawn"))
				: new UploadOffer(null, order, crafter, offer.GetString("note"), offer.GetBool("withdrawn")));
		}
		return offers.Count == 0 ? null : offers;
	}
}
