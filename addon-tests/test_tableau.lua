-- Lot 8 : tableau de bord (une carte par module actif) et thèmes de couleur.

local T = require("helpers")
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

local function Titles(cards)
	local titles = {}
	for i, card in ipairs(cards) do
		titles[i] = card.title
	end
	return table.concat(titles, ",")
end

test("le tableau est le premier onglet et réunit une carte par module, dans l'ordre", function()
	local game = T.Started({ settings = T.Settings(), officer = true, serverData = T.RichServerData() })
	game.GP.Profiles.SaveProfile({ main = "Thorgal Marteau", roles = { "tank" } })
	game.GP.ToggleMainFrame()
	T.equal(game:TabLabels()[1], "Tableau", "premier onglet")
	T.equal(game.GP.UI.State.tab, "tabHome", "la fenêtre s'ouvre sur le tableau")
	local cards = game.GP.Home.Cards()
	T.check(Titles(cards):find("^Prochains événements,") ~= nil, "les événements d'abord : " .. Titles(cards))
	T.check(Titles(cards):find("Commandes de craft", 1, true), "carte des commandes")
	T.check(table.concat(cards[1].lines, " "):find("Cœur du Magma", 1, true), "prochain événement affiché")
	-- « Ouvrir » mène à l'onglet du module.
	game.GP.ShowTab("tabHome")
	for _, object in ipairs(game.objects) do
		if rawget(object, "tab") == "tabEvents" and object:IsShown() then
			object:Click()
			break
		end
	end
	T.equal(game.GP.UI.State.tab, "tabEvents", "« Ouvrir » affiche l'onglet Événements")
	T.CheckClean(game)
end)

test("un module désactivé n'a pas de carte ; une carte en panne n'empêche pas les autres", function()
	local game = T.Started({ settings = T.Settings({ "craft" }), officer = true, serverData = T.RichServerData() })
	T.check(not Titles(game.GP.Home.Cards()):find("Commandes de craft", 1, true), "pas de carte Artisanat")
	game.GP.Communication.Get = function() error("panne simulée") end
	game.GP.ShowTab("tabHome")
	local titles = Titles(game.GP.Home.Cards())
	T.check(titles:find("Prochains événements", 1, true), "les autres cartes restent : " .. titles)
	T.check(not titles:find("Vie de la guilde", 1, true), "pas de carte pour le module en panne")
	T.CheckClean(game, true)
end)

test("Tableau de bord désactivé : pas d'onglet, les autres onglets s'affichent", function()
	local game = T.Started({ settings = T.Settings({ "home" }), serverData = T.RichServerData() })
	local labels = T.ShowEveryTab(game)
	T.check(not T.contains(labels, "Tableau"), "pas d'onglet Tableau")
	T.equal(labels[1], "Métiers", "le premier onglet redevient Métiers")
	T.CheckClean(game)
end)

test("thèmes : choisis par /gkeep theme ou le bouton, gardés par compte, appliqués au chargement", function()
	local game = T.Started({ settings = T.Settings() })
	local COLOR = game.GP.UI.COLOR
	T.equal(game.env.GuildkeepSettings.theme, "or", "thème par défaut")
	game:Slash("theme arcanes")
	T.equal(game.env.GuildkeepSettings.theme, "arcanes", "thème enregistré")
	T.equal(COLOR.gold[1], 0.8, "accent repeint")
	game:Slash("thème Émeraude")
	T.equal(game.env.GuildkeepSettings.theme, "emeraude", "nom avec accent accepté")
	game.prints = {}
	game:Slash("theme licorne")
	T.check(T.find(game:PlainPrints(), "thème inconnu"), "thème inconnu signalé")
	game.GP.ShowTab("tabHome")
	game.GP.Theme.Next()
	T.equal(game.env.GuildkeepSettings.theme, "arcanes", "le bouton passe au thème suivant")

	local settings = T.Settings()
	settings.theme = "braise"
	local other = T.Started({ settings = settings })
	T.equal(other.GP.UI.COLOR.gold[2], 0.6, "thème appliqué dès le chargement")
	local broken = T.Settings()
	broken.theme = "inexistant"
	local third = T.Started({ settings = broken })
	T.equal(third.env.GuildkeepSettings.theme, "or", "thème inconnu remplacé par celui par défaut")
	T.CheckClean(game)
	T.CheckClean(other)
	T.CheckClean(third)
end)

return tests
