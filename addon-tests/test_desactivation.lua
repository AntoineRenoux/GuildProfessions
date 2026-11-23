-- Chaque module désactivé seul : il ne relève rien, n'écoute rien, n'envoie
-- rien, n'affiche pas son onglet, ses données restent ; les autres modules
-- fonctionnent normalement.

local T = require("helpers")
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

-- Données déjà présentes de chaque module, qui doivent survivre à sa désactivation.
local function ExistingDB()
	return {
		characters = { ["Thorgal Marteau"] = { guild = T.GUILD } },
		importedByGuild = {},
		orders = { ["Thorgal Marteau-1-1"] = { requester = "Thorgal Marteau", item = "Lingot", qty = 1, createdTs = T.NOW, createdAt = "2026-11-23 10:00" } },
		orderActions = {}, orderOffers = {}, orderPeer = {},
		eventEdits = { ["Thorgal Marteau-1-2"] = { title = "Onyxia", kind = "raid", startsAt = T.NOW + 86400, by = "Thorgal Marteau", at = T.NOW,
			tank = 1, healer = 0, melee = 0, ranged = 0 } },
		eventSignups = {}, eventPlacements = {}, eventPeer = {},
		eventReserves = { ["Thorgal Marteau-1-2|Thorgal Marteau|high"] = { event = "Thorgal Marteau-1-2", character = "Thorgal Marteau",
			priority = "high", itemId = 19019, by = "Thorgal Marteau", at = T.NOW } },
		eventHardReserves = {}, eventDuties = {},
		raidSessions = { ["2026-11-20|Onyxia|Thorgal Marteau"] = { key = "2026-11-20|Onyxia|Thorgal Marteau", instance = "Onyxia",
			day = "2026-11-20", startedAt = T.NOW - 86400, endedAt = T.NOW - 80000, guild = T.GUILD, participants = {}, classes = {} } },
		raidLoot = {}, attendanceCorrections = {}, lootCorrections = {},
	}
end

local function Start(disabled)
	local game = T.Started({
		settings = T.Settings(disabled),
		db = ExistingDB(),
		serverData = T.RichServerData(),
		professions = { { name = "Alchimie", level = 300, max = 300, skillLine = 171 } },
		equipment = { [1] = "|cffa335ee|Hitem:16921::::|h[Auréole]|h|r" },
		quests = { { questID = 4001, title = "Le cœur du Magma", tagID = 62, isHeader = false } },
	})
	game.loginLines = T.ModuleLines(game)
	return game
end

-- Messages reçus d'un autre joueur connecté (Nayra), pour chaque module.
local function Peer(game)
	local nayra = T.Game({ first = "Nayra", last = "Solen", classFile = "PRIEST", settings = T.Settings(),
		db = { characters = { ["Nayra Solen"] = { guild = T.GUILD } }, importedByGuild = {} }, serverData = T.RichServerData(),
		quests = { { questID = 4002, title = "Onyxia", tagID = 62, isHeader = false } } }):Login()
	nayra:RunTimers()
	nayra.sent = {}
	local GP = nayra.GP
	GP.Craft.CreateOrder(nil, "Flacon des Titans", 2, nil, 17635)
	GP.Events.Create({ title = "Zul'Gurub", kind = "raid", startsAt = T.NOW + 3 * 86400, tank = 1 })
	GP.RaidNight.SetReserve(GP.Events.Find("12"), "low", 18814, "Pierre")
	GP.Quests.Scan()
	GP.Quests.Announce()
	return nayra:DeliverTo(game)
end

-- Vérifications communes : messages de connexion, onglets, fenêtre.
local function CheckCommon(game, disabled)
	local off = {}
	for _, id in ipairs(disabled) do
		off[id] = true
	end
	local lines = game.loginLines
	for i, id in ipairs(T.MODULES) do
		local expected
		if off[id] then
			expected = ("module %s désactivé"):format(T.LABELS[id])
		elseif id == "raid" and off.events then
			expected = "module Soirée de raid désactivé : il nécessite le module Événements"
		else
			expected = ("module %s chargé"):format(T.LABELS[id])
		end
		T.equal(lines[i], expected, "message de connexion du module " .. id)
	end
	local labels = T.ShowEveryTab(game)
	for id, label in pairs(T.TABS) do
		T.equal(T.contains(labels, label), not off[id], "onglet " .. label .. (off[id] and " absent" or " présent"))
	end
	T.check(game.GP.IsMainFrameShown(), "la fenêtre s'ouvre")
	T.CheckClean(game)
end

local function Export(game)
	return game.GP.BuildExportPayload()
end

test("module Métiers désactivé seul", function()
	local game = Start({ "professions" })
	local char = game.env.GuildkeepDB.characters["Thorgal Marteau"]
	game:Fire("SKILL_LINES_CHANGED")
	game:Fire("TRADE_SKILL_SHOW")
	game:RunTimers()
	T.check(char.classFile == "WARRIOR", "le socle relève toujours l'identité")
	T.check(char.professions == nil, "aucun métier relevé")
	T.check(char.equipment ~= nil, "le module Membres relève toujours l'équipement")
	game.prints = {}
	game:Slash("debug")
	T.check(not T.find(game:PlainPrints(), "relevé des recettes"), "pas de diagnostic des recettes")
	CheckCommon(game, { "professions" })
end)

test("module Membres désactivé seul", function()
	local game = Start({ "members" })
	local char = game.env.GuildkeepDB.characters["Thorgal Marteau"]
	game:Fire("PLAYER_EQUIPMENT_CHANGED")
	T.check(char.equipment == nil, "aucun équipement relevé")
	T.check(char.professions and char.professions.Alchimie, "le module Métiers relève toujours")
	CheckCommon(game, { "members" })
end)

test("module Artisanat désactivé seul", function()
	local game = Start({ "craft" })
	local db = game.env.GuildkeepDB
	T.check(not game.prefixes.GPROF, "préfixe des commandes non enregistré")
	Peer(game)
	T.check(next(db.orderPeer) == nil, "commande d'un autre joueur ignorée")
	T.check(db.orders["Thorgal Marteau-1-1"], "les commandes déjà présentes restent")
	T.check(Export(game).orders == nil, "aucune commande exportée")
	T.check(game.GP.Modules.Service("craft.order") == nil, "pas de bouton « Commander » dans la fenêtre des recettes")
	game:Fire("BAG_UPDATE_DELAYED")
	local events = game.GP.Events.GetEvents()
	T.check(#events >= 2, "les événements fonctionnent (serveur, local, reçu)")
	CheckCommon(game, { "craft" })
end)

test("module Événements désactivé seul (Soirée de raid suit)", function()
	local game = Start({ "events" })
	local db = game.env.GuildkeepDB
	Peer(game)
	T.check(next(db.eventPeer) == nil, "événements et réservations d'un autre joueur ignorés")
	game:RunTimers()
	T.check(not T.find(game:PlainPrints(), "attend ta réponse"), "pas de rappel des événements")
	T.check(db.eventEdits["Thorgal Marteau-1-2"] and db.eventReserves["Thorgal Marteau-1-2|Thorgal Marteau|high"],
		"les événements et réservations déjà présents restent")
	local payload = Export(game)
	T.check(payload.events == nil and payload.reserves == nil, "ni événement ni réservation exportés")
	T.check(payload.orders and #payload.orders == 1, "les commandes partent toujours")
	T.check(next(db.orderPeer) ~= nil, "la commande d'un autre joueur est reçue")
	game:Slash("events")
	T.check(game.GP.IsMainFrameShown(), "/gkeep events ouvre simplement la fenêtre")
	game.GP.ToggleMainFrame()
	CheckCommon(game, { "events" })
end)

test("module Soirée de raid désactivé seul", function()
	local game = Start({ "raid" })
	local db = game.env.GuildkeepDB
	Peer(game)
	local store = db.eventPeer["former hope"]
	T.check(store and next(store.events) ~= nil, "les événements d'un autre joueur sont reçus")
	T.check(store.reserves == nil or next(store.reserves) == nil, "ses réservations sont ignorées")
	local event = game.GP.Events.Find("12")
	T.check(event and event.reserves == nil and next(event.night) == nil, "pas de réservations dans l'événement")
	T.check(db.eventReserves["Thorgal Marteau-1-2|Thorgal Marteau|high"], "les réservations déjà présentes restent")
	local payload = Export(game)
	T.check(payload.events and payload.reserves == nil, "événements exportés, pas les réservations")
	T.ShowEveryTab(game)
	for _, object in ipairs(game.objects) do
		local section = rawget(object, "section")
		T.check(section == nil or section == "roster", "pas de section Réserves ni Soirée dans le détail")
	end
	CheckCommon(game, { "raid" })
end)

test("module Présence et butin désactivé seul", function()
	local game = Start({ "attendance" })
	local db = game.env.GuildkeepDB
	T.equal(#T.Tickers(game, 60), 0, "aucune minuterie de présence")
	game.raid = { instance = "Onyxia", members = {
		{ "Thorgal", 2, "WARRIOR", true }, { "Nayra-Solen", 0, "PRIEST", true }, { "Elaria-Brume", 0, "MAGE", true },
		{ "Kael-Fourbe", 0, "ROGUE", true }, { "Mira-Vent", 0, "DRUID", true } } }
	game:Fire("GROUP_ROSTER_UPDATE")
	game:Fire("CHAT_MSG_LOOT", "Nayra-Solen reçoit le butin : |cffa335ee|Hitem:19019::::|h[Serre-tonnerre]|h|r.")
	local count = 0
	for _ in pairs(db.raidSessions) do
		count = count + 1
	end
	T.equal(count, 1, "aucun raid relevé (seul celui déjà présent reste)")
	T.check(next(db.raidLoot) == nil, "aucun butin relevé")
	T.check(Export(game).raids == nil, "aucun raid exporté")
	CheckCommon(game, { "attendance" })
end)

test("module Quêtes désactivé seul", function()
	local game = Start({ "quests" })
	local db = game.env.GuildkeepDB
	game:Fire("QUEST_LOG_UPDATE")
	game:RunTimers()
	T.check(db.characters["Thorgal Marteau"].quests == nil, "journal de quêtes non relevé")
	Peer(game)
	T.check(db.questPeer == nil, "quêtes d'un autre joueur ignorées")
	game:Slash("quetes")
	CheckCommon(game, { "quests" })
end)

test("tous les modules désactivés : la fenêtre s'ouvre et l'explique, rien n'est envoyé", function()
	local game = Start(T.MODULES)
	local db = game.env.GuildkeepDB
	game:RunTimers()
	T.equal(#game.sent, 0, "aucun message d'addon envoyé (pas même la demande de nouveautés)")
	T.check(not game.prefixes.GPROF and not game.prefixes.GKEEPEV, "aucun préfixe enregistré")
	Peer(game)
	T.check(next(db.orderPeer) == nil and next(db.eventPeer) == nil and db.questPeer == nil, "rien n'est reçu")
	local char = db.characters["Thorgal Marteau"]
	T.check(char.classFile and not char.professions and not char.equipment and not char.quests, "seule l'identité est relevée")
	game.GP.ToggleMainFrame()
	T.equal(#game:TabLabels(), 0, "aucun onglet")
	T.check(game.GP.IsMainFrameShown(), "la fenêtre s'ouvre")
	local payload = Export(game)
	T.check(payload.characters and payload.orders == nil and payload.events == nil and payload.raids == nil, "export : personnages seulement")
	T.CheckClean(game)
	for i, id in ipairs(T.MODULES) do
		T.equal(game.loginLines[i], ("module %s désactivé"):format(T.LABELS[id]), "message du module " .. id)
	end
end)

return tests
