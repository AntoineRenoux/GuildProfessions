-- Outils communs aux tests : postes de jeu prêts à l'emploi et vérifications.

local Wow = require("wow")
local T = { Wow = Wow }

T.NOW = 1795000000
T.GUILD = "FORMER HOPE"
T.MODULES = { "professions", "members", "craft", "events", "raid", "attendance", "quests", "communication", "profiles" }
T.LABELS = {
	professions = "Métiers", members = "Membres", craft = "Artisanat", events = "Événements",
	raid = "Soirée de raid", attendance = "Présence et butin", quests = "Quêtes", communication = "Communication",
	profiles = "Profils et accueil",
}
T.TABS = { professions = "Métiers", members = "Membres", craft = "Commandes", events = "Événements",
	attendance = "Présence", quests = "Quêtes", communication = "Guilde", profiles = "Profils" }

function T.check(condition, message)
	if not condition then
		error("vérification échouée : " .. message, 2)
	end
end

function T.equal(actual, expected, message)
	if actual ~= expected then
		error(("vérification échouée : %s (attendu %s, obtenu %s)"):format(message, tostring(expected), tostring(actual)), 2)
	end
end

function T.contains(list, value)
	for _, v in ipairs(list) do
		if v == value then
			return true
		end
	end
	return false
end

function T.find(list, pattern)
	for _, v in ipairs(list) do
		if v:find(pattern, 1, true) then
			return v
		end
	end
end

-- Préférences : tous les modules activés sauf ceux listés.
function T.Settings(disabled)
	local modules = {}
	for _, id in ipairs(T.MODULES) do
		modules[id] = true
	end
	for _, id in ipairs(disabled or {}) do
		modules[id] = false
	end
	return { modules = modules }
end

function T.ServerData(extra)
	local section = { version = 1, guildName = T.GUILD, characters = {}, orders = {}, events = {} }
	for k, v in pairs(extra or {}) do
		section[k] = v
	end
	return { guilds = { section } }
end

-- Données de guilde variées, pour que chaque onglet ait de quoi s'afficher.
function T.RichServerData()
	local now = T.NOW
	return T.ServerData({
		characters = {
			{ name = "Nayra Solen", classFile = "PRIEST", level = 60, equipment = { ["1"] = "|cffa335ee|Hitem:16921::::|h[Auréole]|h|r" },
				professions = { { name = "Alchimie", level = 300, max = 300, recipes = { 17635 } } },
				quests = { { id = 4001, title = "Le cœur du Magma", kind = "raid", location = "Cœur du Magma", level = 60 } } },
			{ name = "Elaria Brume", classFile = "MAGE", level = 58, professions = { { name = "Couture", level = 250, max = 300 } } },
		},
		catalog = { ["17636"] = { "Flacon de sagesse", "Alchimie" } },
		orders = { { id = 7, requester = "Nayra Solen", item = "Flacon des Titans", qty = 1, status = "open",
			createdUtc = "2026-11-23 10:00", expiresUtc = "2026-11-28 10:00", recipeId = 17635, offers = {} } },
		events = { {
			id = 12, title = "Cœur du Magma", kind = "raid", startsAt = now + 86400, organizer = "Thorgal Marteau",
			tank = 1, healer = 1, melee = 0, ranged = 0, cancelled = false, locked = false, updatedAt = 100,
			signups = {
				{ character = "Thorgal Marteau", classFile = "WARRIOR", role = "tank", response = "yes", responseAt = 50, placement = "confirmed", placementAt = 60 },
				{ character = "Nayra Solen", classFile = "PRIEST", role = "healer", response = "yes", responseAt = 50, placement = "pending", placementAt = 0 },
			},
			reserves = { { character = "Nayra Solen", priority = "high", itemId = 19019, itemName = "Serre-tonnerre", at = 70 } },
			hardReserves = {}, duties = { { duty = "lead", character = "Thorgal Marteau", at = 70 } },
		} },
		raids = { {
			id = 4, key = "2026-11-20|Cœur du Magma|Thorgal Marteau", instance = "Cœur du Magma", day = "2026-11-20",
			startedAt = now - 3 * 86400, endedAt = now - 3 * 86400 + 7200,
			participants = { { character = "Nayra Solen", minutes = 120, classFile = "PRIEST" } },
			loot = { { id = 1, character = "Nayra Solen", itemId = 19019, itemName = "Serre-tonnerre", ordinal = 1, quality = 5, at = 1, voided = false, voidAt = 0 } },
			corrections = {},
		} },
	})
end

-- Poste prêt à charger : Thorgal Marteau, officier de FORMER HOPE, par défaut.
function T.Game(options)
	options = options or {}
	options.addonDir = options.addonDir or T.addonDir
	options.now = options.now or T.NOW
	if options.serverData == nil then
		options.serverData = T.ServerData()
	end
	options.roster = options.roster or {
		{ "Thorgal-Marteau", 1, true }, { "Nayra-Solen", 4, true }, { "Elaria-Brume", 4, true },
		{ "Kael-Fourbe", 4, true }, { "Mira-Vent", 4, true },
	}
	options.spells = options.spells or { [17635] = "Flacon des Titans", [17636] = "Flacon de sagesse" }
	return Wow.New(options):Load()
end

-- Poste chargé et connecté.
function T.Started(options)
	return T.Game(options):Login()
end

-- Aucune API WoW manquante dans l'imitation, aucun fichier en erreur.
function T.CheckClean(game, allowErrors)
	local unknown = {}
	for name in pairs(game.unknown) do
		unknown[#unknown + 1] = name
	end
	table.sort(unknown)
	T.check(#unknown == 0, "API WoW non imitée lue par l'addon : " .. table.concat(unknown, ", "))
	T.check(#game.fileErrors == 0, "fichier en erreur au chargement : " .. table.concat(game.fileErrors, " | "))
	if not allowErrors then
		T.check(#game.errors == 0, "erreur Lua signalée : " .. table.concat(game.errors, " | "))
	end
end

-- Ouvre la fenêtre et affiche chaque onglet tour à tour (construction et rafraîchissement).
-- Minuteries actives d'un intervalle donné (présence : 60 s, rappels d'événements : 30 s).
function T.Tickers(game, seconds)
	local list = {}
	for _, ticker in ipairs(game.tickers) do
		if ticker._seconds == seconds and not ticker._cancelled then
			list[#list + 1] = ticker
		end
	end
	return list
end

function T.ShowEveryTab(game)
	if not game.GP.IsMainFrameShown() then
		game.GP.ToggleMainFrame()
	end
	for _, key in ipairs({ "tabProfessions", "tabMembers", "tabOrders", "tabEvents", "tabHistory", "tabQuests", "tabGuild", "tabProfiles" }) do
		game.GP.ShowTab(key)
		-- Bascules de l'onglet : sections du détail d'un événement, modes d'affichage.
		for _, field in ipairs({ "section", "mode" }) do
			for _, object in ipairs(game.objects) do
				if rawget(object, field) and object:GetScript("OnClick") and object:IsShown() then
					object:Click()
				end
			end
		end
	end
	return game:TabLabels()
end

-- Lignes « Guildkeep — module … » affichées à la connexion.
function T.ModuleLines(game)
	local lines = {}
	for _, line in ipairs(game:PlainPrints()) do
		if line:find("^Guildkeep — module ") then
			lines[#lines + 1] = line:gsub("^Guildkeep — ", "")
		end
	end
	return lines
end

return T
