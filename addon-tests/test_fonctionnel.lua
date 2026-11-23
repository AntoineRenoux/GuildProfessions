-- Tout activé : le comportement de chaque module est inchangé (commandes,
-- événements, soirée de raid, présence, quêtes, synchro entre joueurs).

local T = require("helpers")
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

local function Thorgal(options)
	options = options or {}
	options.settings = options.settings or T.Settings()
	options.db = options.db or { characters = { ["Thorgal Marteau"] = { guild = T.GUILD,
		professions = { Alchimie = { recipes = { 17635 }, catalog = { 17635, 17636 } } } } }, importedByGuild = {} }
	local game = T.Started(options)
	game:RunTimers()
	game.sent = {}
	return game
end

local function Nayra(options)
	options = options or {}
	options.first, options.last, options.classFile = "Nayra", "Solen", "PRIEST"
	options.settings = options.settings or T.Settings()
	options.db = options.db or { characters = { ["Nayra Solen"] = { guild = T.GUILD } }, importedByGuild = {} }
	local game = T.Started(options)
	game:RunTimers()
	game.sent = {}
	return game
end

test("Artisanat : demande ouverte, offre, choix, envoi et réception entre deux joueurs", function()
	local nayra, thorgal = Nayra(), Thorgal({ professions = { { name = "Alchimie", level = 300, max = 300, skillLine = 171 } } })
	local N, Th = nayra.GP.Craft, thorgal.GP.Craft
	local key = N.CreateOrder(nil, "Flacon des Titans", 2, "Composants fournis", 17635)
	local mine = N.GetOrders()[1]
	T.check(mine.status == "open" and mine.crafter == nil and mine.expiresTs == T.NOW + 5 * 86400, "demande ouverte, 5 jours")
	nayra:DeliverTo(thorgal)
	local seen = Th.GetOrders()[1]
	T.check(seen and seen.key == key and Th.CanMake(seen), "Thorgal voit la demande et sait la faire")
	T.check(T.find(thorgal:PlainPrints(), "tu sais le faire"), "Thorgal est prévenu")
	Th.SetOffer(seen, "Ce soir", false)
	thorgal:DeliverTo(nayra)
	local order = N.GetOrders()[1]
	T.check(#order.offers == 1 and order.offers[1].note == "Ce soir", "Nayra voit l'offre")
	T.equal(N.CountOrdersToHandle(), 1, "une offre à départager")
	N.SetOrderStatus(order, "accepted", "Thorgal Marteau")
	nayra:DeliverTo(thorgal)
	order = Th.GetOrders()[1]
	T.check(order.status == "accepted" and order.crafter == "Thorgal Marteau", "Thorgal se voit confier la commande")
	thorgal.now = thorgal.now + 10
	Th.SetOrderStatus(order, "mailed")
	thorgal:DeliverTo(nayra)
	T.equal(N.GetOrders()[1].status, "mailed", "Nayra voit la commande envoyée")
	nayra.now = nayra.now + 20
	N.SetOrderStatus(N.GetOrders()[1], "done")
	T.equal(N.GetOrders()[1].status, "done", "réception confirmée")
	-- Un intrus ne fait pas avancer la commande d'autrui.
	local forged = "UPD\tNayra Solen\tFlacon des Titans\tcancelled\tKael Fourbe\t" .. key .. "\t\t" .. (T.NOW + 99)
	thorgal:Fire("CHAT_MSG_ADDON", "GPROF", forged, "GUILD", "Kael-Fourbe")
	T.check(Th.GetOrders()[1].status ~= "cancelled", "un intrus n'annule pas la commande d'un autre")
	local payload = nayra.GP.BuildExportPayload()
	T.check(#payload.orders == 1 and payload.orders[1].recipeId == 17635 and payload.orderActions[1].status == "done", "export de Nayra")
	payload = thorgal.GP.BuildExportPayload()
	T.check(payload.offers and payload.offers[1].orderClientId == key, "export de l'offre de Thorgal")
	T.check(Th.GetCatalog()[17636].name == "Flacon de sagesse", "catalogue des recettes")
	T.CheckClean(nayra)
	T.CheckClean(thorgal)
end)

test("Artisanat : la fenêtre des recettes propose « Commander » par le service du module", function()
	local game = Thorgal({ serverData = T.RichServerData() })
	local order = game.GP.Modules.Service("craft.order")
	T.check(order ~= nil, "service offert")
	game.GP.OpenRecipesDialog({ name = "Nayra Solen", classFile = "PRIEST" }, { name = "Alchimie", level = 300, max = 300, recipes = { 17635 } })
	order("Nayra Solen", "Flacon des Titans", nil, 17635)
	T.CheckClean(game)
end)

test("Événements : création, réponse, placement, export puis purge", function()
	local game = Thorgal({ serverData = T.RichServerData() })
	local E = game.GP.Events
	local key = E.Create({ title = "Onyxia", kind = "raid", startsAt = T.NOW + 2 * 86400, tank = 1 })
	T.check(E.Find(key) and E.Find(key).localOnly, "l'événement créé est visible avant la synchro")
	T.check(E.Create({ title = " ", startsAt = T.NOW }) == nil, "titre vide refusé")
	E.Respond(E.Find(key), "tentative", "tank", nil, "en retard")
	T.equal(E.Find(key).mine.response, "tentative", "réponse locale")
	E.Place(E.Find("12"), "Nayra Solen", "confirmed")
	T.equal(E.Counts(E.Find("12")).healer.confirmed, 1, "composition")
	local payload = game.GP.BuildExportPayload()
	T.check(#payload.events == 1 and payload.events[1].clientId == key, "export : événement créé")
	T.check(#payload.placements == 1 and payload.placements[1].eventId == 12, "export : placement")
	local section = game.env.Guildkeep_ServerData.guilds[1]
	section.events[1].signups[2].placement, section.events[1].signups[2].placementAt = "confirmed", T.NOW + 100
	section.events[2] = { id = 13, clientId = key, title = "Onyxia", kind = "raid", startsAt = T.NOW + 2 * 86400,
		organizer = "Thorgal Marteau", tank = 1, healer = 0, melee = 0, ranged = 0, cancelled = false, updatedAt = T.NOW + 100,
		signups = { { character = "Thorgal Marteau", role = "tank", response = "tentative", responseAt = T.NOW + 100, placement = "pending", placementAt = 0 } } }
	E.Prune()
	local db = game.env.GuildkeepDB
	T.check(next(db.eventEdits) == nil and next(db.eventSignups) == nil and next(db.eventPlacements) == nil, "purge")
	T.equal(E.Find(key).id, 13, "l'événement est retrouvé sous son id serveur")
	T.CheckClean(game)
end)

test("Événements : synchro directe entre joueurs connectés, et rappel à la connexion", function()
	local thorgal, nayra = Thorgal({ serverData = T.RichServerData() }), Nayra({ serverData = T.RichServerData() })
	local key = thorgal.GP.Events.Create({ title = "Zul'Gurub", kind = "raid", startsAt = T.NOW + 3 * 86400, tank = 1 })
	thorgal:DeliverTo(nayra)
	local event = nayra.GP.Events.Find(key)
	T.check(event and event.title == "Zul'Gurub", "Nayra reçoit l'événement")
	T.check(T.find(nayra:PlainPrints(), "nouvel événement de Thorgal Marteau"), "Nayra est prévenue")
	nayra.GP.Events.Respond(event, "yes", "healer", nil, nil)
	nayra:DeliverTo(thorgal)
	local mine
	for _, row in ipairs(thorgal.GP.Events.Find(key).signups) do
		if row.character == "Nayra Solen" then
			mine = row
		end
	end
	T.check(mine and mine.response == "yes", "Thorgal reçoit la réponse")
	-- Nouvelle connexion de Nayra : rappel de l'événement sans réponse, demande des
	-- nouveautés, Thorgal renvoie ce qu'il a en attente.
	local late = Nayra({ serverData = T.RichServerData() })
	local server = T.RichServerData()
	table.insert(server.guilds[1].events, { id = 14, title = "Onyxia", kind = "raid", startsAt = T.NOW + 86400,
		organizer = "Thorgal Marteau", tank = 1, healer = 0, melee = 0, ranged = 0, cancelled = false, updatedAt = 100, signups = {} })
	local fresh = T.Game({ first = "Nayra", last = "Solen", settings = T.Settings(), serverData = server,
		db = { characters = { ["Nayra Solen"] = { guild = T.GUILD } }, importedByGuild = {} } }):Login()
	fresh:RunTimers()
	T.check(T.find(fresh:PlainPrints(), "attend ta réponse"), "rappel des événements sans réponse")
	fresh:DeliverTo(thorgal)
	thorgal:RunTimers()
	T.check(#thorgal.sent > 0, "Thorgal répond à la demande de nouveautés")
	thorgal:DeliverTo(late)
	T.check(late.GP.Events.Find(key), "les changements en attente sont renvoyés")
	T.CheckClean(thorgal)
	T.CheckClean(nayra)
end)

test("Soirée de raid : réservations, soutien, réservation d'office, verrou, export et purge", function()
	local server = T.RichServerData()
	server.guilds[1].events[1].reserves, server.guilds[1].events[1].duties = {}, {}
	local nayra, thorgal = Nayra({ serverData = server }), Thorgal({ serverData = server })
	local E, R = nayra.GP.Events, nayra.GP.RaidNight
	T.check(R.SetReserve(E.Find("12"), "high", 19019, "Serre-tonnerre"), "un joueur réserve pour lui")
	T.equal(E.Find("12").myReserves.high.itemId, 19019, "réservation visible aussitôt")
	T.check(not R.SetReserve(E.Find("12"), "low", 19019, "Serre-tonnerre"), "même objet à une autre priorité refusé")
	T.check(not R.SetDuty(E.Find("12"), "summoner", "Nayra Solen"), "un joueur n'attribue pas de rôle de soutien")
	nayra:DeliverTo(thorgal)
	E, R = thorgal.GP.Events, thorgal.GP.RaidNight
	thorgal.now = thorgal.now + 5
	T.check(R.SetReserve(E.Find("12"), "normal", 19019, "Serre-tonnerre"), "l'organisateur réserve")
	T.equal(#R.ReserveGroups(E.Find("12"))[1].rows, 2, "réservation de Nayra reçue : conflit")
	T.check(R.SetDuty(E.Find("12"), "summoner", "Nayra Solen"), "rôle de soutien")
	local text = {}
	for _, item in ipairs(R.Checklist(E.Find("12"))) do
		text[#text + 1] = item.text
	end
	text = table.concat(text, "\n")
	T.check(text:find("réservé par 2 joueurs", 1, true) and text:find("n'est plus confirmé", 1, true), "liste de contrôle")
	T.check(R.SetHardReserve(E.Find("12"), 19019, "Serre-tonnerre", nil, true), "réservation d'office")
	E.SetLocked(E.Find("12"), true)
	T.equal(#E.Find("12").effectiveReserves, 0, "verrouillé : objet d'office sorti, non-confirmés écartés")
	local payload = thorgal.GP.BuildExportPayload()
	T.check(#payload.reserves == 1 and #payload.hardReserves == 1 and #payload.duties == 1, "export de la soirée")
	local section = thorgal.env.Guildkeep_ServerData.guilds[1].events[1]
	section.updatedAt, section.locked = thorgal.now + 100, true
	section.reserves = { { character = "Thorgal Marteau", priority = "normal", itemId = 19019, at = thorgal.now + 100 } }
	section.hardReserves = { { itemId = 19019, active = true, at = thorgal.now + 100 } }
	section.duties = { { duty = "summoner", character = "Nayra Solen", at = thorgal.now + 100 } }
	E.Prune()
	local db = thorgal.env.GuildkeepDB
	T.check(next(db.eventReserves) == nil and next(db.eventHardReserves) == nil and next(db.eventDuties) == nil, "purge")
	T.equal(E.Find("12").duties.summoner, "Nayra Solen", "soutien venu du serveur")
	T.check(R.ParseItem("|cffa335ee|Hitem:19019::::|h[x]|h|r") == 19019 and R.ParseItem("17076") == 17076, "lecture des objets")
	T.CheckClean(nayra)
	T.CheckClean(thorgal)
end)

test("Présence et butin : relevé du raid, butin, corrections, export et purge", function()
	local game = Thorgal({ officer = true })
	local A = game.GP.Attendance
	game.raid = { instance = "Cœur du Magma", members = {
		{ "Thorgal", 2, "WARRIOR", true }, { "Nayra-Solen", 0, "PRIEST", true }, { "Elaria-Brume", 0, "MAGE", true },
		{ "Kael-Fourbe", 0, "ROGUE", true }, { "Pugg-Hors", 0, "HUNTER", true } } }
	game:Fire("GROUP_ROSTER_UPDATE")
	T.check(next(game.env.GuildkeepDB.raidSessions) == nil, "moins de 5 membres de la guilde : rien")
	table.insert(game.raid.members, { "Mira-Vent", 0, "DRUID", false })
	game:Fire("GROUP_ROSTER_UPDATE")
	local session = A.CurrentRaid()
	T.check(session and session.leader == "Thorgal Marteau", "raid de guilde relevé")
	for _ = 1, 20 do
		game.now = game.now + 60
		game:Tick()
	end
	T.equal(session.participants["Nayra Solen"], 21, "minutes comptées par la minuterie")
	T.equal(session.participants["Mira Vent"], 0, "déconnectée à zéro")
	game:Fire("CHAT_MSG_LOOT", "Nayra-Solen reçoit le butin : |cffff8000|Hitem:19019::::|h[Serre-tonnerre]|h|r.")
	game:Fire("CHAT_MSG_LOOT", "Pugg-Hors reçoit le butin : |cffa335ee|Hitem:18814::::|h[Pierre]|h|r.")
	local count = 0
	for _ in pairs(game.env.GuildkeepDB.raidLoot) do
		count = count + 1
	end
	T.equal(count, 1, "butin épique des membres seulement")
	T.check(A.Correct(A.GetRaids()[1], "Kael Fourbe", "excused", "Déplacement"), "correction d'officier")
	local stats = A.Stats(A.GetRaids(), "Nayra Solen")
	T.check(stats.d30.attended == 1 and stats.loot == 1, "taux et butin")
	local payload = game.GP.BuildExportPayload()
	T.check(#payload.raids == 1 and #payload.loot == 1 and #payload.attendanceCorrections == 1, "export")
	T.ShowEveryTab(game)
	T.CheckClean(game)
end)

test("Quêtes : relevé du journal, annonce et réception entre joueurs", function()
	local quests = { { questID = 4001, title = "Le cœur du Magma", tagID = 62, isHeader = false } }
	local nayra = Nayra()
	-- Le journal est relevé à la connexion, puis annoncé aux joueurs connectés.
	local thorgal = T.Started({ settings = T.Settings(), quests = quests })
	thorgal:RunTimers()
	local char = thorgal.env.GuildkeepDB.characters["Thorgal Marteau"]
	T.check(char.quests and char.quests[1].kind == "raid", "quête de raid relevée")
	local announced = false
	for _, message in ipairs(thorgal.sent) do
		announced = announced or (message.prefix == "GKEEPEV" and message.message:find("qs", 1, true) ~= nil)
	end
	T.check(announced, "relevé annoncé aux joueurs connectés")
	thorgal:DeliverTo(nayra)
	local all = nayra.GP.Quests.GetAll()
	T.check(all[4001] and all[4001].holders["Thorgal Marteau"], "Nayra voit la quête de Thorgal")
	T.check(thorgal.GP.BuildExportPayload().characters[1].quests[1].id == 4001, "export : quêtes du personnage")
	T.CheckClean(thorgal)
	T.CheckClean(nayra)
end)

test("Import manuel : la chaîne du bot remplace les données de la guilde", function()
	local game = Thorgal()
	local data = T.RichServerData().guilds[1]
	data.version = 9
	local text = game.GP.EncodeTable(data)
	local ok = game.GP.ImportString(text)
	T.check(ok, "chaîne importée")
	T.equal(game.GP.GetServerData().version, 9, "données importées utilisées")
	T.check(game.GP.Events.Find("12"), "l'événement importé est visible")
	T.CheckClean(game)
end)

return tests
