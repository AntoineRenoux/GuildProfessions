-- Un module qui plante : signalé à la connexion, retiré proprement, sans
-- empêcher les autres modules de se charger ni la fenêtre de s'ouvrir.

local T = require("helpers")
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

local function Lines(game)
	local lines = {}
	for i, line in ipairs(T.ModuleLines(game)) do
		lines[T.MODULES[i]] = line
	end
	return lines
end

test("un module qui plante au chargement : message d'échec, les autres se chargent", function()
	local game = T.Game({ settings = T.Settings(), serverData = T.RichServerData() })
	-- La partie interface de l'Artisanat plante après avoir branché son onglet et son service.
	game.GP.Modules.Define("craft", "ui", function(ctx)
		ctx:Provide("craft.order", function() end)
		ctx:AddTab({ label = "Commandes", key = "tabOrders", order = 30, build = function() end, refresh = function() end })
		error("panne simulée de l'interface Artisanat")
	end)
	game:Login()
	local lines = Lines(game)
	T.equal(lines.craft, "module Artisanat : échec du chargement, contacte un administrateur", "message d'échec")
	for _, id in ipairs(T.MODULES) do
		if id ~= "craft" then
			T.equal(lines[id], ("module %s chargé"):format(T.LABELS[id]), "les autres modules se chargent : " .. id)
		end
	end
	T.check(T.find(game.errors, "panne simulée de l'interface Artisanat"), "l'erreur est signalée")
	T.equal(game.GP.Modules.Status("craft").state, "failed", "état « échec »")
	-- Tout ce que le module avait branché est retiré, partie données comprise.
	T.check(game.GP.Modules.Service("craft.order") == nil, "service retiré")
	T.check(game.GP.BuildExportPayload().orders == nil, "export de l'Artisanat retiré")
	local nayra = T.Started({ first = "Nayra", last = "Solen", settings = T.Settings() })
	nayra.GP.Craft.CreateOrder(nil, "Flacon des Titans", 1, nil, 17635)
	nayra:DeliverTo(game)
	T.check(next(game.env.GuildkeepDB.orderPeer) == nil, "messages de commande ignorés")
	game.errors = {}
	local labels = T.ShowEveryTab(game)
	T.equal(table.concat(labels, ","), "Métiers,Membres,Événements,Présence,Quêtes", "onglet Commandes retiré, les autres restent")
	T.check(game.GP.IsMainFrameShown(), "la fenêtre s'ouvre")
	T.CheckClean(game)
end)

test("données corrompues : Événements échoue, Soirée de raid suit, le reste se charge", function()
	local game = T.Started({ settings = T.Settings(), db = { characters = {}, importedByGuild = {}, eventEdits = "corrompu" } })
	local lines = Lines(game)
	T.equal(lines.events, "module Événements : échec du chargement, contacte un administrateur", "Événements en échec")
	T.equal(lines.raid, "module Soirée de raid désactivé : il nécessite le module Événements", "Soirée de raid désactivée")
	for _, id in ipairs({ "professions", "members", "craft", "attendance", "quests" }) do
		T.equal(lines[id], ("module %s chargé"):format(T.LABELS[id]), "chargé : " .. id)
	end
	T.equal(#game.errors, 1, "une erreur signalée")
	game.errors = {}
	game:RunTimers()
	local labels = T.ShowEveryTab(game)
	T.equal(table.concat(labels, ","), "Métiers,Membres,Commandes,Présence,Quêtes", "pas d'onglet Événements")
	T.CheckClean(game)
end)

test("fichier d'un module absent : le module échoue, les autres se chargent", function()
	local game = T.Started({ settings = T.Settings(), skip = { "Quests.lua" } })
	local lines = Lines(game)
	T.equal(lines.quests, "module Quêtes : échec du chargement, contacte un administrateur", "Quêtes en échec")
	T.check(game.GP.Modules.Status("quests").reason:find("data", 1, true), "partie manquante indiquée")
	for _, id in ipairs({ "professions", "members", "craft", "events", "raid", "attendance" }) do
		T.equal(lines[id], ("module %s chargé"):format(T.LABELS[id]), "chargé : " .. id)
	end
	local labels = T.ShowEveryTab(game)
	T.check(not T.contains(labels, "Quêtes"), "pas d'onglet Quêtes")
	T.CheckClean(game)
end)

test("Soirée de raid qui plante : les Événements restent complets", function()
	local game = T.Game({ settings = T.Settings(), serverData = T.RichServerData() })
	local GP = game.GP
	game.GP.Modules.Define("raid", "ui", function()
		error("panne simulée de la soirée de raid")
	end)
	game:Login()
	local lines = Lines(game)
	T.equal(lines.raid, "module Soirée de raid : échec du chargement, contacte un administrateur", "Soirée de raid en échec")
	T.equal(lines.events, "module Événements chargé", "Événements chargés")
	local event = GP.Events.Find("12")
	T.check(event and event.reserves == nil, "les lignes de la soirée ne sont plus rattachées aux événements")
	T.check(GP.BuildExportPayload().reserves == nil, "export de la soirée retiré")
	game.errors = {}
	T.ShowEveryTab(game)
	for _, object in ipairs(game.objects) do
		local section = rawget(object, "section")
		T.check(section == nil or section == "roster", "pas de section de la soirée")
	end
	T.CheckClean(game)
end)

test("un onglet qui plante à l'affichage : la fenêtre et les autres onglets restent utilisables", function()
	local game = T.Game({ settings = T.Settings(), serverData = T.RichServerData() })
	game.GP.Modules.Define("quests", "ui", function(ctx)
		ctx:AddTab({
			label = "Quêtes", key = "tabQuests", order = 70,
			build = function() error("construction impossible") end,
			refresh = function() error("affichage impossible") end,
			badge = function() error("compteur impossible") end,
		})
	end)
	game:Login()
	T.equal(Lines(game).quests, "module Quêtes chargé", "le module se charge")
	local labels = T.ShowEveryTab(game)
	T.equal(table.concat(labels, ","), "Métiers,Membres,Commandes,Événements,Présence,Quêtes", "tous les onglets")
	T.check(game.GP.IsMainFrameShown(), "fenêtre ouverte")
	T.check(T.find(game.errors, "construction impossible") and T.find(game.errors, "affichage impossible"), "erreurs signalées")
	T.equal(#game.GP.UI.TabBadges(), 0, "compteur en erreur ignoré")
	game.errors = {}
	game.GP.ShowTab("tabEvents")
	T.CheckClean(game)
end)

test("une erreur dans un gestionnaire d'événement ne touche pas les autres modules", function()
	local game = T.Started({ settings = T.Settings(), equipment = {} })
	game.GP.Attendance.OnLoot = function()
		error("butin illisible")
	end
	game.equipment[1] = "|cffa335ee|Hitem:16921::::|h[Auréole]|h|r"
	game:Fire("CHAT_MSG_LOOT", "Nayra-Solen reçoit le butin : |cffa335ee|Hitem:19019::::|h[Serre-tonnerre]|h|r.")
	game:Fire("PLAYER_EQUIPMENT_CHANGED")
	T.check(T.find(game.errors, "butin illisible"), "l'erreur est signalée")
	T.check(game.env.GuildkeepDB.characters["Thorgal Marteau"].equipment["1"], "le module Membres relève toujours")
	T.check(game.GP.Modules.IsActive("attendance"), "le module reste chargé")
	game.errors = {}
	T.CheckClean(game)
end)

-- Export partiel du serveur : un module en panne côté serveur a sa section
-- absente (Data.lua ou chaîne /gkeep export) ; les autres modules s'en servent normalement.

test("Data.lua partiel (Présence en panne côté serveur) : commandes et événements toujours là", function()
	local data = T.RichServerData()
	data.guilds[1].raids = nil
	local game = T.Started({ settings = T.Settings(), serverData = data })
	T.check(game.GP.IsGuildRegistered(), "guilde reconnue")
	local orders = game.GP.Craft.GetOrders()
	T.check(#orders == 1 and orders[1].item == "Flacon des Titans", "la commande du serveur est visible")
	T.check(game.GP.Events.Find("12"), "l'événement du serveur est visible")
	T.equal(#game.GP.Attendance.GetRaids(), 0, "Présence : pas de données, sans erreur")
	T.ShowEveryTab(game)
	T.CheckClean(game)
end)

test("Data.lua partiel (Annuaire en panne côté serveur) : les autres modules restent utilisables", function()
	local data = T.RichServerData()
	data.guilds[1].characters, data.guilds[1].recipeNames = nil, nil
	local game = T.Started({ settings = T.Settings(), serverData = data })
	T.equal(#game.GP.Craft.GetOrders(), 1, "la commande du serveur est visible")
	T.check(game.GP.Events.Find("12"), "l'événement du serveur est visible")
	T.check(#game.GP.Attendance.GetRaids() > 0, "les raids du serveur sont visibles")
	T.ShowEveryTab(game)
	T.CheckClean(game)
end)

test("chaîne /gkeep export partielle (sans personnages) : importée, les autres sections prises", function()
	local game = T.Started({ settings = T.Settings() })
	local section = T.RichServerData().guilds[1]
	section.characters, section.raids, section.version = nil, nil, 50
	local ok, err = game.GP.ImportString(game.GP.EncodeTable(section))
	T.check(ok, "import accepté (" .. tostring(err) .. ")")
	T.equal(game.GP.GetServerData().version, 50, "la chaîne importée est prise")
	T.equal(#game.GP.Craft.GetOrders(), 1, "la commande importée est visible")
	T.ShowEveryTab(game)
	T.CheckClean(game)
end)

return tests
