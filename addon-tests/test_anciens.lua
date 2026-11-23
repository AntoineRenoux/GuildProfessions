-- Anciens scripts de test (luatest/*_test.lua des lots 1 à 5), adaptés à
-- l'addon modulaire : mêmes scénarios et mêmes vérifications, mais l'addon
-- est chargé en entier (ordre du .toc, tous les modules actifs) dans des
-- postes isolés, et l'API interne renommée est suivie (GP.Craft.*,
-- GP.RaidNight.*). Seules les vérifications que test_fonctionnel.lua ne
-- reprenait pas sont réécrites ici, à l'identique de l'ancien script.

local T = require("helpers")
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

local function Station(first, last, options)
	options = options or {}
	options.first, options.last = first, last
	options.settings = options.settings or T.Settings()
	options.db = options.db or { characters = { [first .. " " .. last] = { guild = T.GUILD } }, importedByGuild = {} }
	local game = T.Started(options)
	game:RunTimers()
	game.sent = {}
	return game
end

-- events_test.lua -------------------------------------------------------------------------

test("ancien events_test : droits, réponse locale, export des références, version la plus récente", function()
	local server = { guilds = { {
		version = 3, guildName = T.GUILD, characters = {}, orders = {},
		events = { {
			id = 12, clientId = "Thorgal Marteau-1794990000-1111", title = "Cœur du Magma", kind = "raid",
			startsAt = T.NOW + 86400, organizer = "Thorgal Marteau", tank = 2, healer = 6, melee = 8, ranged = 8,
			cancelled = false, updatedAt = 100,
			signups = { { character = "Nayra Solen", classFile = "PRIEST", role = "healer", response = "yes", responseAt = 50, placement = "pending", placementAt = 0 } },
		} },
	} } }
	local game = Station("Thorgal", "Marteau", { serverData = server, roster = { { "Thorgal-Marteau", 1, true }, { "Nayra-Solen", 4, true } } })
	local E = game.GP.Events
	local events = E.GetEvents()
	T.check(#events == 1 and events[1].key == "12", "l'événement serveur est listé sous son id")
	T.check(E.CanManage(events[1]), "l'organisateur peut gérer son événement")
	T.check(E.CountAwaitingAnswer() == 1, "un événement attend la réponse du joueur")
	E.Respond(events[1], "yes", "tank", "Protection", "")
	local ev = E.Find("12")
	T.check(ev.mine and ev.mine.response == "yes" and ev.mine.role == "tank", "la réponse locale apparaît aussitôt")
	T.check(E.CountAwaitingAnswer() == 0, "plus rien n'attend de réponse")
	game:RunTimers()
	T.check(#game.sent >= 1, "la réponse est annoncée sur le canal de guilde")
	E.Place(ev, "Nayra Solen", "confirmed")
	local nayra
	for _, row in ipairs(E.Find("12").signups) do
		if row.character == "Nayra Solen" then
			nayra = row
		end
	end
	T.check(nayra.placement == "confirmed" and nayra.response == "yes", "le placement garde la réponse du joueur")
	game.now = game.now + 10
	local key = E.Create({ title = "Onyxia", kind = "raid", startsAt = game.now + 2 * 86400, tank = 1 })
	E.Respond(E.Find(key), "tentative", "tank", nil, "en retard")
	T.check(E.Find(key).mine.response == "tentative", "on peut répondre à un événement local")

	-- Un autre membre ne gère pas l'événement d'autrui, sauf officier (autre poste).
	local other = Station("Nayra", "Solen", { serverData = server, roster = { { "Thorgal-Marteau", 1, true }, { "Nayra-Solen", 4, true } } })
	T.check(not other.GP.Events.CanManage(other.GP.Events.Find("12")), "un simple membre ne gère pas l'événement d'un autre")
	other.officer = true
	T.check(other.GP.Events.CanManage(other.GP.Events.Find("12")), "un officier gère tous les événements")

	local payload = game.GP.BuildExportPayload()
	T.check(#payload.events == 1 and payload.events[1].clientId == key and payload.events[1].serverId == nil,
		"export : l'événement créé part avec son id client")
	local refs = {}
	for _, s in ipairs(payload.signups) do
		refs[s.eventId or s.eventClientId] = s.response
	end
	T.check(refs[12] == "yes" and refs[key] == "tentative", "export : réponses vers l'id serveur ou l'id client")

	local section = game.env.Guildkeep_ServerData.guilds[1]
	section.events[1].signups = {
		{ character = "Nayra Solen", role = "healer", response = "yes", responseAt = 50, placement = "confirmed", placementAt = game.now + 100 },
		{ character = "Thorgal Marteau", role = "tank", response = "yes", responseAt = game.now + 100, placement = "pending", placementAt = 0 },
	}
	section.events[2] = { id = 13, clientId = key, title = "Onyxia", kind = "raid", startsAt = game.now + 2 * 86400, organizer = "Thorgal Marteau",
		tank = 1, healer = 0, melee = 0, ranged = 0, cancelled = false, updatedAt = game.now + 100,
		signups = { { character = "Thorgal Marteau", role = "tank", response = "tentative", responseAt = game.now + 100, placement = "pending", placementAt = 0 } } }
	E.Prune()
	local db = game.env.GuildkeepDB
	T.check(next(db.eventEdits) == nil and next(db.eventSignups) == nil and next(db.eventPlacements) == nil, "purge")
	T.check(#E.GetEvents() == 2 and E.Find(key).id == 13, "l'événement local est retrouvé sous son nouvel id serveur")
	db.eventEdits["13"] = { title = "Vieux titre", kind = "raid", startsAt = game.now, by = "Thorgal Marteau", at = 5 }
	T.check(E.Find("13").title == "Onyxia", "la version la plus récente gagne")
	T.CheckClean(game)
	T.CheckClean(other)
end)

-- peer_test.lua ---------------------------------------------------------------------------

test("ancien peer_test : message en plusieurs morceaux, désordre, usurpation refusée", function()
	local server = T.ServerData()
	local thorgal = Station("Thorgal", "Marteau", { serverData = server })
	local nayra = Station("Nayra", "Solen", { serverData = server })
	local elaria = Station("Elaria", "Brume", { serverData = server })
	local key = thorgal.GP.Events.Create({ title = "Cœur du Magma", kind = "raid", startsAt = T.NOW + 86400,
		description = string.rep("Flacons, réparations, buffs. ", 15), tank = 2 })
	thorgal:RunTimers()
	local sent = thorgal.sent
	thorgal.sent = {}
	T.check(#sent >= 2, ("message découpé en %d morceaux"):format(#sent))
	for _, m in ipairs(sent) do
		T.check(#m.message <= 255, "morceau sous la limite de 255 octets")
	end
	-- Remis dans le désordre à Nayra, dans l'ordre à Elaria.
	for i = #sent, 1, -1 do
		nayra:Fire("CHAT_MSG_ADDON", sent[i].prefix, sent[i].message, "GUILD", "Thorgal-Marteau")
	end
	for _, m in ipairs(sent) do
		elaria:Fire("CHAT_MSG_ADDON", m.prefix, m.message, "GUILD", "Thorgal-Marteau")
	end
	local ev = nayra.GP.Events.Find(key)
	T.check(ev ~= nil and ev.title == "Cœur du Magma" and ev.tank == 2, "l'événement reçu d'un autre joueur s'affiche")
	T.check(#ev.description > 300, "la description complète est réassemblée")
	T.check(next(nayra.env.GuildkeepDB.eventEdits) == nil, "un événement reçu n'est pas renvoyé au serveur par ce poste")

	-- Un joueur ne peut pas parler au nom d'un autre.
	thorgal.GP.Comm.Broadcast({ t = "su", s = { event = key, character = "Thorgal Marteau", role = "tank", response = "no", at = T.NOW + 50 } })
	thorgal:RunTimers()
	for _, m in ipairs(thorgal.sent) do
		nayra:Fire("CHAT_MSG_ADDON", m.prefix, m.message, "GUILD", "Kael-Fourbe")
	end
	thorgal.sent = {}
	T.check(nayra.GP.Events.Find(key).mine == nil and #nayra.GP.Events.Find(key).signups == 0, "une réponse au nom d'un autre est ignorée")

	-- Un membre ordinaire (Nayra, rang 4) ne modifie pas l'événement de Thorgal (vu chez Elaria).
	nayra.GP.Comm.Broadcast({ t = "ev", key = key, e = { title = "Piraté", kind = "raid", startsAt = T.NOW + 86400, by = "Nayra Solen",
		tank = 0, healer = 0, melee = 0, ranged = 0, cancelled = true, at = T.NOW + 999 } })
	nayra:DeliverTo(elaria)
	T.check(elaria.GP.Events.Find(key).title == "Cœur du Magma" and not elaria.GP.Events.Find(key).cancelled,
		"un membre ordinaire ne modifie pas l'événement d'un autre")
	T.CheckClean(thorgal)
	T.CheckClean(nayra)
	T.CheckClean(elaria)
end)

-- night_test.lua --------------------------------------------------------------------------

test("ancien night_test : réserver pour un autre refusé, verrou côté joueur, objet invalide", function()
	local server = T.RichServerData()
	local ev12 = server.guilds[1].events[1]
	ev12.reserves, ev12.duties = {}, {}
	local nayra = Station("Nayra", "Solen", { serverData = server })
	local E, R = nayra.GP.Events, nayra.GP.RaidNight
	T.check(not R.SetReserve(E.Find("12"), "high", 17076, "x", "Thorgal Marteau"), "un joueur ne réserve pas pour un autre")
	local thorgal = Station("Thorgal", "Marteau", { serverData = server })
	local TE, TR = thorgal.GP.Events, thorgal.GP.RaidNight
	T.check(TR.SetReserve(TE.Find("12"), "high", 18814, "Pierre", "Kael Fourbe"), "l'organisateur réserve pour un joueur sans addon")
	TE.SetLocked(TE.Find("12"), true)
	thorgal:DeliverTo(nayra)
	T.check(E.Find("12").locked, "le verrou est reçu")
	local ok = R.SetReserve(E.Find("12"), "normal", 18814, "Pierre")
	T.check(not ok, "verrouillé : un joueur ne change plus ses réservations")
	T.check(R.ParseItem("|cffa335ee|Hitem:19019::::::::60:::::|h[x]|h|r") == 19019 and R.ParseItem("17076") == 17076 and not R.ParseItem("abc"),
		"lecture des liens et numéros d'objet")
	local payload = thorgal.GP.BuildExportPayload()
	T.check(payload.events[1].locked == true, "export : verrou de l'événement")
	T.CheckClean(nayra)
	T.CheckClean(thorgal)
end)

-- attendance_test.lua ---------------------------------------------------------------------

test("ancien attendance_test : quantités, doublons, annulation, taux, purge", function()
	local roster = {}
	for _, name in ipairs({ "Thorgal-Marteau", "Nayra-Solen", "Elaria-Brume", "Kael-Fourbe", "Mira-Vent", "Brann-Roc" }) do
		roster[#roster + 1] = { name, 3, true }
	end
	local game = Station("Thorgal", "Marteau", { roster = roster, serverData = T.ServerData() })
	local A = game.GP.Attendance
	game.raid = { instance = "Cœur du Magma", members = { { "Thorgal", 2, "WARRIOR", true }, { "Nayra-Solen", 0, "PRIEST", true },
		{ "Elaria-Brume", 0, "MAGE", true }, { "Kael-Fourbe", 0, "ROGUE", true }, { "Pugg-Hors", 0, "HUNTER", true } } }
	A.Tick()
	T.check(next(game.env.GuildkeepDB.raidSessions) == nil, "moins de 5 membres de la guilde : rien n'est relevé")
	table.insert(game.raid.members, { "Mira-Vent", 0, "DRUID", false })
	A.Tick()
	local session = A.CurrentRaid()
	T.check(session and session.key:find("|Cœur du Magma|Thorgal Marteau", 1, true), "raid de guilde : clé jour|instance|chef")
	for _ = 1, 20 do
		game.now = game.now + 60
		A.Tick()
	end
	T.check(session.participants["Nayra Solen"] == 21 and session.participants["Mira Vent"] == 0, "minutes comptées, déconnecté à zéro")
	T.check(session.participants["Pugg Hors"] == nil and session.classes["Nayra Solen"] == "PRIEST", "hors guilde ignoré, classe notée")
	A.OnLoot("Nayra-Solen reçoit le butin : |cffff8000|Hitem:19019::::|h[Serre-tonnerre]|h|r.")
	A.OnLoot("Vous recevez le butin : |cffa335ee|Hitem:18814::::|h[Pierre]|h|rx2.")
	A.OnLoot("Kael-Fourbe reçoit le butin : |cff1eff00|Hitem:1234::::|h[Babiole]|h|r.")
	A.OnLoot("Pugg-Hors reçoit le butin : |cffa335ee|Hitem:18814::::|h[Pierre]|h|r.")
	A.OnLoot("Nayra-Solen reçoit le butin : |cffff8000|Hitem:19019::::|h[Serre-tonnerre]|h|r.")
	local count, nayra = 0, 0
	for _, l in pairs(game.env.GuildkeepDB.raidLoot) do
		count = count + 1
		if l.character == "Nayra Solen" then
			nayra = nayra + 1
		end
	end
	T.check(count == 3 and nayra == 2, ("butin épique des membres seulement, quantité x2 et doublon numérotés (%d, %d)"):format(count, nayra))
	T.check(not A.Correct(A.GetRaids()[1], "Kael Fourbe", "excused"), "correction refusée à un simple membre")
	game.officer = true
	T.check(A.Correct(A.GetRaids()[1], "Kael Fourbe", "excused", "Déplacement"), "un officier excuse un joueur")
	local raid = A.GetRaids()[1]
	T.check(raid.pendingSync and #raid.loot == 3, "le raid local s'affiche avant la synchro")
	local legendary
	for _, l in ipairs(raid.loot) do
		if l.itemId == 19019 and l.ordinal == 1 then
			legendary = l
		end
	end
	T.check(A.SetVoid(raid, legendary, true, "Désenchanté"), "un officier annule un objet")
	local raids = A.GetRaids()
	local stats = A.Stats(raids, "Nayra Solen")
	T.check(stats.d30.attended == 1 and stats.d30.eligible == 1 and stats.loot == 1, "Nayra : 1/1, un objet (l'autre annulé)")
	T.check(A.Stats(raids, "Mira Vent").d30.attended == 0, "Mira, déconnectée tout le raid : absente")
	T.check(A.Stats(raids, "Kael Fourbe").d30.eligible == 0, "Kael excusé : hors calcul")
	local payload = game.GP.BuildExportPayload()
	T.check(#payload.raids == 1 and #payload.loot == 3 and #payload.attendanceCorrections == 1 and #payload.lootCorrections == 1,
		"export : raid, butin et corrections")
	T.check(payload.raids[1].participants["Nayra Solen"] == 21, "export : minutes par participant")
	game.raid = nil
	A.Tick()
	game.now = game.now + 7200
	game.env.Guildkeep_ServerData.guilds[1].raids = { {
		id = 4, key = session.key, instance = "Cœur du Magma", day = session.day, startedAt = session.startedAt, endedAt = session.endedAt,
		participants = { { character = "Nayra Solen", minutes = 21 } },
		loot = {
			{ id = 1, character = "Nayra Solen", itemId = 19019, ordinal = 1, quality = 5, at = 1, voided = true, voidAt = game.now },
			{ id = 2, character = "Nayra Solen", itemId = 19019, ordinal = 2, quality = 5, at = 1, voided = false, voidAt = 0 },
			{ id = 3, character = "Thorgal Marteau", itemId = 18814, ordinal = 1, quality = 4, at = 1, voided = false, voidAt = 0 },
		},
		corrections = { { character = "Kael Fourbe", status = "excused", at = game.now } },
	} }
	A.Prune()
	local db = game.env.GuildkeepDB
	T.check(next(db.raidSessions) == nil and next(db.raidLoot) == nil, "purge : relevé et butin repris")
	T.check(next(db.attendanceCorrections) == nil and next(db.lootCorrections) == nil, "purge : corrections reprises")
	T.check(A.GetRaids()[1].id == 4, "le raid vient maintenant du serveur")
	T.CheckClean(game)
end)

-- craft_test.lua --------------------------------------------------------------------------

test("ancien craft_test : expiration des demandes ouvertes et purge des deux côtés", function()
	local nayra = Station("Nayra", "Solen", { serverData = T.ServerData() })
	local thorgal = Station("Thorgal", "Marteau", { serverData = T.ServerData(),
		db = { characters = { ["Thorgal Marteau"] = { guild = T.GUILD, professions = { Alchimie = { recipes = { 17635 }, catalog = { 17635, 17636 } } } } },
			importedByGuild = {} } })
	local N, Th = nayra.GP.Craft, thorgal.GP.Craft
	local key = N.CreateOrder(nil, "Flacon des Titans", 2, "Composants fournis", 17635)
	nayra:DeliverTo(thorgal)
	Th.SetOffer(Th.GetOrders()[1], "Ce soir", false)
	thorgal:DeliverTo(nayra)
	N.SetOrderStatus(N.GetOrders()[1], "accepted", "Thorgal Marteau")
	nayra:DeliverTo(thorgal)
	local key2 = N.CreateOrder(nil, "Lingot", 5, nil, nil)
	nayra.now = nayra.now + 6 * 86400
	local expired
	for _, o in ipairs(N.GetOrders()) do
		if o.key == key2 then
			expired = o
		end
	end
	T.check(expired.status == "expired", "demande ouverte expirée après 5 jours")
	local orders = {
		{ id = 14, clientId = key, requester = "Nayra Solen", crafter = "Thorgal Marteau", item = "Flacon des Titans", qty = 2, status = "done",
			createdUtc = "2026-11-23 10:00", offers = { { crafter = "Thorgal Marteau", note = "Ce soir", createdUtc = "2026-11-23 10:05" } } },
		{ id = 15, clientId = key2, requester = "Nayra Solen", item = "Lingot", qty = 5, status = "expired", createdUtc = "2026-11-23 10:00" },
	}
	nayra.env.Guildkeep_ServerData.guilds[1].orders = orders
	thorgal.env.Guildkeep_ServerData.guilds[1].orders = orders
	N.PruneOrders()
	T.check(next(nayra.env.GuildkeepDB.orders) == nil and next(nayra.env.GuildkeepDB.orderActions) == nil, "purge côté Nayra")
	Th.PruneOrders()
	local tdb = thorgal.env.GuildkeepDB
	T.check(next(tdb.orderOffers) == nil and next(tdb.orderPeer["former hope"].actions) == nil, "purge côté Thorgal (offre et actions reçues)")
	T.CheckClean(nayra)
	T.CheckClean(thorgal)
end)

-- quest_test.lua --------------------------------------------------------------------------

test("ancien quest_test : filtre élite/donjon, en-têtes, serveur fusionné, quête rendue", function()
	local log = {
		{ title = "Pic Rochenoire", isHeader = true },
		{ title = "Avertir Thrall", questID = 4903, level = 58, suggestedGroup = 5, tagID = 81 },
		{ title = "Maleterre de l'Ouest", isHeader = true },
		{ title = "Tueur de géants", questID = 5001, level = 55, suggestedGroup = 3, tagID = 1, complete = true },
		{ title = "Ramasser des fleurs", questID = 9000, level = 55, suggestedGroup = 0 },
	}
	local server = { guilds = { { version = 1, guildName = T.GUILD, orders = {}, characters = {
		{ name = "Elaria Brume", classFile = "MAGE", quests = { { id = 3000, title = "Vieille quête", kind = "elite", location = "Tanaris" },
			{ id = 4903, title = "Avertir Thrall", kind = "dungeon", location = "Pic Rochenoire" } } },
	} } } }
	local game = T.Game({ settings = T.Settings(), serverData = server, quests = log })
	game.env.C_QuestLog.IsComplete = function(id)
		for _, q in ipairs(log) do
			if q.questID == id then
				return q.complete
			end
		end
	end
	game.env.C_QuestLog.IsQuestFlaggedCompleted = function(id)
		return id == 3000
	end
	game:Login()
	local Q = game.GP.Quests
	Q.Scan()
	local quests = game.env.GuildkeepDB.characters["Thorgal Marteau"].quests
	T.check(#quests == 2, "seules les quêtes élites et de donjon sont relevées")
	T.check(quests[1].kind == "dungeon" and quests[1].location == "Pic Rochenoire", "donjon rangé sous son en-tête")
	T.check(quests[2].kind == "elite" and quests[2].complete, "élite, objectifs remplis")
	local all = Q.GetAll()
	T.check(all[4903].holders["Thorgal Marteau"] and all[4903].holders["Elaria Brume"], "Thorgal et Elaria (serveur) ont Avertir Thrall")
	T.check(Q.TurnedIn(3000) and not Q.TurnedIn(4903), "quête déjà rendue reconnue")
	table.remove(log, 2)
	Q.Scan()
	T.check(#game.env.GuildkeepDB.characters["Thorgal Marteau"].quests == 1, "quête rendue : elle disparaît du relevé")
	local mine
	for _, c in ipairs(game.GP.BuildExportPayload().characters) do
		if c.name == "Thorgal Marteau" then
			mine = c
		end
	end
	T.check(mine.quests and mine.quests[1].id == 5001, "export : quêtes du personnage")
	T.CheckClean(game)
end)

return tests
