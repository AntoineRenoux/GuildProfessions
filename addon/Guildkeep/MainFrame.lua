local _, GP = ...

-- Fenêtre principale (socle) : un onglet par module actif, ajouté par le module
-- à son chargement ; pied de fenêtre commun (import/export manuel, état des
-- données). La fenêtre s'ouvre même si un module a échoué : chaque onglet est
-- construit et rafraîchi à part, sous protection.

local M = GP.Modules
local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label
local state = UI.State

local mainFrame

-- Onglets ----------------------------------------------------------------------------------
-- Chaque onglet : label, key, order (place dans la barre), build(frame, panel)
-- construit le contenu une fois, refresh() le remplit, badge() (facultatif)
-- rend le compteur affiché dans le libellé et badgeHint(n) sa ligne
-- d'infobulle (onglet de la fenêtre de guilde).
local TABS = {}

-- Ajout d'un onglet par un module, avant la première ouverture de la fenêtre.
-- Rend la fonction qui le retire (module en échec).
function UI.AddTab(def, owner)
	def.owner = owner
	TABS[#TABS + 1] = def
	table.sort(TABS, function(a, b) return (a.order or 100) < (b.order or 100) end)
	return function()
		for i, other in ipairs(TABS) do
			if other == def then
				table.remove(TABS, i)
				return
			end
		end
	end
end

local function TabActive(def)
	return not def.owner or M.IsActive(def.owner)
end

local function TabIndex(key)
	for i, def in ipairs(TABS) do
		if def.key == key then
			return i
		end
	end
end

local function BadgeCount(def)
	if not def.badge or not TabActive(def) then
		return 0
	end
	local ok, count = pcall(def.badge)
	return ok and tonumber(count) or 0
end

-- Lignes « à traiter » des onglets (infobulle de l'onglet de guilde).
function UI.TabBadges()
	local lines = {}
	for _, def in ipairs(TABS) do
		local count = BadgeCount(def)
		if count > 0 and def.badgeHint then
			local ok, line = pcall(def.badgeHint, count)
			if ok and line then
				lines[#lines + 1] = line
			end
		end
	end
	return lines
end

function UI.GetMainFrame()
	return mainFrame
end

-- Import / export manuel ----------------------------------------------------------------------

local function OpenImportDialog()
	local dialog = UI.TextDialog()
	UI.SetWindowTitle(dialog, "Importer les données de la guilde")
	dialog.hint:SetText("Colle la chaîne du bot Discord (commande /gkeep exporter), puis Importer.")
	dialog.editBox:SetScript("OnTextChanged", nil)
	dialog.editBox:SetText("")
	dialog.status:SetText("")
	dialog.actionButton:SetText("Importer")
	dialog.actionButton:Show()
	dialog.actionButton:SetScript("OnClick", function()
		local ok, err = GP.ImportString(dialog.editBox:GetText())
		if ok then
			dialog.status:SetText(Paint(COLOR.positive, "Import réussi — données à jour."))
			dialog.editBox:SetText("")
		else
			dialog.status:SetText(Paint(COLOR.negative, err or "échec"))
		end
	end)
	dialog:Show()
	dialog.editBox:SetFocus()
end

local function OpenExportDialog()
	local dialog = UI.TextDialog()
	UI.SetWindowTitle(dialog, "Exporter mes données")
	dialog.hint:SetText("Ctrl+C pour copier, puis /gkeep importer sur Discord (ou donne la chaîne à un officier).")
	dialog.actionButton:Hide()
	local text, err = GP.BuildExportString()
	if text then
		dialog.status:SetText(Paint(COLOR.muted, ("%d caractères"):format(#text)))
		dialog.editBox:SetText(text)
		dialog.editBox:HighlightText()
		dialog.editBox:SetFocus()
		-- Toute frappe restaure la chaîne : la zone reste un presse-papiers.
		dialog.editBox:SetScript("OnTextChanged", function(self, userInput)
			if userInput then
				self:SetText(text)
				self:HighlightText()
			end
		end)
	else
		dialog.editBox:SetText("")
		dialog.status:SetText(Paint(COLOR.negative, err or "échec de l'export"))
	end
	dialog:Show()
end

-- Fenêtre principale ---------------------------------------------------------------------------

local function UpdateBanner()
	local guild = GP.GetPlayerGuildName()
	local server = GP.GetServerData()
	local text
	if not guild then
		text = Paint(COLOR.negative, "Ce personnage n'est dans aucune guilde : rien n'est relevé ni partagé.")
	elseif not GP.IsGuildRegistered() then
		text = Paint(COLOR.gold, ("« %s » n'utilise pas encore Guildkeep : un administrateur doit lancer /gkeep-setup sur Discord, puis partager le pack."):format(guild))
	elseif server.generatedUtc then
		text = "Données du " .. server.generatedUtc
	else
		text = "Données importées le " .. (server.importedAt or "?")
	end
	if server.mock then
		text = text .. " " .. Paint(COLOR.gold, "(démonstration)")
	end
	mainFrame.banner:SetText(text)
end

local function UpdateTabBadges()
	for i, def in ipairs(TABS) do
		local count = BadgeCount(def)
		local tab = mainFrame.Tabs[i]
		if tab then
			tab:SetText(count > 0 and ("%s (%d)"):format(def.label, count) or def.label)
			if PanelTemplates_TabResize then
				PanelTemplates_TabResize(tab, 0)
			end
		end
	end
end

local function SelectTab(index)
	local def = TABS[index]
	if not def then
		return
	end
	state.tab = def.key
	for _, other in ipairs(TABS) do
		if mainFrame[other.key] then
			mainFrame[other.key]:SetShown(other == def)
		end
	end
	PanelTemplates_SetTab(mainFrame, index)
	GP.RefreshUI()
	if mainFrame:IsShown() and GP.Tutorial then
		M.Protect(GP.Tutorial.OnTabShown, def.key)
	end
end

local function CreateMainFrame()
	local frame = UI.CreateWindow("GuildkeepFrame", 900, 580)
	mainFrame = frame
	UI.SetWindowTitle(frame, "Guildkeep")
	UI.SetWindowPortrait(frame, GP.ICON)

	local guildLine = Label(frame, nil, "GameFontHighlightSmall", COLOR.muted)
	guildLine:SetPoint("TOPLEFT", 66, -34)
	frame.guildLine = guildLine

	-- Pied de fenêtre : import/export manuel + état des données
	local importButton = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
	importButton:SetSize(96, 22)
	importButton:SetPoint("BOTTOMLEFT", 6, 4)
	importButton:SetText("Importer")
	importButton:SetScript("OnClick", OpenImportDialog)

	local exportButton = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
	exportButton:SetSize(96, 22)
	exportButton:SetPoint("LEFT", importButton, "RIGHT", 4, 0)
	exportButton:SetText("Exporter")
	exportButton:SetScript("OnClick", OpenExportDialog)
	frame.importButton, frame.exportButton = importButton, exportButton

	-- « ? » : rejoue le tutoriel de l'onglet affiché.
	frame.helpButton = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
	frame.helpButton:SetSize(26, 22)
	frame.helpButton:SetPoint("TOPRIGHT", -10, -28)
	frame.helpButton:SetText("?")
	frame.helpButton:SetScript("OnClick", function()
		if GP.Tutorial and not GP.Tutorial.Replay(state.tab) then
			GP.Chat("pas de tutoriel pour cet onglet.")
		end
	end)
	frame.helpButton:SetScript("OnEnter", function(self)
		GameTooltip:SetOwner(self, "ANCHOR_LEFT")
		GameTooltip:SetText("Tutoriel de cet onglet")
		GameTooltip:AddLine("Explique chaque bouton. Aussi : /gkeep tuto", 1, 1, 1, true)
		GameTooltip:Show()
	end)
	frame.helpButton:SetScript("OnLeave", GameTooltip_Hide)

	frame.banner = Label(frame, nil, "GameFontHighlightSmall", COLOR.muted)
	frame.banner:SetPoint("LEFT", exportButton, "RIGHT", 12, 0)
	frame.banner:SetPoint("RIGHT", -12, 0)
	frame.banner:SetJustifyH("RIGHT")
	frame.banner:SetWordWrap(false)

	-- Aucun module actif : la fenêtre explique comment en activer.
	frame.noModules = Label(frame.Inset, "Aucun module n'est activé.\nOptions > Extensions > Guildkeep (ou /gkeep modules), puis /reload.",
		"GameFontHighlight", COLOR.muted)
	frame.noModules:SetPoint("CENTER")
	frame.noModules:SetJustifyH("CENTER")
	frame.noModules:SetShown(#TABS == 0)

	-- Onglets natifs sous la fenêtre
	frame.Tabs = {}
	for i, def in ipairs(TABS) do
		local tab = CreateFrame("Button", "GuildkeepFrameTab" .. i, frame, "PanelTabButtonTemplate")
		tab:SetID(i)
		tab:SetText(def.label)
		if i == 1 then
			tab:SetPoint("TOPLEFT", frame, "BOTTOMLEFT", 12, 2)
		else
			tab:SetPoint("TOPLEFT", frame.Tabs[i - 1], "TOPRIGHT", 3, 0)
		end
		tab:SetScript("OnClick", function(self)
			PlaySound(SOUNDKIT.IG_CHARACTER_INFO_TAB)
			SelectTab(self:GetID())
		end)
		frame.Tabs[i] = tab
	end
	PanelTemplates_SetNumTabs(frame, #TABS)

	-- Chaque onglet naît masqué et se construit à part : si l'un échoue (API absente
	-- du client), l'erreur est signalée mais les autres restent utilisables et
	-- SelectTab n'affiche toujours que l'onglet choisi.
	local inset = frame.Inset
	for _, def in ipairs(TABS) do
		local panel = CreateFrame("Frame", nil, inset)
		panel:SetAllPoints()
		panel:Hide()
		frame[def.key] = panel
		M.Protect(def.build, frame, panel)
	end

	frame:HookScript("OnShow", function()
		PlaySound(SOUNDKIT.IG_CHARACTER_INFO_OPEN)
		-- Demande un roster frais (présence en ligne) ; le client limite la cadence.
		if C_GuildInfo and C_GuildInfo.GuildRoster then
			C_GuildInfo.GuildRoster()
		elseif GuildRoster then
			GuildRoster()
		end
		UI.RebuildGuildOnline()
		if GP.OnMainFrameVisibilityChanged then
			GP.OnMainFrameVisibilityChanged()
		end
		-- Première ouverture : bienvenue, puis le tutoriel de l'onglet affiché.
		C_Timer.After(0, function()
			if frame:IsShown() and GP.Tutorial then
				M.Protect(GP.Tutorial.OnTabShown, state.tab)
			end
		end)
	end)
	frame:HookScript("OnHide", function()
		PlaySound(SOUNDKIT.IG_CHARACTER_INFO_CLOSE)
		if GP.Tutorial then
			GP.Tutorial.Stop()
		end
		if GP.OnMainFrameVisibilityChanged then
			GP.OnMainFrameVisibilityChanged()
		end
	end)

	SelectTab(TabIndex(state.tab) or 1)
end

-- Place la fenêtre à droite d'un cadre (fenêtre de guilde), en coordonnées
-- absolues pour qu'elle reste déplaçable indépendamment.
local function PlaceNextTo(anchor)
	local right, top = anchor:GetRight(), anchor:GetTop()
	if not right or not top then
		return
	end
	local scale = anchor:GetEffectiveScale() / mainFrame:GetEffectiveScale()
	mainFrame:ClearAllPoints()
	mainFrame:SetPoint("TOPLEFT", UIParent, "BOTTOMLEFT", right * scale + 40, top * scale)
end

-- API publique -----------------------------------------------------------------------------------

function GP.IsMainFrameShown()
	return mainFrame ~= nil and mainFrame:IsShown()
end

-- Ouvre la fenêtre sur un onglet donné (clé de l'onglet), ex. depuis /gkeep events.
function GP.ShowTab(key)
	if not mainFrame then
		CreateMainFrame()
	end
	local index = TabIndex(key)
	if index then
		mainFrame:Show()
		SelectTab(index)
	end
end

function GP.ToggleMainFrame(anchor)
	if not mainFrame then
		CreateMainFrame()
	end
	if mainFrame:IsShown() then
		mainFrame:Hide()
	else
		if anchor then
			PlaceNextTo(anchor)
		end
		mainFrame:Show()
		GP.RefreshUI()
	end
end

function GP.RefreshUI()
	if not mainFrame or not mainFrame:IsShown() then
		return
	end
	local current = TABS[TabIndex(state.tab) or 0]
	if current and current.refresh and TabActive(current) then
		M.Protect(current.refresh)
	end
	local guild = GP.GetPlayerGuildName()
	mainFrame.guildLine:SetText(guild and ("<" .. guild .. ">  ·  gestion de guilde") or "gestion de guilde")
	UpdateTabBadges()
	UpdateBanner()
end

-- Événements du jeu -------------------------------------------------------------------------------

local events = CreateFrame("Frame")
-- Présence en ligne : le roster arrive en différé après la demande.
events:RegisterEvent("GUILD_ROSTER_UPDATE")
-- Changement de guilde (ou guilde connue tardivement à la connexion) : autres données.
events:RegisterEvent("PLAYER_GUILD_UPDATE")
-- Noms et icônes d'objets arrivés après coup : on redessine, une fois par rafale.
events:RegisterEvent("GET_ITEM_INFO_RECEIVED")
local itemRefreshPending = false
events:SetScript("OnEvent", function(_, event)
	if event == "GET_ITEM_INFO_RECEIVED" then
		if not itemRefreshPending then
			itemRefreshPending = true
			C_Timer.After(0.5, function()
				itemRefreshPending = false
				GP.RefreshUI()
			end)
		end
		return
	end
	UI.RebuildGuildOnline()
	GP.RefreshUI()
end)
