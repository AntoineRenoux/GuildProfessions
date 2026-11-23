local _, GP = ...

-- Tutoriels pas à pas (socle). Chaque module déclare, par ctx:Tutorial, les
-- étapes de son onglet : chaque étape montre un bouton (encadré doré) et
-- explique à quoi il sert. Un tutoriel se lance la première fois que son
-- onglet s'affiche ; on peut le passer à tout moment (Passer, Échap), le
-- revoir avec le bouton « ? » de la fenêtre ou /gkeep tuto, et couper tous
-- les tutoriels. L'avancement est gardé par compte (GuildkeepSettings.tutorials).
-- Le tutoriel d'un module désactivé ou en échec n'est jamais proposé.

GP.Tutorial = {}
local T = GP.Tutorial

local registry = {} -- id → { def, owner }

-- Préférences ---------------------------------------------------------------------------------

local function Prefs()
	if type(GuildkeepSettings) ~= "table" then
		GuildkeepSettings = {}
	end
	local prefs = GuildkeepSettings.tutorials
	if type(prefs) ~= "table" then
		prefs = {}
		GuildkeepSettings.tutorials = prefs
	end
	if type(prefs.enabled) ~= "boolean" then
		prefs.enabled = true
	end
	if type(prefs.done) ~= "table" then
		prefs.done = {}
	end
	return prefs
end

function T.IsEnabled()
	return Prefs().enabled
end

function T.SetEnabled(enabled)
	Prefs().enabled = enabled and true or false
end

function T.IsDone(id)
	return Prefs().done[id] == true
end

function T.MarkDone(id)
	Prefs().done[id] = true
end

function T.Reset()
	Prefs().done = {}
	Prefs().enabled = true
end

-- Déclaration -----------------------------------------------------------------------------------

-- def = { tab = clé d'onglet (nil : tutoriel de bienvenue, à la première ouverture),
--         order = n, title = "…", steps = { { target = function() return cadre end,
--         title = "…", text = "…", hint = "si le bouton n'est pas visible", prepare = function() end } } }
function T.Register(id, def, owner)
	registry[id] = { def = def, owner = owner }
	return function()
		if registry[id] and registry[id].owner == owner then
			registry[id] = nil
		end
	end
end

local function Available(entry)
	return entry and (not entry.owner or GP.Modules.IsActive(entry.owner))
end

-- Cible désignée par son nom sur le panneau d'un onglet (panel.tutorialTargets).
function T.Target(tabKey, name)
	local frame = GP.UI.GetMainFrame and GP.UI.GetMainFrame()
	local panel = frame and frame[tabKey]
	local targets = panel and panel.tutorialTargets
	return targets and targets[name] or nil
end

-- Tutoriels d'un onglet (ou de bienvenue si tab = nil), dans l'ordre.
local function ForTab(tab)
	local list = {}
	for id, entry in pairs(registry) do
		if Available(entry) and entry.def.tab == tab then
			list[#list + 1] = { id = id, def = entry.def }
		end
	end
	table.sort(list, function(a, b)
		if (a.def.order or 0) ~= (b.def.order or 0) then
			return (a.def.order or 0) < (b.def.order or 0)
		end
		return a.id < b.id
	end)
	return list
end

-- Affichage -------------------------------------------------------------------------------------

local overlay
local running -- { queue = { {id, def} … }, index = étape, current = { id, def } }

local function Edge(parent, layer, r, g, b, a)
	local texture = parent:CreateTexture(nil, layer)
	texture:SetColorTexture(r, g, b, a)
	return texture
end

-- Cadre doré autour d'un élément.
local function Border(frame, thickness, color)
	local edges = {}
	for _, side in ipairs({ "top", "bottom", "left", "right" }) do
		edges[side] = Edge(frame, "OVERLAY", color[1], color[2], color[3], color[4] or 1)
	end
	edges.top:SetPoint("TOPLEFT")
	edges.top:SetPoint("TOPRIGHT")
	edges.top:SetHeight(thickness)
	edges.bottom:SetPoint("BOTTOMLEFT")
	edges.bottom:SetPoint("BOTTOMRIGHT")
	edges.bottom:SetHeight(thickness)
	edges.left:SetPoint("TOPLEFT")
	edges.left:SetPoint("BOTTOMLEFT")
	edges.left:SetWidth(thickness)
	edges.right:SetPoint("TOPRIGHT")
	edges.right:SetPoint("BOTTOMRIGHT")
	edges.right:SetWidth(thickness)
	return edges
end

local Finish, ShowStep

local function CreateOverlay()
	local UI = GP.UI
	local frame = CreateFrame("Frame", "GuildkeepTutorial", UIParent)
	frame:SetFrameStrata("FULLSCREEN_DIALOG")
	frame:SetToplevel(true)
	frame:SetAllPoints(UIParent)
	frame:EnableMouse(false)
	frame:Hide()

	frame.glow = CreateFrame("Frame", nil, frame)
	local fill = Edge(frame.glow, "BACKGROUND", 1, 0.82, 0, 0.12)
	fill:SetAllPoints()
	Border(frame.glow, 2, { 1, 0.82, 0, 1 })

	local box = CreateFrame("Frame", nil, frame)
	box:SetSize(340, 170)
	box:EnableMouse(true)
	local bg = Edge(box, "BACKGROUND", 0.05, 0.04, 0.03, 0.95)
	bg:SetAllPoints()
	Border(box, 1, { 1, 0.82, 0, 0.8 })
	frame.box = box

	box.module = UI.Label(box, nil, "GameFontHighlightSmall", UI.COLOR.muted)
	box.module:SetPoint("TOPLEFT", 12, -10)
	box.counter = UI.Label(box, nil, "GameFontHighlightSmall", UI.COLOR.muted)
	box.counter:SetPoint("TOPRIGHT", -12, -10)
	box.title = UI.Label(box, nil, "GameFontNormalLarge")
	box.title:SetPoint("TOPLEFT", box.module, "BOTTOMLEFT", 0, -6)
	box.title:SetPoint("RIGHT", -12, 0)
	box.text = UI.Label(box, nil, "GameFontHighlight")
	box.text:SetPoint("TOPLEFT", box.title, "BOTTOMLEFT", 0, -8)
	box.text:SetWidth(316)
	box.text:SetWordWrap(true)
	box.text:SetJustifyV("TOP")

	box.next = UI.Button(box, "Suivant", 90, function()
		T.Next()
	end)
	box.next:SetPoint("BOTTOMRIGHT", -10, 10)
	box.prev = UI.Button(box, "Précédent", 90, function()
		T.Previous()
	end)
	box.prev:SetPoint("RIGHT", box.next, "LEFT", -4, 0)
	box.skip = UI.Button(box, "Passer", 70, function()
		T.Skip()
	end)
	box.skip:SetPoint("RIGHT", box.prev, "LEFT", -4, 0)
	box.never = CreateFrame("CheckButton", nil, box, "UICheckButtonTemplate")
	box.never:SetSize(20, 20)
	box.never:SetPoint("BOTTOMLEFT", 8, 34)
	UI.SetCheckLabel(box.never, "Ne plus proposer de tutoriels")
	box.never:SetScript("OnClick", function(self)
		T.SetEnabled(not self:GetChecked())
	end)

	-- Échap ferme le tutoriel en cours (compté comme passé).
	tinsert(UISpecialFrames, "GuildkeepTutorial")
	frame:SetScript("OnHide", function()
		if running then
			Finish(true)
		end
	end)
	return frame
end

local function Place(target)
	local box, glow = overlay.box, overlay.glow
	box:ClearAllPoints()
	glow:ClearAllPoints()
	local visible = target and target.IsVisible and target:IsVisible() and target.GetBottom and target:GetBottom()
	if visible then
		glow:SetPoint("TOPLEFT", target, "TOPLEFT", -4, 4)
		glow:SetPoint("BOTTOMRIGHT", target, "BOTTOMRIGHT", 4, -4)
		glow:Show()
		-- Sous l'élément s'il reste la place, sinon au-dessus.
		if target:GetBottom() > box:GetHeight() + 40 then
			box:SetPoint("TOP", target, "BOTTOM", 0, -14)
		else
			box:SetPoint("BOTTOM", target, "TOP", 0, 14)
		end
	else
		glow:Hide()
		local main = GP.UI.GetMainFrame and GP.UI.GetMainFrame()
		box:SetPoint("CENTER", (main and main:IsShown()) and main or UIParent, "CENTER", 0, 0)
	end
	return visible
end

function ShowStep(index)
	local def = running.current.def
	local step = def.steps[index]
	if not step then
		Finish(false)
		return
	end
	running.index = index
	if step.prepare then
		GP.Modules.Protect(step.prepare)
	end
	local target
	if step.target then
		local ok, frame = pcall(step.target)
		target = ok and frame or nil
	end
	local box = overlay.box
	box.module:SetText(def.title or "Guildkeep")
	box.counter:SetText(("%d / %d"):format(index, #def.steps))
	box.title:SetText(step.title or "")
	local visible = Place(target)
	local text = step.text or ""
	if step.target and not visible and step.hint then
		text = text .. "\n\n|cff9d9d9d" .. step.hint .. "|r"
	end
	box.text:SetText(text)
	box:SetHeight(math.max(150, box.text:GetStringHeight() + box.title:GetStringHeight() + 110))
	box.prev:SetEnabled(index > 1)
	box.next:SetText(index == #def.steps and "Terminer" or "Suivant")
	box.never:SetChecked(not T.IsEnabled())
	overlay:Show()
end

local function StartNext()
	local item = table.remove(running.queue, 1)
	-- Vu entre-temps (proposé deux fois) : sauté, sauf s'il est rejoué exprès.
	while item and not item.force and T.IsDone(item.id) do
		item = table.remove(running.queue, 1)
	end
	if not item then
		running = nil
		overlay:Hide()
		return
	end
	running.current = item
	ShowStep(1)
end

-- Fin du tutoriel en cours (terminé ou passé) : compté comme vu, puis le suivant de la file.
function Finish(skipped)
	if not running or not running.current then
		return
	end
	T.MarkDone(running.current.id)
	running.current = nil
	if skipped and not T.IsEnabled() then
		running.queue = {}
	end
	StartNext()
end

local function Queued(id)
	if running.current and running.current.id == id then
		return true
	end
	for _, item in ipairs(running.queue) do
		if item.id == id then
			return true
		end
	end
	return false
end

local function Run(list, force)
	if #list == 0 then
		return false
	end
	for _, item in ipairs(list) do
		item.force = force
	end
	overlay = overlay or CreateOverlay()
	if running then
		-- Déjà affiché ou en file : pas de doublon.
		for _, item in ipairs(list) do
			if not Queued(item.id) then
				running.queue[#running.queue + 1] = item
			end
		end
		return true
	end
	running = { queue = list, index = 0 }
	StartNext()
	return true
end

-- Appelé quand un onglet s'affiche : bienvenue (première ouverture), puis les tutoriels non vus de l'onglet.
function T.OnTabShown(tabKey)
	if not T.IsEnabled() then
		return
	end
	local pending = {}
	for _, item in ipairs(ForTab(nil)) do
		if not T.IsDone(item.id) then
			pending[#pending + 1] = item
		end
	end
	for _, item in ipairs(ForTab(tabKey)) do
		if not T.IsDone(item.id) then
			pending[#pending + 1] = item
		end
	end
	Run(pending)
end

-- Bouton « ? » et /gkeep tuto : rejoue les tutoriels de l'onglet affiché, même vus.
function T.Replay(tabKey)
	if running then
		running.queue = {}
		Finish(true)
	end
	return Run(ForTab(tabKey), true)
end

function T.IsRunning()
	return running ~= nil
end

-- Tutoriel et étape affichés : id, numéro d'étape, cible encadrée (ou nil).
function T.Current()
	if not running or not running.current then
		return nil
	end
	return running.current.id, running.index
end

function T.Next()
	if running and running.current then
		ShowStep(running.index + 1)
	end
end

function T.Previous()
	if running and running.current and running.index > 1 then
		ShowStep(running.index - 1)
	end
end

-- Passe le tutoriel affiché (compté comme vu) ; le suivant de la file prend la place.
function T.Skip()
	Finish(true)
end

-- Tutoriels déclarés et disponibles (modules actifs) : { id, def }.
function T.Registered()
	local list = {}
	for id, entry in pairs(registry) do
		if Available(entry) then
			list[#list + 1] = { id = id, def = entry.def }
		end
	end
	table.sort(list, function(a, b) return a.id < b.id end)
	return list
end

function T.Stop()
	if running then
		running.queue = {}
		Finish(true)
	end
end

function T.Slash(argument)
	local word = strtrim(argument or ""):lower()
	if word == "reset" or word == "revoir" then
		T.Reset()
		GP.Chat("tutoriels réinitialisés : chacun se relancera à l'ouverture de son onglet.")
	elseif word == "off" or word == "non" then
		T.SetEnabled(false)
		T.Stop()
		GP.Chat("tutoriels coupés. « /gkeep tuto on » pour les remettre.")
	elseif word == "on" or word == "oui" then
		T.SetEnabled(true)
		GP.Chat("tutoriels réactivés.")
	else
		GP.ShowTab(GP.UI.State.tab)
		if not T.Replay(GP.UI.State.tab) then
			GP.Chat("pas de tutoriel pour cet onglet.")
		end
	end
end

-- Tutoriel de bienvenue (socle) : la fenêtre, ses onglets, l'aide et les options.
T.Register("welcome", {
	title = "Bienvenue dans Guildkeep",
	order = 0,
	steps = {
		{
			title = "Ta guilde, en un seul endroit",
			text = "Guildkeep réunit les outils de ta guilde : métiers, commandes de craft, événements, présence, quêtes et communication. Ce petit tour te montre chaque bouton ; tu peux le passer à tout moment.",
		},
		{
			target = function() local f = GP.UI.GetMainFrame(); return f and f.Tabs and f.Tabs[1] end,
			title = "Les onglets",
			text = "Un onglet par module, en bas de la fenêtre. Un chiffre entre parenthèses signale ce qui t'attend (commande à traiter, événement sans réponse, annonce non lue).",
		},
		{
			target = function() local f = GP.UI.GetMainFrame(); return f and f.helpButton end,
			title = "Revoir un tutoriel",
			text = "Le bouton « ? » rejoue le tutoriel de l'onglet affiché. Aussi : /gkeep tuto. Pour tout revoir : /gkeep tuto reset ; pour les couper : /gkeep tuto off.",
		},
		{
			target = function() local f = GP.UI.GetMainFrame(); return f and f.importButton end,
			title = "Importer et Exporter",
			text = "Sans l'app compagnon, les données passent par une chaîne de texte : « Exporter » te donne la tienne à envoyer sur Discord (/gkeep import), « Importer » charge celle de la guilde (/gkeep export).",
		},
		{
			target = function() local f = GP.UI.GetMainFrame(); return f and f.banner end,
			title = "L'état des données",
			text = "Ici, la date des données de la guilde. Avec l'app compagnon, elles se mettent à jour toutes seules à chaque /reload ou connexion.",
		},
		{
			title = "Choisir ses modules",
			text = "Chaque module s'active ou se désactive : Options > Extensions > Guildkeep, ou /gkeep modules. Le changement prend effet au prochain /reload, et chaque module annonce son état dans le chat à la connexion.",
		},
	},
})
