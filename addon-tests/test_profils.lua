-- Lot 7 : profils, talents, versions, arrivées et départs, accueil (module Profils et accueil).

local T = require("helpers")
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

-- Nayra Solen (membre) avec un reroll de la même guilde sur ce compte.
local function Nayra(serverData)
	return T.Started({ first = "Nayra", last = "Solen", classFile = "PRIEST", settings = T.Settings(),
		db = { characters = { ["Nayra Solen"] = { guild = T.GUILD }, ["Nayra Reroll"] = { guild = T.GUILD },
			["Nayra Ailleurs"] = { guild = "Les Autres" } }, importedByGuild = {} },
		serverData = serverData or T.RichServerData() })
end

local function Thorgal(options)
	options = options or {}
	options.settings = options.settings or T.Settings()
	options.officer = true
	options.serverData = options.serverData or T.RichServerData()
	return T.Started(options)
end

test("profil : seulement ses personnages de la guilde, visible aussitôt, partagé et exporté", function()
	local nayra, thorgal = Nayra(), Thorgal()
	local P = nayra.GP.Profiles
	T.check(not P.SaveProfile({ main = "Elaria Brume" }), "principal d'un autre compte refusé")
	T.check(P.SaveProfile({ main = "Nayra Solen", alts = { "Nayra Reroll", "Nayra Ailleurs", "Elaria Brume" },
		roles = { "healer", "pirate", "ranged" }, availability = "  Soirs de semaine  " }), "profil enregistré")
	local profile = P.ProfileOf("Nayra Reroll")
	T.check(profile and profile.main == "Nayra Solen", "le reroll pointe vers le principal")
	T.equal(#profile.alts, 1, "seul le reroll de cette guilde est gardé")
	T.equal(table.concat(profile.roles, ","), "healer,ranged", "rôles connus seulement")
	T.equal(profile.availability, "Soirs de semaine", "disponibilités nettoyées")

	nayra:DeliverTo(thorgal)
	local seen = thorgal.GP.Profiles.ProfileOf("Nayra Reroll")
	T.check(seen and seen.main == "Nayra Solen", "Thorgal reçoit le profil")

	local payload = nayra.GP.BuildExportPayload()
	T.equal(payload.profiles.profiles[1].main, "Nayra Solen", "profil exporté")
	T.equal(payload.profiles.versions[1].version, nayra.GP.VERSION, "version de l'addon relevée à la connexion")
	T.CheckClean(nayra)
	T.CheckClean(thorgal)
end)

test("un profil forgé au nom d'un autre joueur est ignoré", function()
	local nayra, thorgal = Nayra(), Thorgal()
	nayra.GP.Comm.Broadcast({ t = "pf", k = "Elaria Brume", e = { main = "Elaria Brume", alts = {}, roles = { "tank" }, by = "Nayra Solen", at = T.NOW + 5 } })
	nayra:DeliverTo(thorgal)
	T.check(thorgal.GP.Profiles.ProfileOf("Elaria Brume") == nil, "profil forgé ignoré")
	T.CheckClean(nayra)
	T.CheckClean(thorgal)
end)

test("talents relevés à la connexion ; purgés quand le serveur les reprend", function()
	local server = T.RichServerData()
	local game = Thorgal({ serverData = server })
	game:RunTimers()
	local talents = game.env.GuildkeepDB.talents["Thorgal Marteau"]
	T.check(talents, "talents relevés")
	T.equal(talents.summary, "31/20/0", "points par arbre")
	T.equal(talents.spec, "Armes", "arbre principal")
	T.equal(game.GP.Profiles.Get().talents["Thorgal Marteau"].summary, "31/20/0", "visibles aussitôt")

	server.guilds[1].profiles = { profiles = {}, talents = { { character = "Thorgal Marteau", spec = "Armes", summary = "31/20/0", at = talents.at } },
		rosterChanges = {}, onboarding = {}, versions = { { character = "Thorgal Marteau", version = game.GP.VERSION, at = 1 } } }
	game.GP.Profiles.Prune()
	T.check(next(game.env.GuildkeepDB.talents) == nil, "talents purgés")
	T.check(next(game.env.GuildkeepDB.versions) == nil, "version purgée")
	T.CheckClean(game)
end)

test("arrivées et départs : relevés au changement du roster, pas au premier passage", function()
	local game = Thorgal()
	game:Fire("GUILD_ROSTER_UPDATE")
	game:RunTimers()
	T.equal(#game.env.GuildkeepDB.rosterChanges, 0, "premier passage : référence seulement")
	table.insert(game.roster, { "Bleu-Neuf", 5, true })
	table.remove(game.roster, 5) -- Mira Vent part
	game:Fire("GUILD_ROSTER_UPDATE")
	game:Fire("GUILD_ROSTER_UPDATE")
	game:RunTimers()
	local journal = game.GP.Profiles.Get().journal
	T.equal(#journal, 2, "une arrivée et un départ")
	T.check(T.find(game:PlainPrints(), "Bleu Neuf a rejoint la guilde"), "arrivée annoncée")
	local payload = game.GP.BuildExportPayload()
	T.equal(#payload.profiles.rosterChanges, 2, "journal exporté")
	-- Le nouveau est suivi par l'accueil (officier).
	local incomplete = game.GP.Profiles.Incomplete()
	T.equal(incomplete[1].character, "Bleu Neuf", "le nouveau attend son accueil")
	T.CheckClean(game)
end)

test("accueil : chacun pour soi, les officiers pour les autres ; rappel à copier", function()
	local nayra, thorgal = Nayra(), Thorgal()
	local P = nayra.GP.Profiles
	T.check(P.SetStep(nil, "handbook", true), "Nayra coche sa charte")
	local ok, err = P.SetStep("Bleu Neuf", "intro", true)
	T.check(not ok and err, "un membre ne coche pas pour un autre")
	T.check(not P.SetStep(nil, "inconnue", true), "étape inconnue refusée")
	P.SaveProfile({ main = "Nayra Solen", roles = { "healer" } })
	local steps = P.StepsOf("Nayra Solen")
	T.check(steps.done.handbook and steps.done.main and steps.done.profile, "étape cochée et étapes déduites du profil")

	nayra:DeliverTo(thorgal)
	T.check(thorgal.GP.Profiles.StepsOf("Nayra Solen").done.handbook, "Thorgal voit la coche de Nayra")
	T.check(thorgal.GP.Profiles.SetStep("Nayra Solen", "intro", true), "un officier coche pour un nouveau")
	T.check(thorgal.GP.Profiles.ReminderText():find("Nayra Solen", 1, true), "rappel listant ce qui reste")
	T.CheckClean(nayra)
	T.CheckClean(thorgal)
end)

test("l'onglet Profils s'affiche pour chaque section ; /gkeep profil moi ouvre l'éditeur", function()
	local game = Thorgal()
	game.GP.Profiles.SaveProfile({ main = "Thorgal Marteau", roles = { "tank" } })
	game.GP.ShowTab("tabProfiles")
	T.check(T.contains(game:TabLabels(), "Profils"), "onglet Profils présent")
	for _, object in ipairs(game.objects) do
		if rawget(object, "profilesSection") and object:IsShown() then
			object:Click()
		end
	end
	game:Slash("profil moi")
	T.check(game.env.GuildkeepProfileEditor and game.env.GuildkeepProfileEditor:IsShown(), "éditeur ouvert")
	game.env.GuildkeepProfileEditor.save:Click()
	T.CheckClean(game)
end)

test("module Profils désactivé : rien n'est relevé, les autres modules fonctionnent", function()
	local game = Thorgal({ settings = T.Settings({ "profiles" }) })
	game:RunTimers()
	T.check(game.env.GuildkeepDB.talents == nil, "aucun relevé de talents")
	T.check(not T.contains(game:TabLabels(), "Profils"), "pas d'onglet Profils")
	T.check(game.GP.Communication.PostAnnouncement("Sortie", "Ce soir"), "la Communication fonctionne")
	T.CheckClean(game)
end)

return tests
