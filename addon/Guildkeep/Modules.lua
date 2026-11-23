local ADDON_NAME, GP = ...

-- Registre des modules.
--
-- Chaque fonctionnalité est un module indépendant, activable par le joueur
-- (Options > Extensions > Guildkeep, ou /gkeep modules) : son choix est rangé
-- par compte dans GuildkeepSettings. Un module se compose de parties (données,
-- interface) déclarées par ses fichiers avec GP.Modules.Define ; ces fichiers
-- ne font que définir des fonctions. Tout effet (événements du jeu, messages
-- d'addon, onglet, minuterie, crochets) passe par le contexte reçu au
-- chargement, qui en garde la trace :
--   - un module désactivé n'est jamais chargé : il ne relève rien, n'écoute
--     rien, n'envoie rien et n'affiche pas son onglet (ses données restent) ;
--   - le chargement de chaque module est protégé : s'il échoue, ce qu'il a déjà
--     branché est retiré, et les autres modules se chargent normalement.
-- Les modules ne s'appellent jamais entre eux : ils passent par le socle
-- (crochets, services, communication). Seule dépendance admise, déclarée ici :
-- la soirée de raid s'appuie sur les événements.

local M = {}
GP.Modules = M

M.LIST = {
	{ id = "professions", label = "Métiers", parts = { "data", "ui" },
		description = "Annuaire des artisans de la guilde ; relève tes métiers et tes recettes." },
	{ id = "members", label = "Membres", parts = { "data", "ui" },
		description = "Membres de la guilde et leur équipement ; relève ton équipement." },
	{ id = "craft", label = "Artisanat", parts = { "data", "ui" },
		description = "Commandes de craft, offres des artisans et catalogue des recettes." },
	{ id = "events", label = "Événements", parts = { "data", "reminders", "ui" },
		description = "Événements de guilde, inscriptions, roster et rappels." },
	{ id = "raid", label = "Soirée de raid", parts = { "data", "ui" }, depends = { "events" },
		description = "Réservations de butin, rôles de soutien et liste de contrôle. Nécessite le module Événements." },
	{ id = "attendance", label = "Présence et butin", parts = { "data", "ui" },
		description = "Relevé de la présence en raid et du butin reçu, corrections des officiers." },
	{ id = "quests", label = "Quêtes", parts = { "data", "ui" },
		description = "Quêtes élites, de donjon et de raid partagées dans la guilde." },
	{ id = "communication", label = "Communication", parts = { "data", "ui" },
		description = "Annonces des officiers, charte de la guilde et objectifs communs." },
}

local BY_ID = {}
for _, def in ipairs(M.LIST) do
	BY_ID[def.id] = def
end

function M.Get(id)
	return BY_ID[id]
end

-- Erreurs ------------------------------------------------------------------------------

local function Report(err)
	local handler = geterrorhandler and geterrorhandler()
	if handler then
		handler(err)
	end
end

-- Appel protégé : une erreur est signalée (fenêtre d'erreurs Lua) sans rien interrompre.
function M.Protect(fn, ...)
	local ok, err = pcall(fn, ...)
	if not ok then
		Report(err)
	end
	return ok, err
end

-- Préférences (par compte) ---------------------------------------------------------------

function M.InitSettings()
	if type(GuildkeepSettings) ~= "table" then
		GuildkeepSettings = {}
	end
	if type(GuildkeepSettings.modules) ~= "table" then
		GuildkeepSettings.modules = {}
	end
	for _, def in ipairs(M.LIST) do
		if type(GuildkeepSettings.modules[def.id]) ~= "boolean" then
			GuildkeepSettings.modules[def.id] = true
		end
	end
end

-- Choix du joueur (pris en compte au prochain chargement de l'interface).
function M.IsEnabled(id)
	local prefs = type(GuildkeepSettings) == "table" and GuildkeepSettings.modules
	return type(prefs) ~= "table" or prefs[id] ~= false
end

-- Message affiché quand le joueur change un module (case ou /gkeep modules).
function M.OnPreferenceChanged(id, enabled)
	local def = BY_ID[id]
	if not def then
		return
	end
	GP.Chat(("module %s %s au prochain /reload."):format(def.label, enabled and "activé" or "désactivé"))
	if not enabled then
		for _, other in ipairs(M.LIST) do
			for _, dep in ipairs(other.depends or {}) do
				if dep == id and M.IsEnabled(other.id) then
					GP.Chat(("le module %s, qui en dépend, sera désactivé lui aussi."):format(other.label))
				end
			end
		end
	end
end

function M.SetEnabled(id, enabled)
	if not BY_ID[id] then
		return false
	end
	M.InitSettings()
	GuildkeepSettings.modules[id] = enabled and true or false
	M.OnPreferenceChanged(id, enabled)
	return true
end

-- État après chargement : loaded, disabled, blocked (dépendance absente) ou failed.
local status = {}

function M.Status(id)
	return status[id]
end

function M.IsActive(id)
	return status[id] ~= nil and status[id].state == "loaded"
end

-- Crochets internes ------------------------------------------------------------------------
-- Points de contact entre le socle et les modules : un module s'abonne à un
-- crochet (export, annonce aux joueurs connectés, relevé complet…) ; seuls les
-- abonnés de modules actifs sont appelés, chacun protégé.

local hooks = {}

function M.On(name, fn, owner)
	hooks[name] = hooks[name] or {}
	local entry = { fn = fn, owner = owner }
	table.insert(hooks[name], entry)
	return function()
		for i, other in ipairs(hooks[name]) do
			if other == entry then
				table.remove(hooks[name], i)
				return
			end
		end
	end
end

function M.Fire(name, ...)
	local list = {}
	for i, entry in ipairs(hooks[name] or {}) do
		list[i] = entry
	end
	for _, entry in ipairs(list) do
		if not entry.owner or M.IsActive(entry.owner) then
			M.Protect(entry.fn, ...)
		end
	end
end

-- Services : une fonction offerte par un module, que le socle propose aux
-- autres (ex. « commander une recette » depuis la fenêtre des recettes).
-- Absent quand le module qui l'offre n'est pas actif.
local services = {}

function M.Service(name)
	local service = services[name]
	if service and M.IsActive(service.owner) then
		return service.fn
	end
end

-- Sous-commandes de /gkeep offertes par les modules (ex. /gkeep events).
local slashCommands = {}

-- Contexte de chargement ---------------------------------------------------------------------

local function NewContext(id)
	local ctx = { id = id, cleanups = {} }

	-- Retrait à faire si le module échoue plus loin dans son chargement.
	function ctx:Track(cleanup)
		self.cleanups[#self.cleanups + 1] = cleanup
	end

	-- Événements du jeu : le gestionnaire n'est appelé que tant que le module est actif.
	function ctx:Listen(events, handler)
		local frame = CreateFrame("Frame")
		for _, name in ipairs(events) do
			frame:RegisterEvent(name)
		end
		frame:SetScript("OnEvent", function(_, event, ...)
			if M.IsActive(id) then
				M.Protect(handler, event, ...)
			end
		end)
		self:Track(function()
			frame:UnregisterAllEvents()
			frame:SetScript("OnEvent", nil)
		end)
		return frame
	end

	function ctx:After(seconds, fn)
		C_Timer.After(seconds, function()
			if M.IsActive(id) then
				M.Protect(fn)
			end
		end)
	end

	function ctx:Every(seconds, fn)
		local ticker = C_Timer.NewTicker(seconds, function()
			if M.IsActive(id) then
				M.Protect(fn)
			end
		end)
		self:Track(function()
			ticker:Cancel()
		end)
	end

	function ctx:On(name, fn)
		self:Track(M.On(name, fn, id))
	end

	function ctx:Provide(name, fn)
		services[name] = { fn = fn, owner = id }
		self:Track(function()
			if services[name] and services[name].owner == id then
				services[name] = nil
			end
		end)
	end

	function ctx:Slash(words, fn)
		for _, word in ipairs(words) do
			slashCommands[word] = { fn = fn, owner = id }
		end
		self:Track(function()
			for _, word in ipairs(words) do
				if slashCommands[word] and slashCommands[word].owner == id then
					slashCommands[word] = nil
				end
			end
		end)
	end

	-- Messages d'addon : JSON sur le canal partagé (par type) ou préfixe propre au module.
	function ctx:OnPeer(kind, handler)
		self:Track(GP.Comm.OnPeer(kind, handler, id))
	end

	function ctx:OnAddonMessage(prefix, handler)
		self:Track(GP.Comm.OnPrefix(prefix, handler, id))
	end

	function ctx:AddTab(def)
		self:Track(GP.UI.AddTab(def, id))
	end

	-- Tutoriel pas à pas de l'onglet du module (voir Tutorial.lua).
	function ctx:Tutorial(tutorialId, def)
		self:Track(GP.Tutorial.Register(tutorialId, def, id))
	end

	return ctx
end

-- Chargement ---------------------------------------------------------------------------------

local parts = {}

-- Déclare une partie d'un module ; `load(ctx)` ne s'exécute que si le module est activé.
function M.Define(id, partName, load)
	if not BY_ID[id] then
		error("module inconnu : " .. tostring(id), 2)
	end
	parts[id] = parts[id] or {}
	parts[id][partName] = load
end

-- Contexte de chaque module chargé, pour pouvoir le retirer s'il tombe en panne plus tard.
local contexts = {}

-- Retire tout ce que le module a branché ; chaque retrait n'est fait qu'une fois.
local function Unplug(ctx)
	local cleanups = ctx.cleanups
	ctx.cleanups = {}
	for i = #cleanups, 1, -1 do
		pcall(cleanups[i])
	end
end

local function Fail(id, ctx, reason)
	status[id] = { state = "failed", reason = reason }
	Unplug(ctx)
end

-- Vrai une fois les messages de connexion affichés : une panne survenue
-- ensuite est annoncée au joueur.
local announced = false

-- Panne d'un module en cours de partie (code appelé par un autre module, par
-- exemple une extension des événements) : l'erreur est signalée, le module est
-- retiré jusqu'au prochain /reload, puis ceux qui en dépendent. Les autres
-- continuent de fonctionner.
function M.Fault(id, err)
	Report(err)
	if not BY_ID[id] or not M.IsActive(id) then
		return
	end
	Fail(id, contexts[id], tostring(err))
	if announced then
		GP.Chat(("|cffff5555module %s : erreur, désactivé jusqu'au prochain /reload, contacte un administrateur|r")
			:format(BY_ID[id].label))
	end
	for _, other in ipairs(M.LIST) do
		for _, dep in ipairs(other.depends or {}) do
			if dep == id and M.IsActive(other.id) then
				status[other.id] = { state = "blocked", dependency = id }
				Unplug(contexts[other.id])
				if announced then
					GP.Chat(("module %s désactivé : il nécessite le module %s"):format(other.label, BY_ID[id].label))
				end
			end
		end
	end
end

local function LoadModule(def)
	if not M.IsEnabled(def.id) then
		status[def.id] = { state = "disabled" }
		return
	end
	for _, dep in ipairs(def.depends or {}) do
		if not M.IsActive(dep) then
			status[def.id] = { state = "blocked", dependency = dep }
			return
		end
	end
	local defined = parts[def.id] or {}
	for _, partName in ipairs(def.parts) do
		if not defined[partName] then
			-- Fichier du module absent ou en erreur avant sa déclaration.
			status[def.id] = { state = "failed", reason = ("partie « %s » introuvable"):format(partName) }
			return
		end
	end
	local ctx = NewContext(def.id)
	contexts[def.id] = ctx
	status[def.id] = { state = "loaded" }
	for _, partName in ipairs(def.parts) do
		local ok, err = pcall(defined[partName], ctx)
		if not ok then
			Report(err)
			Fail(def.id, ctx, tostring(err))
			return
		end
		if not M.IsActive(def.id) then
			-- Mis en échec pendant son propre chargement (M.Fault) : ce qu'il a
			-- branché depuis est retiré aussi.
			Unplug(ctx)
			return
		end
	end
end

function M.LoadAll()
	for _, def in ipairs(M.LIST) do
		LoadModule(def)
	end
end

-- Messages de connexion ------------------------------------------------------------------------

local COLORS = { loaded = "ff33ff99", disabled = "ff9d9d9d", blocked = "ff9d9d9d", failed = "ffff5555" }

function M.StatusText(id)
	local def, info = BY_ID[id], status[id]
	local state = info and info.state or "failed"
	local text
	if state == "loaded" then
		text = ("module %s chargé"):format(def.label)
	elseif state == "disabled" then
		text = ("module %s désactivé"):format(def.label)
	elseif state == "blocked" then
		local dep = BY_ID[info.dependency]
		text = ("module %s désactivé : il nécessite le module %s"):format(def.label, dep and dep.label or info.dependency)
	else
		text = ("module %s : échec du chargement, contacte un administrateur"):format(def.label)
	end
	return ("|c%s%s|r"):format(COLORS[state], text), state
end

-- Une ligne par module, dans l'ordre du registre.
function M.LoadMessages()
	local lines = {}
	for _, def in ipairs(M.LIST) do
		lines[#lines + 1] = "|cff33ff99Guildkeep|r — " .. M.StatusText(def.id)
	end
	return lines
end

function M.AnnounceLoad()
	announced = true
	local server = GP.GetServerData()
	print(("|cff33ff99Guildkeep|r v%s chargé — /gkeep pour ouvrir%s"):format(
		GP.VERSION, server.mock and " |cffffcc00(données de démonstration)|r" or ""))
	for _, line in ipairs(M.LoadMessages()) do
		print(line)
	end
end

-- Commande /gkeep --------------------------------------------------------------------------------

local function FindModule(text)
	text = (text or ""):lower()
	for _, def in ipairs(M.LIST) do
		if def.id == text or def.label:lower() == text then
			return def
		end
	end
end

-- /gkeep modules : l'état de chaque module ; /gkeep modules <id> on|off : le choix du joueur.
local function SlashModules(rest)
	local name, value = (rest or ""):match("^(.-)%s+(%S+)$")
	local def = FindModule(name)
	value = value and value:lower()
	if def and (value == "on" or value == "off") then
		M.SetEnabled(def.id, value == "on")
		return
	end
	for _, other in ipairs(M.LIST) do
		print(("  %s  |cff9d9d9d(%s, %s)|r"):format(M.StatusText(other.id), other.id,
			M.IsEnabled(other.id) and "activé" or "désactivé"))
	end
	GP.Chat("/gkeep modules <nom> on|off, ou Options > Extensions > Guildkeep ; /reload pour appliquer.")
end

SLASH_GUILDKEEP1 = "/gkeep"
SLASH_GUILDKEEP2 = "/gp"
SlashCmdList.GUILDKEEP = function(msg)
	local command, rest = strtrim(msg or ""):match("^(%S*)%s*(.-)$")
	command = (command or ""):lower()
	if command == "modules" or command == "module" then
		SlashModules(rest)
		return
	end
	if command == "options" or command == "config" then
		if M.OpenSettings then
			M.OpenSettings()
		end
		return
	end
	if command == "debug" then
		M.Fire("DEBUG")
		return
	end
	if command == "tuto" or command == "tutoriel" or command == "tutorial" then
		GP.Tutorial.Slash(rest)
		return
	end
	local entry = slashCommands[command]
	if entry and M.IsActive(entry.owner) then
		M.Protect(entry.fn, rest)
		return
	end
	GP.ToggleMainFrame()
end

-- Démarrage ------------------------------------------------------------------------------------

local driver = CreateFrame("Frame")
driver:RegisterEvent("ADDON_LOADED")
driver:RegisterEvent("PLAYER_LOGIN")
driver:SetScript("OnEvent", function(self, event, arg1)
	if event == "ADDON_LOADED" and arg1 == ADDON_NAME then
		self:UnregisterEvent("ADDON_LOADED")
		GP.InitDB()
		M.InitSettings()
		M.LoadAll()
		if M.RegisterSettingsPanel then
			M.Protect(M.RegisterSettingsPanel)
		end
	elseif event == "PLAYER_LOGIN" then
		M.AnnounceLoad()
	end
end)
