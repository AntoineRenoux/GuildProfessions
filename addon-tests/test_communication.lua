-- Lot 6 : annonces, charte, objectifs (module Communication) et rappels d'événements.

local T = require("helpers")
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

local function Nayra(serverData)
	return T.Started({ first = "Nayra", last = "Solen", classFile = "PRIEST", settings = T.Settings(),
		db = { characters = { ["Nayra Solen"] = { guild = T.GUILD } }, importedByGuild = {} }, serverData = serverData or T.RichServerData() })
end

local function Thorgal(serverData)
	return T.Started({ settings = T.Settings(), officer = true, serverData = serverData or T.RichServerData() })
end

test("un officier publie : l'annonce, la charte et l'objectif s'affichent et partent vers les autres", function()
	local thorgal, nayra = Thorgal(), Nayra()
	local C = thorgal.GP.Communication
	T.check(C.PostAnnouncement("Raid ce soir", "Rendez-vous à 20 h 30", 12), "annonce publiée")
	T.check(C.SaveSection("Butin", "Règles de butin", "Réservations d'abord", 2), "section de charte enregistrée")
	T.check(C.SaveGoal({ title = "Flacons", target = 200, unit = "flacons" }), "objectif créé")
	local data = C.Get()
	T.equal(#data.announcements, 1, "annonce visible tout de suite")
	T.equal(data.handbook[1].key, "butin", "clé de section normalisée")
	T.equal(#data.goals, 1, "objectif visible tout de suite")

	thorgal:DeliverTo(nayra)
	local seen = nayra.GP.Communication.Get()
	T.equal(#seen.announcements, 1, "Nayra reçoit l'annonce")
	T.equal(#seen.handbook, 1, "Nayra reçoit la charte")
	T.equal(#seen.goals, 1, "Nayra reçoit l'objectif")
	T.check(T.find(nayra:PlainPrints(), "annonce de Thorgal Marteau"), "Nayra est prévenue de l'annonce")
	T.equal(nayra.GP.Communication.UnreadCount(), 1, "annonce non lue")
	T.CheckClean(thorgal)
	T.CheckClean(nayra)
end)

test("un membre ne publie pas, et ce qu'il prétend publier est ignoré par les autres", function()
	local nayra, thorgal = Nayra(), Thorgal()
	local C = nayra.GP.Communication
	local ok, err = C.PostAnnouncement("Piraté", "Texte")
	T.check(not ok and err, "publication refusée à un membre")
	-- Message forgé : Nayra (rang 4) se présente comme auteure d'une annonce.
	nayra.GP.Comm.Broadcast({ t = "an", k = "x", e = { title = "Piraté", body = "x", by = "Nayra Solen", at = T.NOW + 5 } })
	nayra:DeliverTo(thorgal)
	T.equal(#thorgal.GP.Communication.Get().announcements, 0, "annonce d'un membre ignorée")
	T.CheckClean(nayra)
	T.CheckClean(thorgal)
end)

test("contributions : visibles aussitôt, exportées une fois, purgées quand le serveur les reprend", function()
	local server = T.RichServerData()
	server.guilds[1].communication = { announcements = {}, templates = {}, handbook = {}, goals = {
		{ id = 5, title = "Flacons", target = 200, progress = 20, closed = false, createdBy = "Thorgal Marteau", updatedAt = 100,
			unit = "flacons", contributions = { { clientId = "c-0", character = "Elaria Brume", amount = 20, at = 90 } } },
	} }
	local nayra = Nayra(server)
	local C = nayra.GP.Communication
	local goal = C.FindGoal("5")
	T.check(C.Contribute(goal, 15, "Titans"), "contribution acceptée")
	T.equal(C.FindGoal("5").progress, 35, "avancement mis à jour aussitôt")
	local ok = C.Contribute(goal, 0)
	T.check(not ok, "quantité nulle refusée")

	local payload = nayra.GP.BuildExportPayload()
	local contribution = payload.communication.contributions[1]
	T.equal(contribution.goalId, 5, "contribution rattachée à l'objectif serveur")
	T.equal(contribution.character, "Nayra Solen", "contribution du personnage connecté")
	T.check(payload.communication.announcements == nil, "pas de liste vide dans l'export")

	-- Le serveur l'a reprise : purge au chargement suivant.
	table.insert(server.guilds[1].communication.goals[1].contributions, { clientId = contribution.clientId, character = "Nayra Solen", amount = 15, at = contribution.at })
	server.guilds[1].communication.goals[1].progress = 35
	nayra.GP.Communication.Prune()
	T.check(next(nayra.env.GuildkeepDB.goalContributions) == nil, "contribution purgée")
	T.equal(C.FindGoal("5").progress, 35, "pas comptée deux fois")
	T.CheckClean(nayra)
end)

test("objectif clos : plus de contribution ; modèle d'annonce rendu", function()
	local thorgal = Thorgal()
	local C = thorgal.GP.Communication
	C.SaveGoal({ title = "Or", target = 1000, unit = "po" })
	local goal = C.Get().goals[1]
	C.SetGoalClosed(goal, true)
	T.check(C.Get().goals[1].closed, "objectif clos")
	T.check(not C.Contribute(C.Get().goals[1], 10), "contribution refusée sur un objectif clos")
	local rendered = C.Render("{event} le {date} — {guild}", { title = "Onyxia", startsAt = T.NOW })
	T.check(rendered:find("Onyxia le ", 1, true) and rendered:find("FORMER HOPE", 1, true), "modèle rendu")
	T.CheckClean(thorgal)
end)

test("l'onglet Guilde s'affiche pour chaque section, et l'annonce affichée est marquée lue", function()
	local thorgal = Thorgal()
	local C = thorgal.GP.Communication
	C.PostAnnouncement("Raid ce soir", "20 h 30")
	C.SaveSection("reglement", "Règlement", "Respect", 0)
	C.SaveGoal({ title = "Flacons", target = 200 })
	thorgal.GP.ShowTab("tabGuild")
	T.equal(C.UnreadCount(), 0, "annonce lue une fois affichée")
	T.check(T.contains(thorgal:TabLabels(), "Guilde"), "onglet Guilde présent")
	T.CheckClean(thorgal)
end)

test("rappel personnel : une seule alerte, à l'approche d'un événement où l'on est inscrit", function()
	local game = Thorgal()
	local E = game.GP.Events
	local key = E.Create({ title = "Onyxia", kind = "raid", startsAt = T.NOW + 20 * 60, tank = 1 })
	E.Respond(E.Find(key), "yes", "tank", nil, nil)
	E.Reminders.Set(true, 30)
	E.Reminders.Check()
	E.Reminders.Check()
	local alerts = 0
	for _, line in ipairs(game:PlainPrints()) do
		if line:find("Onyxia commence dans", 1, true) then
			alerts = alerts + 1
		end
	end
	T.equal(alerts, 1, "une seule alerte")
	T.check(E.Reminders.RemindGuild(E.Find(key)), "rappel aux inscrits par l'organisateur")
	T.equal(game.chat[1].channel, "GUILD", "posté dans le canal de guilde")
	T.check(game.chat[1].text:find("Onyxia", 1, true) and game.chat[1].text:find("Thorgal", 1, true), "titre et inscrits dans le message")
	T.check(game.GP.Modules.Service("events.upcoming") ~= nil, "service des prochains événements offert")
	T.CheckClean(game)
end)

test("Événements désactivé : la Communication fonctionne, sans événement à lier", function()
	local game = T.Started({ settings = T.Settings({ "events" }), officer = true, serverData = T.RichServerData() })
	T.check(game.GP.Modules.Service("events.upcoming") == nil, "service absent")
	T.check(game.GP.Communication.PostAnnouncement("Sortie", "Ce soir"), "annonce publiée sans le module Événements")
	game.GP.ShowTab("tabGuild")
	T.CheckClean(game)
end)

return tests
