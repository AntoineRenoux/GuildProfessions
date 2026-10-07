local _, GP = ...

local ROW_HEIGHT = 26
local FILTER_WIDTH = 180

local mainFrame
local state = { tab = 1, profession = nil, minLevel = 0, search = "" }
local rowPool = {}
local profButtonPool = {}

-- Helpers ---------------------------------------------------------------------

local function ClassColor(classFile)
	local color = classFile and RAID_CLASS_COLORS and RAID_CLASS_COLORS[classFile]
	if color then
		return color.r, color.g, color.b
	end
	return 0.9, 0.9, 0.9
end

-- Construit la liste plate (1 ligne = 1 couple personnage/métier) selon les filtres.
local function BuildEntries()
	local entries = {}
	local search = string.lower(state.search or "")
	for _, char in ipairs(GP.GetRoster()) do
		for _, prof in ipairs(char.professions or {}) do
			local keep = true
			if state.profession and prof.name ~= state.profession then
				keep = false
			end
			if keep and (prof.level or 0) < state.minLevel then
				keep = false
			end
			local matched
			if keep and search ~= "" then
				keep = false
				for _, id in ipairs(prof.recipes or {}) do
					local recipeName = GP.GetRecipeName(id)
					if recipeName and string.lower(recipeName):find(search, 1, true) then
						keep = true
						matched = recipeName
						break
					end
				end
			end
			if keep then
				entries[#entries + 1] = { char = char, prof = prof, matched = matched }
			end
		end
	end
	table.sort(entries, function(a, b)
		if a.prof.name ~= b.prof.name then
			return a.prof.name < b.prof.name
		end
		if (a.prof.level or 0) ~= (b.prof.level or 0) then
			return (a.prof.level or 0) > (b.prof.level or 0)
		end
		return a.char.name < b.char.name
	end)
	return entries
end

local function ProfessionNames()
	local set, list = {}, {}
	for _, char in ipairs(GP.GetRoster()) do
		for _, prof in ipairs(char.professions or {}) do
			if not set[prof.name] then
				set[prof.name] = true
				list[#list + 1] = prof.name
			end
		end
	end
	table.sort(list)
	return list
end

-- Lignes de la liste ------------------------------------------------------------

local function AcquireRow(i, parent)
	local row = rowPool[i]
	if not row then
		row = CreateFrame("Frame", nil, parent)
		row:SetHeight(ROW_HEIGHT)

		row.name = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
		row.name:SetPoint("LEFT", 4, 0)
		row.name:SetWidth(120)
		row.name:SetJustifyH("LEFT")

		row.prof = row:CreateFontString(nil, "OVERLAY", "GameFontHighlight")
		row.prof:SetPoint("LEFT", row.name, "RIGHT", 6, 0)
		row.prof:SetWidth(110)
		row.prof:SetJustifyH("LEFT")

		row.bar = CreateFrame("StatusBar", nil, row)
		row.bar:SetPoint("LEFT", row.prof, "RIGHT", 6, 0)
		row.bar:SetSize(110, 14)
		row.bar:SetStatusBarTexture("Interface\\TargetingFrame\\UI-StatusBar")
		row.bar:SetStatusBarColor(0.2, 0.6, 1)
		local barBg = row.bar:CreateTexture(nil, "BACKGROUND")
		barBg:SetAllPoints()
		barBg:SetColorTexture(0, 0, 0, 0.4)
		row.bar.text = row.bar:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
		row.bar.text:SetPoint("CENTER")

		row.info = row:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
		row.info:SetPoint("LEFT", row.bar, "RIGHT", 10, 0)
		row.info:SetPoint("RIGHT", row, "RIGHT", -4, 0)
		row.info:SetJustifyH("LEFT")
		row.info:SetWordWrap(false)

		local stripe = row:CreateTexture(nil, "BACKGROUND")
		stripe:SetAllPoints()
		stripe:SetColorTexture(1, 1, 1, 0.03)
		row.stripe = stripe

		rowPool[i] = row
	end
	row:SetParent(parent)
	return row
end

local function RefreshList()
	local entries = BuildEntries()
	local content = mainFrame.listContent
	for _, row in ipairs(rowPool) do
		row:Hide()
	end
	for i, entry in ipairs(entries) do
		local row = AcquireRow(i, content)
		row:ClearAllPoints()
		row:SetPoint("TOPLEFT", content, "TOPLEFT", 0, -(i - 1) * ROW_HEIGHT)
		row:SetPoint("RIGHT", content, "RIGHT", 0, 0)
		row.stripe:SetShown(i % 2 == 0)

		row.name:SetText(entry.char.name)
		row.name:SetTextColor(ClassColor(entry.char.classFile))
		row.prof:SetText(entry.prof.name)

		local level, max = entry.prof.level or 0, entry.prof.max or 300
		if max <= 0 then
			max = 300
		end
		row.bar:SetMinMaxValues(0, max)
		row.bar:SetValue(level)
		row.bar.text:SetText(level .. " / " .. max)

		local info
		if entry.matched then
			info = "|cff80ff80" .. entry.matched .. "|r"
		elseif entry.prof.note and entry.prof.note ~= "" then
			info = entry.prof.note
		elseif entry.prof.recipes and #entry.prof.recipes > 0 then
			info = #entry.prof.recipes .. " recettes connues"
		else
			info = "—"
		end
		local source
		if entry.prof.source == "addon" then
			source = "|cff999999scan " .. (entry.prof.scannedAt or "") .. "|r"
		else
			source = "|cff999999Discord|r"
		end
		row.info:SetText(info .. "   " .. source)
		row:Show()
	end
	content:SetHeight(math.max(#entries * ROW_HEIGHT, 1))
	mainFrame.emptyText:SetShown(#entries == 0)
end

-- Filtres ------------------------------------------------------------------------

local function AcquireProfButton(i, parent)
	local button = profButtonPool[i]
	if not button then
		button = CreateFrame("Button", nil, parent)
		button:SetHeight(20)
		button.text = button:CreateFontString(nil, "OVERLAY", "GameFontNormal")
		button.text:SetPoint("LEFT", 6, 0)
		button.text:SetPoint("RIGHT", -2, 0)
		button.text:SetJustifyH("LEFT")
		local highlight = button:CreateTexture(nil, "HIGHLIGHT")
		highlight:SetAllPoints()
		highlight:SetColorTexture(1, 1, 1, 0.08)
		button:SetScript("OnClick", function(self)
			state.profession = self.value
			GP.RefreshUI()
		end)
		profButtonPool[i] = button
	end
	button:SetParent(parent)
	return button
end

local function RefreshProfessionButtons()
	local parent = mainFrame.filterPanel
	for _, button in ipairs(profButtonPool) do
		button:Hide()
	end
	local y = -54
	local index = 1
	local function addButton(value, label)
		local button = AcquireProfButton(index, parent)
		button:ClearAllPoints()
		button:SetPoint("TOPLEFT", parent, "TOPLEFT", 4, y)
		button:SetPoint("RIGHT", parent, "RIGHT", -4, 0)
		button.value = value
		local selected = state.profession == value
		button.text:SetText((selected and "|cffffd100> " or "|cffffffff") .. label .. "|r")
		button:Show()
		y = y - 20
		index = index + 1
	end
	addButton(nil, "Tous les métiers")
	for _, name in ipairs(ProfessionNames()) do
		addButton(name, name)
	end
end

local function UpdateBanner()
	local server = GP.GetServerData()
	local text
	if server.generatedUtc then
		text = "Données serveur du " .. server.generatedUtc .. " — lancez le compagnon pour mettre à jour."
	else
		text = "Aucune donnée serveur — installez et lancez l'app compagnon."
	end
	if server.mock then
		text = text .. " |cffffcc00(données de démonstration)|r"
	end
	mainFrame.banner:SetText(text)
end

-- Fenêtre principale ----------------------------------------------------------

local function SelectTab(tabIndex)
	state.tab = tabIndex
	mainFrame.tabProfessions:SetShown(tabIndex == 1)
	mainFrame.tabMembers:SetShown(tabIndex == 2)
	mainFrame.tabButton1:SetEnabled(tabIndex ~= 1)
	mainFrame.tabButton2:SetEnabled(tabIndex ~= 2)
	GP.RefreshUI()
end

local function CreateMainFrame()
	local frame = CreateFrame("Frame", "GuildProfessionsFrame", UIParent, "BackdropTemplate")
	frame:SetSize(860, 540)
	frame:SetPoint("CENTER")
	frame:SetFrameStrata("HIGH")
	frame:SetMovable(true)
	frame:EnableMouse(true)
	frame:RegisterForDrag("LeftButton")
	frame:SetScript("OnDragStart", frame.StartMoving)
	frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
	frame:SetBackdrop({
		bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background-Dark",
		edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
		edgeSize = 24,
		insets = { left = 6, right = 6, top = 6, bottom = 6 },
	})
	frame:Hide()
	tinsert(UISpecialFrames, "GuildProfessionsFrame") -- Échap ferme la fenêtre

	local title = frame:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
	title:SetPoint("TOP", 0, -14)
	title:SetText("|cff33ff99Guild|rProfessions")

	local close = CreateFrame("Button", nil, frame, "UIPanelCloseButton")
	close:SetPoint("TOPRIGHT", -4, -4)

	-- Onglets
	local tab1 = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
	tab1:SetSize(110, 24)
	tab1:SetPoint("TOPLEFT", 14, -38)
	tab1:SetText("Métiers")
	tab1:SetScript("OnClick", function() SelectTab(1) end)
	frame.tabButton1 = tab1

	local tab2 = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
	tab2:SetSize(110, 24)
	tab2:SetPoint("LEFT", tab1, "RIGHT", 6, 0)
	tab2:SetText("Membres")
	tab2:SetScript("OnClick", function() SelectTab(2) end)
	frame.tabButton2 = tab2

	-- Bandeau bas
	local banner = frame:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	banner:SetPoint("BOTTOM", 0, 14)
	banner:SetTextColor(0.7, 0.7, 0.7)
	frame.banner = banner

	-- === Onglet 1 : Métiers ===
	local tabProfessions = CreateFrame("Frame", nil, frame)
	tabProfessions:SetPoint("TOPLEFT", 10, -70)
	tabProfessions:SetPoint("BOTTOMRIGHT", -10, 34)
	frame.tabProfessions = tabProfessions

	-- Colonne de filtres
	local filterPanel = CreateFrame("Frame", nil, tabProfessions, "BackdropTemplate")
	filterPanel:SetWidth(FILTER_WIDTH)
	filterPanel:SetPoint("TOPLEFT")
	filterPanel:SetPoint("BOTTOMLEFT")
	filterPanel:SetBackdrop({ bgFile = "Interface\\Buttons\\WHITE8x8" })
	filterPanel:SetBackdropColor(0, 0, 0, 0.3)
	frame.filterPanel = filterPanel

	local filterTitle = filterPanel:CreateFontString(nil, "OVERLAY", "GameFontNormal")
	filterTitle:SetPoint("TOPLEFT", 8, -10)
	filterTitle:SetText("|cffffd100Filtres|r")

	local profLabel = filterPanel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	profLabel:SetPoint("TOPLEFT", 8, -34)
	profLabel:SetText("Métier")

	local levelLabel = filterPanel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	levelLabel:SetPoint("BOTTOMLEFT", filterPanel, "BOTTOMLEFT", 8, 76)
	levelLabel:SetText("Niveau minimum")

	local levelBox = CreateFrame("EditBox", nil, filterPanel, "InputBoxTemplate")
	levelBox:SetSize(60, 20)
	levelBox:SetPoint("TOPLEFT", levelLabel, "BOTTOMLEFT", 6, -4)
	levelBox:SetAutoFocus(false)
	levelBox:SetNumeric(true)
	levelBox:SetMaxLetters(3)
	levelBox:SetScript("OnTextChanged", function(self)
		state.minLevel = tonumber(self:GetText()) or 0
		RefreshList()
	end)
	levelBox:SetScript("OnEnterPressed", levelBox.ClearFocus)
	levelBox:SetScript("OnEscapePressed", levelBox.ClearFocus)

	local searchLabel = filterPanel:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	searchLabel:SetPoint("BOTTOMLEFT", filterPanel, "BOTTOMLEFT", 8, 34)
	searchLabel:SetText("Recherche de patron")

	local searchBox = CreateFrame("EditBox", nil, filterPanel, "InputBoxTemplate")
	searchBox:SetSize(FILTER_WIDTH - 24, 20)
	searchBox:SetPoint("TOPLEFT", searchLabel, "BOTTOMLEFT", 6, -4)
	searchBox:SetAutoFocus(false)
	searchBox:SetScript("OnTextChanged", function(self)
		state.search = self:GetText() or ""
		RefreshList()
	end)
	searchBox:SetScript("OnEnterPressed", searchBox.ClearFocus)
	searchBox:SetScript("OnEscapePressed", function(self)
		self:SetText("")
		self:ClearFocus()
	end)

	-- En-têtes de colonnes
	local header = tabProfessions:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
	header:SetPoint("TOPLEFT", filterPanel, "TOPRIGHT", 14, -2)
	header:SetText("|cffffd100Personnage          Métier               Niveau              Infos|r")

	-- Liste scrollable
	local scroll = CreateFrame("ScrollFrame", nil, tabProfessions, "UIPanelScrollFrameTemplate")
	scroll:SetPoint("TOPLEFT", filterPanel, "TOPRIGHT", 10, -20)
	scroll:SetPoint("BOTTOMRIGHT", -26, 0)

	local content = CreateFrame("Frame", nil, scroll)
	content:SetSize(1, 1)
	scroll:SetScrollChild(content)
	scroll:SetScript("OnSizeChanged", function(_, width)
		content:SetWidth(width)
	end)
	frame.listContent = content

	local emptyText = tabProfessions:CreateFontString(nil, "OVERLAY", "GameFontDisable")
	emptyText:SetPoint("CENTER", scroll, "CENTER")
	emptyText:SetText("Aucun résultat avec ces filtres.")
	emptyText:Hide()
	frame.emptyText = emptyText

	-- === Onglet 2 : Membres (étape 5) ===
	local tabMembers = CreateFrame("Frame", nil, frame)
	tabMembers:SetPoint("TOPLEFT", 10, -70)
	tabMembers:SetPoint("BOTTOMRIGHT", -10, 34)
	tabMembers:Hide()
	frame.tabMembers = tabMembers

	local membersPlaceholder = tabMembers:CreateFontString(nil, "OVERLAY", "GameFontDisableLarge")
	membersPlaceholder:SetPoint("CENTER")
	membersPlaceholder:SetText("Onglet Membres — prévu à l'étape 5\n(liste du roster + rendu 3D de l'équipement)")

	mainFrame = frame
	SelectTab(1)
end

-- API publique -------------------------------------------------------------------

function GP.ToggleMainFrame()
	if not mainFrame then
		CreateMainFrame()
	end
	if mainFrame:IsShown() then
		mainFrame:Hide()
	else
		mainFrame:Show()
		GP.RefreshUI()
	end
end

function GP.RefreshUI()
	if not mainFrame or not mainFrame:IsShown() then
		return
	end
	if state.tab == 1 then
		RefreshProfessionButtons()
		RefreshList()
	end
	UpdateBanner()
end
