-- Tout activé : chargement, messages de connexion, onglets, panneau d'options.

local T = require("helpers")
local tests = {}
local function test(name, fn)
	tests[#tests + 1] = { name = name, fn = fn }
end

test("tout activé : chaque module annonce « chargé » à la connexion", function()
	local game = T.Started({ settings = T.Settings() })
	T.CheckClean(game)
	local prints = game:PlainPrints()
	T.equal(prints[1], "Guildkeep v0.1.0 chargé — /gkeep pour ouvrir", "ligne d'accueil")
	local lines = T.ModuleLines(game)
	T.equal(#lines, #T.MODULES, "une ligne par module")
	for i, id in ipairs(T.MODULES) do
		T.equal(lines[i], ("module %s chargé"):format(T.LABELS[id]), "message du module " .. id)
		T.check(game.GP.Modules.IsActive(id), "module actif : " .. id)
	end
end)

test("messages de chargement : affichés à la connexion, pas avant", function()
	local game = T.Game({ settings = T.Settings() })
	game:Fire("ADDON_LOADED", "Guildkeep")
	T.equal(#game.prints, 0, "rien dans le chat au chargement de l'addon")
	game:Fire("PLAYER_LOGIN")
	T.equal(#T.ModuleLines(game), #T.MODULES, "les messages arrivent à la connexion")
	T.CheckClean(game)
end)

test("préférences absentes (première installation) : tout est activé et enregistré par compte", function()
	local game = T.Started({ settings = nil })
	T.CheckClean(game)
	for _, id in ipairs(T.MODULES) do
		T.check(game.GP.Modules.IsActive(id), "actif par défaut : " .. id)
		T.equal(game.env.GuildkeepSettings.modules[id], true, "préférence enregistrée : " .. id)
	end
end)

test("tout activé : un onglet par module, la fenêtre s'ouvre et chaque onglet s'affiche avec des données", function()
	local game = T.Started({ settings = T.Settings(), serverData = T.RichServerData() })
	local labels = T.ShowEveryTab(game)
	T.equal(table.concat(labels, ","), "Métiers,Membres,Commandes,Événements,Présence,Quêtes", "onglets dans l'ordre")
	T.check(game.GP.IsMainFrameShown(), "fenêtre ouverte")
	T.CheckClean(game)
end)

test("tout activé : relevés, messages d'addon et minuteries branchés", function()
	local game = T.Started({
		settings = T.Settings(),
		professions = { { name = "Alchimie", level = 300, max = 300, skillLine = 171 } },
		equipment = { [1] = "|cffa335ee|Hitem:16921::::|h[Auréole]|h|r" },
		quests = { { questID = 4001, title = "Le cœur du Magma", tagID = 62, isHeader = false } },
	})
	local char = game.env.GuildkeepDB.characters["Thorgal Marteau"]
	T.check(char and char.classFile == "WARRIOR", "identité relevée (socle)")
	T.check(char.professions and char.professions.Alchimie, "métiers relevés (module Métiers)")
	T.check(char.equipment and char.equipment["1"], "équipement relevé (module Membres)")
	game:Fire("QUEST_LOG_UPDATE")
	game:RunTimers()
	T.check(char.quests and char.quests[1] and char.quests[1].id == 4001, "quêtes relevées (module Quêtes)")
	T.check(game.prefixes.GPROF, "préfixe des commandes enregistré (module Artisanat)")
	T.check(game.prefixes.GKEEPEV, "canal partagé enregistré")
	T.equal(#game.tickers, 1, "minuterie de présence (module Présence et butin)")
	T.CheckClean(game)
end)

test("tout activé : l'export manuel contient les données de chaque module", function()
	local game = T.Started({ settings = T.Settings(), serverData = T.RichServerData() })
	local GP = game.GP
	GP.Craft.CreateOrder("Nayra Solen", "Flacon des Titans", 1, nil, 17635)
	local key = GP.Events.Create({ title = "Onyxia", kind = "raid", startsAt = T.NOW + 2 * 86400, tank = 1 })
	GP.RaidNight.SetReserve(GP.Events.Find("12"), "normal", 18814, "Pierre")
	GP.Events.Respond(GP.Events.Find(key), "yes", "tank", nil, nil)
	game.env.GuildkeepDB.raidSessions["k"] = { key = "k", instance = "Onyxia", day = "2026-11-23", startedAt = T.NOW, endedAt = T.NOW,
		participants = {}, classes = {}, guild = T.GUILD }
	local payload = GP.BuildExportPayload()
	T.check(payload.characters and #payload.characters == 1, "personnages (socle)")
	T.check(payload.orders and #payload.orders == 1, "commandes (Artisanat)")
	T.check(payload.events and payload.signups, "événements et réponses (Événements)")
	T.check(payload.reserves and #payload.reserves == 1, "réservations (Soirée de raid)")
	T.check(payload.raids and #payload.raids == 1, "raids relevés (Présence et butin)")
	T.check(GP.BuildExportString():sub(1, 4) == "GP1!", "chaîne d'export au format GP1!")
	T.CheckClean(game)
end)

test("panneau d'options : une case par module, Soirée de raid rattachée aux Événements", function()
	local game = T.Started({ settings = T.Settings() })
	T.equal(#game.settingsCategories, 1, "une catégorie Guildkeep")
	local category = game.settingsCategories[1]
	T.equal(category.name, "Guildkeep", "nom de la catégorie")
	T.equal(#category.checkboxes, #T.MODULES, "une case par module")
	local byKey = {}
	for i, checkbox in ipairs(category.checkboxes) do
		T.equal(checkbox.setting.key, T.MODULES[i], "case dans l'ordre du registre")
		T.equal(checkbox.setting.name, T.LABELS[T.MODULES[i]], "libellé de la case")
		T.check(checkbox.setting.tbl == game.env.GuildkeepSettings.modules, "case liée aux préférences du compte")
		T.check(checkbox.tooltip:find("/reload", 1, true), "l'infobulle indique le /reload")
		byKey[checkbox.setting.key] = checkbox
	end
	T.check(byKey.raid.parent == byKey.events, "Soirée de raid dépend de la case Événements")
	T.check(byKey.raid.predicate() == true, "Soirée de raid disponible tant que les Événements sont cochés")
	byKey.events.setting:SetValue(false)
	game:RunTimers()
	T.check(byKey.raid.predicate() == false, "Événements décochés : Soirée de raid grisée")
	local prints = game:PlainPrints()
	T.check(T.find(prints, "module Événements désactivé au prochain /reload."), "message au changement de case")
	T.check(T.find(prints, "le module Soirée de raid, qui en dépend, sera désactivé lui aussi."), "dépendance signalée")
	T.equal(game.env.GuildkeepSettings.modules.events, false, "préférence enregistrée")
	T.check(game.GP.Modules.IsActive("events"), "le changement attend le /reload")
	local header = category.initializers[1]
	T.check(header and header.kind == "header" and header.name:find("/reload", 1, true), "en-tête du panneau, qui indique le /reload")
	local button = category.initializers[2]
	T.check(button and button.kind == "button", "bouton « Recharger l'interface »")
	button.onClick()
	T.check(game.reloaded, "le bouton recharge l'interface")
	game:Slash("options")
	T.equal(game.openedCategory, category.id, "/gkeep options ouvre le panneau")
	T.CheckClean(game)
end)

test("panneau d'options de repli (client sans API Settings)", function()
	local game = T.Started({ settings = T.Settings(), noSettingsApi = true })
	T.equal(#game.interfacePanels, 1, "panneau enregistré dans les options d'interface")
	local panel = game.interfacePanels[1]
	T.equal(panel.name, "Guildkeep", "nom du panneau")
	local checks = {}
	for _, object in ipairs(game.objects) do
		if object._template == "UICheckButtonTemplate" then
			checks[#checks + 1] = object
		end
	end
	T.equal(#checks, #T.MODULES, "une case par module")
	panel:Show()
	panel:Hide()
	panel:Show()
	T.check(checks[3]:GetChecked(), "case Artisanat cochée")
	checks[3]:SetChecked(false)
	checks[3]:Click()
	T.equal(game.env.GuildkeepSettings.modules.craft, false, "décocher Artisanat l'enregistre")
	T.check(T.find(game:PlainPrints(), "module Artisanat désactivé au prochain /reload."), "message au changement")
	checks[4]:SetChecked(false)
	checks[4]:Click()
	T.check(checks[5]:IsEnabled() == false, "Événements décochés : case Soirée de raid grisée")
	game:Slash("options")
	T.check(game.openedPanel == panel, "/gkeep options ouvre le panneau")
	T.CheckClean(game)
end)

test("/gkeep modules : état de chaque module et changement de préférence", function()
	local game = T.Started({ settings = T.Settings({ "quests" }) })
	game.prints = {}
	game:Slash("modules")
	local prints = game:PlainPrints()
	T.check(T.find(prints, "module Métiers chargé"), "état listé")
	T.check(T.find(prints, "module Quêtes désactivé"), "module désactivé listé")
	game:Slash("modules quests on")
	T.equal(game.env.GuildkeepSettings.modules.quests, true, "/gkeep modules quests on")
	game:Slash("modules Artisanat off")
	T.equal(game.env.GuildkeepSettings.modules.craft, false, "nom du module accepté")
	T.CheckClean(game)
end)

return tests
