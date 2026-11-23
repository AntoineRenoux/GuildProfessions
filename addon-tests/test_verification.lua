-- Vérification indépendante de l'indépendance des modules (relecture sceptique).
--
-- Matrice complète : chaque module, chaque partie (données, interface), mis en
-- échec APRÈS avoir tout branché (pire cas pour le retrait) ; chaque module
-- désactivé seul avec export, fusion des données serveur et trafic d'un autre
-- joueur ; panne d'exécution de la Soirée de raid vue depuis les Événements ;
-- persistance des cases du panneau d'options.

local T = require("helpers")
local Wow = T.Wow
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

-- Clés d'export propres à chaque module.
local EXPORT_KEYS = {
	craft = { "orders", "orderActions", "offers" },
	events = { "events", "signups", "placements" },
	raid = { "reserves", "hardReserves", "duties" },
	attendance = { "raids", "loot", "attendanceCorrections", "lootCorrections" },
}
-- Types de messages du canal partagé GKEEPEV et préfixes propres, par module.
local PEER_KINDS = { events = { ev = true, su = true, pl = true }, raid = { sr = true, hr = true, du = true }, quests = { qs = true } }

local function Parts(game)
	-- Table locale `parts` du registre, lue par introspection pour envelopper la vraie fonction de chargement.
	local define = game.GP.Modules.Define
	for i = 1, 10 do
		local name, value = debug.getupvalue(define, i)
		if name == "parts" then
			return value
		end
		if not name then
			break
		end
	end
	error("table des parties introuvable")
end

-- Types des messages envoyés par un poste (GKEEPEV réassemblé, et préfixes bruts).
local function SentKinds(game)
	local kinds, chunks = {}, {}
	for _, message in ipairs(game.sent) do
		if message.prefix == "GKEEPEV" then
			local id, index, total, part = message.message:match("^(%d+)\t(%d+)\t(%d+)\t(.*)$")
			chunks[id] = chunks[id] or { total = tonumber(total), parts = {} }
			chunks[id].parts[tonumber(index)] = part
		else
			kinds["prefix:" .. message.prefix] = true
		end
	end
	for _, entry in pairs(chunks) do
		local text = table.concat(entry.parts)
		local ok, data = pcall(function()
			return assert(load("return " .. text, "m", "t", {}))()
		end)
		if ok and type(data) == "table" then
			kinds[data.t] = true
		end
	end
	return kinds
end

local function Options(extra)
	local options = {
		settings = T.Settings(),
		serverData = T.RichServerData(),
		professions = { { name = "Alchimie", level = 300, max = 300, skillLine = 171 } },
		equipment = { [1] = "|cffa335ee|Hitem:16921::::|h[Auréole]|h|r" },
		quests = { { questID = 4001, title = "Le cœur du Magma", tagID = 62, isHeader = false } },
	}
	for k, v in pairs(extra or {}) do
		options[k] = v
	end
	return options
end

-- Un autre joueur connecté (tous modules actifs) qui sollicite chaque module.
local function Nayra()
	local nayra = T.Game({ first = "Nayra", last = "Solen", classFile = "PRIEST", settings = T.Settings(),
		db = { characters = { ["Nayra Solen"] = { guild = T.GUILD } }, importedByGuild = {} }, serverData = T.RichServerData(),
		quests = { { questID = 4002, title = "Onyxia", tagID = 62, isHeader = false } } }):Login()
	nayra:RunTimers()
	nayra.sent = {}
	local GP = nayra.GP
	GP.Craft.CreateOrder(nil, "Flacon des Titans", 2, nil, 17635)
	GP.Events.Create({ title = "Zul'Gurub", kind = "raid", startsAt = T.NOW + 3 * 86400, tank = 1 })
	GP.Events.Respond(GP.Events.Find("12"), "yes", "healer", nil, "")
	GP.RaidNight.SetReserve(GP.Events.Find("12"), "low", 18814, "Pierre")
	GP.Quests.Scan()
	GP.Quests.Announce()
	return nayra
end

-- Activité normale d'un poste : connexion, relevés, minuteries, raid, butin, onglets.
local function Exercise(game)
	local GP, M = game.GP, game.GP.Modules
	-- Changements locaux dans chaque module actif (ce qui part à l'export et aux autres joueurs).
	if M.IsActive("craft") then
		GP.Craft.CreateOrder(nil, "Lingot", 3, nil, nil)
	end
	if M.IsActive("events") then
		GP.Events.Create({ title = "Onyxia", kind = "raid", startsAt = T.NOW + 2 * 86400, tank = 1 })
		GP.Events.Respond(GP.Events.Find("12"), "yes", "tank", nil, "")
	end
	if M.IsActive("raid") then
		GP.RaidNight.SetReserve(GP.Events.Find("12"), "high", 19019, "Serre-tonnerre")
	end
	game:RunTimers()
	game:Tick()
	game:Fire("SKILL_LINES_CHANGED")
	game:Fire("PLAYER_EQUIPMENT_CHANGED")
	game:Fire("QUEST_LOG_UPDATE")
	game:Fire("BAG_UPDATE_DELAYED")
	game.raid = { instance = "Onyxia", members = {
		{ "Thorgal", 2, "WARRIOR", true }, { "Nayra-Solen", 0, "PRIEST", true }, { "Elaria-Brume", 0, "MAGE", true },
		{ "Kael-Fourbe", 0, "ROGUE", true }, { "Mira-Vent", 0, "DRUID", true } } }
	game:Fire("GROUP_ROSTER_UPDATE")
	game:Fire("CHAT_MSG_LOOT", "Nayra-Solen reçoit le butin : |cffa335ee|Hitem:19019::::|h[Serre-tonnerre]|h|r.")
	game:Tick()
	game.raid = nil
	game:RunTimers()
	-- Fusion des données serveur et export, quel que soit l'état des modules.
	local server = game.GP.GetServerData()
	T.check(server and server.guildName == T.GUILD, "données serveur de la guilde")
	T.check(#game.GP.GetRoster() >= 2, "roster fusionné serveur + relevés")
	local payload = game.GP.BuildExportPayload()
	T.check(type(payload.characters) == "table" and #payload.characters >= 1, "export : personnages")
	T.check(game.GP.BuildExportString():sub(1, 4) == "GP1!", "chaîne d'export")
	return payload
end

local function Lines(game)
	local lines = {}
	for i, line in ipairs(T.ModuleLines(game)) do
		lines[T.MODULES[i]] = line
	end
	return lines
end

local function CheckOthersWork(game, broken, payload, sent)
	local GP = game.GP
	local down = { [broken] = true }
	if broken == "events" then
		down.raid = true
	end
	-- Les modules restants restent fonctionnels.
	if not down.events then
		T.check(GP.Events.Find("12") ~= nil, "Événements : l'événement serveur est lu")
		T.check(payload.events ~= nil, "Événements : export présent")
	end
	if not down.raid then
		local event = GP.Events.Find("12")
		T.check(event.reserves ~= nil, "Soirée de raid : réservations rattachées")
		T.check(payload.reserves ~= nil, "Soirée de raid : export présent")
	end
	if not down.craft then
		T.check(#GP.Craft.GetOrders() >= 1, "Artisanat : commandes lues")
		T.check(GP.Modules.Service("craft.order") ~= nil, "Artisanat : service Commander")
	end
	if not down.attendance then
		T.check(#GP.Attendance.GetRaids() >= 1, "Présence : raids lus")
		T.check(payload.raids ~= nil, "Présence : export présent")
		T.check(#T.Tickers(game, 60) == 1, "Présence : minuterie active")
	end
	if not down.quests then
		T.check(next(GP.Quests.GetAll()) ~= nil, "Quêtes : quêtes lues")
	end
	local char = game.env.GuildkeepDB.characters["Thorgal Marteau"]
	if not down.professions then
		T.check(char.professions and char.professions.Alchimie, "Métiers : relevé fait")
	end
	if not down.members then
		T.check(char.equipment ~= nil, "Membres : équipement relevé")
	end
	-- Le module en panne n'exporte rien et n'envoie rien.
	for id in pairs(down) do
		for _, key in ipairs(EXPORT_KEYS[id] or {}) do
			T.check(payload[key] == nil, ("export sans la clé %s du module %s"):format(key, id))
		end
		for kind in pairs(PEER_KINDS[id] or {}) do
			T.check(not sent[kind], ("aucun message %s du module %s"):format(kind, id))
		end
	end
	if down.craft then
		T.check(not sent["prefix:GPROF"], "aucun message GPROF")
	end
	if down.professions then
		T.check(char.professions == nil, "Métiers en panne : aucun relevé")
	end
	if down.members then
		T.check(char.equipment == nil, "Membres en panne : aucun relevé")
	end
	if down.quests then
		T.check(char.quests == nil, "Quêtes en panne : aucun relevé")
	end
end

-- 1. Matrice d'échec ------------------------------------------------------------------------

for _, id in ipairs(T.MODULES) do
	for _, part in ipairs({ "data", "ui" }) do
		for _, when in ipairs({ "après", "avant" }) do
			test(("échec de %s/%s %s branchement : message, retrait complet, les autres fonctionnent"):format(id, part, when), function()
				local game = T.Game(Options())
				local parts = Parts(game)
				local real = parts[id][part]
				T.check(real ~= nil, "partie déclarée")
				parts[id][part] = function(ctx)
					if when == "après" then
						real(ctx)
					end
					error("panne simulée " .. id .. "/" .. part)
				end
				game:Login()
				local lines = Lines(game)
				T.equal(lines[id], ("module %s : échec du chargement, contacte un administrateur"):format(T.LABELS[id]), "message d'échec")
				for _, other in ipairs(T.MODULES) do
					if other ~= id then
						local expected = ("module %s chargé"):format(T.LABELS[other])
						if other == "raid" and id == "events" then
							expected = "module Soirée de raid désactivé : il nécessite le module Événements"
						end
						T.equal(lines[other], expected, "message du module " .. other)
					end
				end
				T.check(T.find(game.errors, "panne simulée " .. id .. "/" .. part), "erreur d'origine signalée")
				T.equal(#game.errors, 1, "une seule erreur : " .. table.concat(game.errors, " | "))
				game.errors = {}
				-- Trafic d'un autre joueur, puis activité normale.
				local nayra = Nayra()
				nayra:DeliverTo(game)
				game.sent = {}
				-- Un autre joueur se connecte et demande les nouveautés.
				game:Fire("CHAT_MSG_ADDON", "GKEEPEV", "1\t1\t1\t{[\"t\"]=\"req\"}", "GUILD", "Elaria-Brume")
				local payload = Exercise(game)
				local sent = SentKinds(game)
				CheckOthersWork(game, id, payload, sent)
				local db = game.env.GuildkeepDB
				if id == "craft" then
					T.check(db.orderPeer == nil or next(db.orderPeer) == nil, "commande reçue ignorée")
				end
				if id == "quests" then
					T.check(db.questPeer == nil, "quêtes reçues ignorées")
				end
				-- Onglets : celui du module en panne est absent, les autres s'affichent sans erreur.
				local labels = T.ShowEveryTab(game)
				for mod, label in pairs(T.TABS) do
					local expectAbsent = mod == id
					T.equal(T.contains(labels, label), not expectAbsent, "onglet " .. label)
				end
				if id == "raid" or id == "events" then
					for _, object in ipairs(game.objects) do
						local section = rawget(object, "section")
						T.check(section == nil or section == "roster", "pas de section de la soirée")
					end
				end
				T.check(game.GP.IsMainFrameShown(), "la fenêtre s'ouvre")
				-- Commandes /gkeep du module en panne : simple ouverture de la fenêtre.
				game:Slash("events")
				game:Slash("quetes")
				game:Slash("presence")
				game:Slash("modules")
				T.CheckClean(game)
			end)
		end
	end
end

-- 2. Chaque module désactivé seul : export, fusion, trafic, pas d'écoute ---------------------

local MODULE_EVENTS = {
	professions = { "SKILL_LINES_CHANGED", "TRADE_SKILL_SHOW", "TRADE_SKILL_LIST_UPDATE" },
	members = { "PLAYER_EQUIPMENT_CHANGED" },
	craft = { "BAG_UPDATE_DELAYED" },
	attendance = { "CHAT_MSG_LOOT", "GROUP_ROSTER_UPDATE", "ZONE_CHANGED_NEW_AREA" },
	quests = { "QUEST_LOG_UPDATE", "QUEST_TURNED_IN", "QUEST_ACCEPTED", "QUEST_REMOVED" },
}

local function Registered(game, event)
	local count = 0
	for _, object in ipairs(game.objects) do
		local events = rawget(object, "_events")
		if events and events[event] then
			count = count + 1
		end
	end
	return count
end

for _, id in ipairs(T.MODULES) do
	test(("module %s désactivé : pas d'écoute, rien envoyé, export et fusion serveur intacts"):format(id), function()
		local game = T.Started(Options({ settings = T.Settings({ id }) }))
		for _, event in ipairs(MODULE_EVENTS[id] or {}) do
			T.equal(Registered(game, event), 0, "aucun cadre n'écoute " .. event)
		end
		if id == "craft" then
			T.check(not game.prefixes.GPROF, "préfixe GPROF non enregistré")
		end
		Nayra():DeliverTo(game)
		game.sent = {}
		game:Fire("CHAT_MSG_ADDON", "GKEEPEV", "1\t1\t1\t{[\"t\"]=\"req\"}", "GUILD", "Elaria-Brume")
		local payload = Exercise(game)
		CheckOthersWork(game, id, payload, SentKinds(game))
		-- Import manuel (fusion d'une chaîne serveur) avec le module désactivé.
		local section = T.RichServerData().guilds[1]
		section.version = 99
		local text = game.GP.EncodeTable(section)
		T.check(game.GP.ImportString(text), "import manuel accepté")
		T.equal(game.GP.GetServerData().version, 99, "la chaîne importée est prise")
		T.ShowEveryTab(game)
		T.CheckClean(game)
	end)
end

-- 3. Panne d'exécution de la Soirée de raid, vue depuis les Événements ------------------------

test("Soirée de raid qui plante à l'exécution (finish) : les Événements restent utilisables", function()
	local game = T.Game(Options())
	-- Bogue dans le code de la soirée : la fonction de fin de fusion plante.
	game.GP.RaidNight.FinishNight = function()
		error("bogue simulé de la soirée de raid")
	end
	game:Login()
	local ok, err = pcall(game.GP.Events.GetEvents)
	T.check(ok, "E.GetEvents survit à un bogue de la soirée de raid (" .. tostring(err) .. ")")
	T.check(ok and #err > 0, "les événements du serveur restent lisibles")
	-- La panne est contenue : la soirée est mise en échec (une seule erreur signalée),
	-- les Événements restent chargés.
	T.equal(game.GP.Modules.Status("raid").state, "failed", "la soirée en panne est mise en échec")
	T.equal(game.GP.Modules.Status("events").state, "loaded", "les Événements restent chargés")
	local reported = 0
	for _, e in ipairs(game.errors) do
		if e:find("bogue simulé", 1, true) then
			reported = reported + 1
		end
	end
	T.equal(reported, 1, "l'erreur de la soirée est signalée une seule fois")
	T.check(T.find(game.prints, "module Soirée de raid : erreur, désactivé jusqu'au prochain /reload, contacte un administrateur"),
		"le joueur est prévenu de la panne : " .. table.concat(game.prints, " | "))
	game.errors = {}
	-- Un changement local d'événement part toujours à l'export.
	game.GP.Events.Create({ title = "Onyxia", kind = "raid", startsAt = T.NOW + 2 * 86400, tank = 1 })
	local payload
	ok, err = pcall(function()
		payload = game.GP.BuildExportPayload()
	end)
	T.check(ok and payload.events ~= nil, "l'export des événements survit (" .. tostring(err) .. ")")
	T.check(payload.reserves == nil, "la soirée en échec n'exporte plus rien")
	T.ShowEveryTab(game)
	T.CheckClean(game)
end)

test("ligne de réservation inattendue venue du serveur : les Événements restent utilisables", function()
	-- Données réalistes : une priorité inconnue de ce client (ajoutée côté serveur plus tard)
	-- sur un objet réservé par deux joueurs, et une ligne sans personnage.
	local function Data()
		local data = T.RichServerData()
		local event = data.guilds[1].events[1]
		event.reserves = {
			{ character = "Nayra Solen", priority = "bis", itemId = 19019, at = 70 },
			{ character = "Thorgal Marteau", priority = "high", itemId = 19019, at = 70 },
		}
		return data
	end
	-- Avec la Soirée de raid désactivée, les mêmes données passent : la panne vient de la soirée.
	local without = T.Started(Options({ settings = T.Settings({ "raid" }), serverData = Data() }))
	T.check(pcall(without.GP.Events.GetEvents), "sans la soirée de raid, les événements se lisent")
	local game = T.Started(Options({ serverData = Data() }))
	local ok, err = pcall(game.GP.Events.GetEvents)
	T.check(ok, "une réservation inattendue (module Soirée de raid) casse la lecture des événements : " .. tostring(err))
	local data = Data()
	data.guilds[1].events[1].reserves = { { priority = "high", itemId = 19019, at = 70 } }
	game = T.Started(Options({ serverData = data }))
	ok, err = pcall(game.GP.Events.GetEvents)
	T.check(ok, "une réservation sans personnage (rowKey de la soirée) casse la lecture des événements : " .. tostring(err))
end)

test("priorité ou type inconnu venu du serveur : la Soirée de raid et les Quêtes restent chargées", function()
	-- Priorités ajoutées côté serveur plus tard (« bis », « alt »), lignes mal formées,
	-- quête d'un type inconnu et correction de présence d'un statut inconnu.
	local data = T.RichServerData()
	local section = data.guilds[1]
	section.events[1].reserves = {
		{ character = "Nayra Solen", priority = "bis", itemId = 19019, itemName = "Serre-tonnerre", at = 70 },
		{ character = "Elaria Brume", priority = "alt", itemId = 19019, at = 70 },
		{ character = "Thorgal Marteau", priority = "high", itemId = 19019, at = 70 },
		{ character = "Thorgal Marteau", priority = "low", itemId = "19020", at = 70 },
	}
	section.events[1].hardReserves = { { itemId = "x", active = true, at = 70 } }
	table.insert(section.characters[1].quests, { id = 4002, title = "Monde", kind = "world", location = "Silithus" })
	table.insert(section.characters[1].quests, { id = "4003", title = "Sans numéro", kind = "raid" })
	section.raids[1].corrections = { { character = "Nayra Solen", status = "late", note = "retard", at = 1 } }
	local game = T.Started(Options({ serverData = data }))
	local ok, events = pcall(game.GP.Events.GetEvents)
	T.check(ok, "les événements se lisent (" .. tostring(events) .. ")")
	T.equal(game.GP.Modules.Status("raid").state, "loaded", "la Soirée de raid reste chargée")
	local event = game.GP.Events.Find("12")
	local R = game.GP.RaidNight
	local order = {}
	for _, r in ipairs(event.reserves) do
		order[#order + 1] = r.priority
	end
	T.equal(table.concat(order, ","), "high,alt,bis", "priorités inconnues rangées en dernier, ligne mal formée ignorée")
	T.equal(#event.hardReserves, 0, "réservation d'office sans numéro d'objet ignorée")
	T.equal(#R.ReserveGroups(event)[1].rows, 3, "les trois réservations sur l'objet comptent")
	local lines = table.concat(R.NightExportLines(event), "\n")
	T.check(lines:find("Nayra Solen (bis)", 1, true) and lines:find("Thorgal Marteau (haute)", 1, true),
		"lignes à copier : libellé connu, priorité inconnue affichée telle quelle")
	T.check(#R.Checklist(event) > 0, "liste de contrôle calculée")
	-- Le joueur réserve encore normalement à côté de la ligne inconnue.
	game.now = game.now + 5
	T.check(R.SetReserve(game.GP.Events.Find("12"), "normal", 19020, "Autre objet"), "réservation possible")
	-- Quêtes : seules les quêtes valides d'un type connu sont retenues.
	local all = game.GP.Quests.GetAll()
	T.check(all[4001] and not all[4002] and not all["4003"], "quête d'un type inconnu ou sans numéro ignorée")
	T.equal(game.GP.Modules.Status("quests").state, "loaded", "les Quêtes restent chargées")
	T.ShowEveryTab(game)
	for _, id in ipairs(T.MODULES) do
		T.equal(game.GP.Modules.Status(id).state, "loaded", "module toujours chargé après affichage : " .. id)
	end
	T.CheckClean(game)
end)

test("Soirée de raid qui plante à l'affichage (titre de section, lignes à copier) : l'onglet Événements reste utilisable", function()
	local game = T.Started(Options())
	local extensions
	for i = 1, 20 do
		local name, value = debug.getupvalue(game.GP.EventsUI.Extend, i)
		if name == "extensions" then
			extensions = value
			break
		end
	end
	T.check(extensions and #extensions == 1, "extension de la soirée trouvée")
	for _, section in ipairs(extensions[1].sections) do
		if section.title then
			section.title = function() error("titre impossible") end
		end
	end
	extensions[1].exportLines = function() error("lignes impossibles") end
	game.GP.ToggleMainFrame()
	game.GP.ShowTab("tabEvents")
	-- Sélection de l'événement pour afficher le détail.
	for _, object in ipairs(game.objects) do
		if rawget(object, "data") and type(object.data) == "table" and object.data.key == "12" and object:GetScript("OnClick") then
			object:Click()
			break
		end
	end
	local errors = {}
	for _, e in ipairs(game.errors) do
		errors[#errors + 1] = e
	end
	-- La panne est contenue : signalée une seule fois, la soirée est mise en échec
	-- (ses sections disparaissent) et l'onglet Événements continue de se rafraîchir.
	local reported = 0
	for _, e in ipairs(errors) do
		if e:find("titre impossible", 1, true) then
			reported = reported + 1
		end
	end
	T.equal(reported, 1, "panne de titre signalée une seule fois : " .. table.concat(errors, " | "))
	T.equal(game.GP.Modules.Status("raid").state, "failed", "la soirée en panne est mise en échec")
	T.equal(game.GP.Modules.Status("events").state, "loaded", "les Événements restent chargés")
	game.errors = {}
	local ok, err = pcall(game.GP.RefreshUI)
	T.check(ok, "l'onglet Événements se rafraîchit encore (" .. tostring(err) .. ")")
	-- Bouton « Copier » : le roster se copie, sans les lignes de la soirée en panne.
	local copied = false
	for _, object in ipairs(game.objects) do
		local text = object.GetText and object:GetText()
		if type(text) == "string" and text:find("^Copier l") and object:GetScript("OnClick") then
			ok, err = pcall(object.Click, object)
			T.check(ok, "le bouton « Copier » fonctionne (" .. tostring(err) .. ")")
			copied = true
		end
	end
	T.check(copied, "bouton « Copier » trouvé")
	T.check(#game.errors == 0, "aucune nouvelle erreur : " .. table.concat(game.errors, " | "))
end)

-- 4. Panneau d'options : une case par module, état persisté ----------------------------------

test("panneau d'options : une case par module, choix persisté au /reload suivant", function()
	local game = T.Started(Options({ settings = nil }))
	local category = game.settingsCategories[1]
	T.check(category, "catégorie enregistrée")
	T.equal(#category.checkboxes, #T.MODULES, "une case par module")
	local byKey = {}
	for _, checkbox in ipairs(category.checkboxes) do
		byKey[checkbox.setting.key] = checkbox.setting
	end
	for _, id in ipairs(T.MODULES) do
		T.check(byKey[id], "case du module " .. id)
		T.equal(byKey[id]:GetValue(), true, "activé par défaut : " .. id)
	end
	T.check(byKey.quests.tbl == game.env.GuildkeepSettings.modules, "case reliée à la SavedVariable GuildkeepSettings.modules")
	byKey.quests:SetValue(false)
	byKey.craft:SetValue(false)
	game:RunTimers()
	-- La session courante n'est pas touchée avant /reload.
	T.check(game.GP.Modules.IsActive("quests"), "pas d'effet avant /reload")
	-- /reload : nouvelle session avec la SavedVariable écrite.
	local saved = game.env.GuildkeepSettings
	local reloaded = T.Started(Options({ settings = saved, db = game.env.GuildkeepDB }))
	local lines = Lines(reloaded)
	T.equal(lines.quests, "module Quêtes désactivé", "Quêtes désactivé après /reload")
	T.equal(lines.craft, "module Artisanat désactivé", "Artisanat désactivé après /reload")
	T.equal(lines.events, "module Événements chargé", "les autres restent")
	-- /gkeep modules : réactivation persistée.
	reloaded:Slash("modules quests on")
	T.equal(reloaded.env.GuildkeepSettings.modules.quests, true, "/gkeep modules quests on persisté")
	reloaded:Slash("modules Artisanat on")
	T.equal(reloaded.env.GuildkeepSettings.modules.craft, true, "/gkeep modules par libellé")
	-- Toc : la SavedVariable est déclarée.
	local toc = io.open(T.addonDir .. "/Guildkeep.toc"):read("a")
	T.check(toc:find("## SavedVariables: GuildkeepDB, GuildkeepSettings", 1, true), "GuildkeepSettings déclarée dans le .toc")
	T.CheckClean(reloaded)
end)

test("préférences corrompues : valeurs non booléennes ramenées à activé, sans erreur", function()
	local game = T.Started(Options({ settings = { modules = { craft = "non", events = 0, quests = false } } }))
	local lines = Lines(game)
	T.equal(lines.craft, "module Artisanat chargé", "valeur non booléenne = activé")
	T.equal(lines.quests, "module Quêtes désactivé", "false respecté")
	T.CheckClean(game)
end)

test("ancienne sauvegarde sans tables de module : chaque module crée les siennes, le compagnon lit les mêmes clés", function()
	local game = T.Started(Options({ db = { characters = {} } }))
	local db = game.env.GuildkeepDB
	for _, key in ipairs({ "orders", "orderActions", "orderOffers", "eventEdits", "eventSignups", "eventPlacements", "eventReserves",
		"eventHardReserves", "eventDuties", "raidSessions", "raidLoot", "attendanceCorrections", "lootCorrections" }) do
		T.check(type(db[key]) == "table", "table " .. key .. " créée avec tous les modules actifs")
	end
	T.CheckClean(game)
end)

return tests
