-- Tutoriels pas à pas : bienvenue puis tutoriel de chaque onglet, passables,
-- rejouables (« ? », /gkeep tuto), jamais pour un module désactivé, et chaque
-- étape désigne un élément qui existe vraiment.

local T = require("helpers")
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

local function Game(disabled, tutorials)
	local settings = T.Settings(disabled)
	settings.tutorials = tutorials
	return T.Started({ settings = settings, officer = true, serverData = T.RichServerData() })
end

-- Parcourt tout le tutoriel affiché (Suivant jusqu'au bout) ; renvoie son id et son nombre d'étapes.
local function RunThrough(game)
	local Tu = game.GP.Tutorial
	local id = Tu.Current()
	local steps = 0
	while Tu.Current() == id and steps < 50 do
		steps = steps + 1
		Tu.Next()
	end
	return id, steps
end

test("première ouverture : bienvenue, puis le tutoriel de l'onglet ; plus rien ensuite", function()
	local game = Game()
	local Tu = game.GP.Tutorial
	game.GP.ShowTab("tabEvents")
	game:RunTimers()
	T.equal(Tu.Current(), "welcome", "bienvenue d'abord")
	T.equal(select(1, RunThrough(game)), "welcome", "bienvenue parcourue")
	T.equal(Tu.Current(), "events", "puis le tutoriel des événements")
	RunThrough(game)
	T.equal(Tu.Current(), "raid", "puis celui de la soirée de raid, sur le même onglet")
	RunThrough(game)
	T.check(not Tu.IsRunning(), "terminé")
	T.check(Tu.IsDone("welcome") and Tu.IsDone("events") and Tu.IsDone("raid"), "gardés comme vus")
	game.GP.ShowTab("tabProfessions")
	game.GP.ShowTab("tabEvents")
	T.check(Tu.Current() ~= "events", "pas de second passage sur Événements")
	T.CheckClean(game)
end)

test("Passer : le tutoriel est compté comme vu, le suivant de la file s'affiche", function()
	local game = Game()
	local Tu = game.GP.Tutorial
	game.GP.ShowTab("tabQuests")
	T.equal(Tu.Current(), "welcome", "bienvenue")
	Tu.Skip()
	T.check(Tu.IsDone("welcome"), "bienvenue passée = vue")
	T.equal(Tu.Current(), "quests", "tutoriel des quêtes ensuite")
	Tu.Skip()
	T.check(not Tu.IsRunning(), "plus rien")
	T.CheckClean(game)
end)

test("« ? » et /gkeep tuto rejouent le tutoriel de l'onglet, même déjà vu", function()
	local game = Game(nil, { enabled = true, done = { welcome = true, craft = true } })
	local Tu = game.GP.Tutorial
	game.GP.ShowTab("tabOrders")
	T.check(not Tu.IsRunning(), "déjà vu : rien à l'ouverture")
	game.GP.UI.GetMainFrame().helpButton:Click()
	T.equal(Tu.Current(), "craft", "le bouton ? relance le tutoriel")
	Tu.Skip()
	game:Slash("tuto")
	T.equal(Tu.Current(), "craft", "/gkeep tuto aussi")
	T.CheckClean(game)
end)

test("/gkeep tuto off coupe tout, reset relance tout", function()
	local game = Game()
	local Tu = game.GP.Tutorial
	game:Slash("tuto off")
	game.GP.ShowTab("tabMembers")
	T.check(not Tu.IsRunning(), "aucun tutoriel une fois coupés")
	game:Slash("tuto reset")
	game.GP.ShowTab("tabProfessions")
	T.equal(Tu.Current(), "welcome", "relancés après reset")
	T.CheckClean(game)
end)

test("module désactivé : son tutoriel n'est jamais proposé", function()
	local game = Game({ "raid", "quests" })
	local Tu = game.GP.Tutorial
	for _, item in ipairs(Tu.Registered()) do
		T.check(item.id ~= "raid" and item.id ~= "quests", "pas de tutoriel pour " .. item.id)
	end
	game.GP.ShowTab("tabEvents")
	Tu.Skip()
	T.equal(Tu.Current(), "events", "Événements seul")
	Tu.Skip()
	T.check(not Tu.IsRunning(), "pas de tutoriel de la soirée")
	T.CheckClean(game)
end)

test("chaque étape de chaque tutoriel désigne un élément qui existe", function()
	local game = Game(nil, { enabled = false, done = {} })
	local GP = game.GP
	T.ShowEveryTab(game)
	local ids = {}
	for _, item in ipairs(GP.Tutorial.Registered()) do
		ids[#ids + 1] = item.id
		if item.def.tab then
			GP.ShowTab(item.def.tab)
			-- Un événement sélectionné, pour que le détail existe.
			if item.def.tab == "tabEvents" then
				game:RunTimers()
			end
		end
		for index, step in ipairs(item.def.steps) do
			T.check(type(step.title) == "string" and step.title ~= "", ("%s, étape %d : titre"):format(item.id, index))
			T.check(type(step.text) == "string" and #step.text > 20, ("%s, étape %d : texte"):format(item.id, index))
			if step.prepare then
				step.prepare()
			end
			if step.target then
				local ok, frame = pcall(step.target)
				T.check(ok and frame ~= nil, ("%s, étape %d (« %s ») : élément introuvable"):format(item.id, index, step.title))
			end
		end
	end
	table.sort(ids)
	T.equal(table.concat(ids, ","), "attendance,communication,craft,events,home,members,professions,profiles,quests,raid,welcome", "un tutoriel par module, plus la bienvenue")
	T.CheckClean(game)
end)

return tests
