local _, GP = ...

-- Interface de l'addon, bâtie sur les gabarits natifs du client Mainline
-- (ButtonFrameTemplate, PanelTabButtonTemplate, ScrollBox, SearchBoxTemplate,
-- ItemButtonTemplate) pour se fondre dans l'UI Blizzard.

GP.ICON = "Interface\\Icons\\INV_Misc_Gear_01"

-- Thème : couleurs sémantiques, jamais de valeurs brutes dans les composants.
local COLOR = {
	gold = { 1, 0.82, 0 },
	text = { 1, 1, 1 },
	muted = { 0.62, 0.62, 0.62 },
	positive = { 0.25, 1, 0.4 },
	negative = { 1, 0.35, 0.35 },
	info = { 0.4, 0.65, 1 },
	bar = { 0.2, 0.55, 1 },
	barMax = { 0.25, 0.8, 0.35 },
	stripe = { 1, 1, 1, 0.035 },
	hover = { 1, 1, 1, 0.08 },
	selected = { 1, 0.82, 0, 0.14 },
	panel = { 0, 0, 0, 0.25 },
}

local function Hex(c)
	return ("|cff%02x%02x%02x"):format(math.floor(c[1] * 255), math.floor(c[2] * 255), math.floor(c[3] * 255))
end

local function Paint(color, text)
	return Hex(color) .. text .. "|r"
end

local state = {
	tab = 1,
	profession = nil,
	minLevel = 0,
	search = "",
	sortKey = "prof",
	sortAsc = true,
	memberSearch = "",
	ordersFilter = "all",
	showClosed = false,
}

local mainFrame

-- Données de présentation ----------------------------------------------------------

-- Noms localisés (frFR + enUS) → icône. Le scan local fournit l'icône exacte,
-- cette table couvre les métiers venus du serveur.
local PROFESSION_ICONS = {
	["forge"] = "Trade_BlackSmithing", ["blacksmithing"] = "Trade_BlackSmithing",
	["travail du cuir"] = "INV_Misc_ArmorKit_17", ["leatherworking"] = "INV_Misc_ArmorKit_17",
	["couture"] = "Trade_Tailoring", ["tailoring"] = "Trade_Tailoring",
	["ingénierie"] = "Trade_Engineering", ["engineering"] = "Trade_Engineering",
	["alchimie"] = "Trade_Alchemy", ["alchemy"] = "Trade_Alchemy",
	["enchantement"] = "Trade_Engraving", ["enchanting"] = "Trade_Engraving",
	["minage"] = "Trade_Mining", ["mining"] = "Trade_Mining",
	["herboristerie"] = "Trade_Herbalism", ["herbalism"] = "Trade_Herbalism",
	["dépeçage"] = "INV_Misc_Pelt_Wolf_01", ["skinning"] = "INV_Misc_Pelt_Wolf_01",
	["cuisine"] = "INV_Misc_Food_15", ["cooking"] = "INV_Misc_Food_15",
	["secourisme"] = "Spell_Holy_SealOfSacrifice", ["first aid"] = "Spell_Holy_SealOfSacrifice",
	["pêche"] = "Trade_Fishing", ["fishing"] = "Trade_Fishing",
	["joaillerie"] = "INV_Misc_Gem_01", ["jewelcrafting"] = "INV_Misc_Gem_01",
	["calligraphie"] = "INV_Inscription_Tradeskill01", ["inscription"] = "INV_Inscription_Tradeskill01",
}

function GP.GetProfessionIcon(prof)
	if prof.icon then
		return prof.icon
	end
	local file = PROFESSION_ICONS[string.lower(prof.name or "")]
	return "Interface\\Icons\\" .. (file or "INV_Misc_QuestionMark")
end

-- Rangs vanilla, déduits du plafond du métier.
local function RankName(max)
	if not max or max <= 0 then
		return nil
	elseif max <= 75 then
		return "Apprenti"
	elseif max <= 150 then
		return "Compagnon"
	elseif max <= 225 then
		return "Expert"
	end
	return "Artisan"
end

local function ClassColor(classFile)
	local color = classFile and RAID_CLASS_COLORS and RAID_CLASS_COLORS[classFile]
	if color then
		return color.r, color.g, color.b
	end
	return 0.9, 0.9, 0.9
end

local CLASS_ICON_FILE = "Interface\\GLUES\\CHARACTERCREATE\\UI-CHARACTERCREATE-CLASSES"

local function SetClassIcon(texture, classFile)
	if not classFile then
		texture:Hide()
		return
	end
	local atlas = "classicon-" .. string.lower(classFile)
	if C_Texture and C_Texture.GetAtlasInfo and C_Texture.GetAtlasInfo(atlas) then
		texture:SetTexCoord(0, 1, 0, 1)
		texture:SetAtlas(atlas)
		texture:Show()
		return
	end
	local coords = CLASS_ICON_TCOORDS and CLASS_ICON_TCOORDS[classFile]
	if coords then
		texture:SetTexture(CLASS_ICON_FILE)
		texture:SetTexCoord(unpack(coords))
		texture:Show()
	else
		texture:Hide()
	end
end

-- Présence en ligne, d'après le roster de guilde du client ------------------------

local guildOnline = {}

local function RebuildGuildOnline()
	wipe(guildOnline)
	if not IsInGuild() or not GetNumGuildMembers or not GetGuildRosterInfo then
		return
	end
	for i = 1, GetNumGuildMembers() do
		local name, _, _, _, _, _, _, _, online = GetGuildRosterInfo(i)
		if name then
			-- « Prénom-Nom » : le nom de famille occupe le slot royaume.
			guildOnline[(name:gsub("%-", " ", 1))] = online and true or false
		end
	end
end

-- true, false, ou nil si le membre n'apparaît pas dans le roster du client.
local function IsOnline(name)
	return guildOnline[name]
end

local function SetOnlineIcon(texture, name)
	local online = IsOnline(name)
	if online == nil then
		texture:Hide()
		return
	end
	texture:SetTexture(online and "Interface\\FriendsFrame\\StatusIcon-Online" or "Interface\\FriendsFrame\\StatusIcon-Offline")
	texture:Show()
end

local function AddOnlineLine(name)
	local online = IsOnline(name)
	if online == true then
		GameTooltip:AddLine("En ligne", unpack(COLOR.positive))
	elseif online == false then
		GameTooltip:AddLine("Hors ligne", unpack(COLOR.muted))
	end
end

-- Briques d'interface ----------------------------------------------------------------

local function SetWindowTitle(frame, text)
	if frame.SetTitle then
		frame:SetTitle(text)
	elseif frame.TitleContainer and frame.TitleContainer.TitleText then
		frame.TitleContainer.TitleText:SetText(text)
	elseif frame.TitleText then
		frame.TitleText:SetText(text)
	end
end

local function SetWindowPortrait(frame, icon)
	if frame.SetPortraitToAsset then
		frame:SetPortraitToAsset(icon)
		return
	end
	local portrait = (frame.PortraitContainer and frame.PortraitContainer.portrait) or frame.portrait
	if portrait then
		if SetPortraitToTexture then
			SetPortraitToTexture(portrait, icon)
		else
			portrait:SetTexture(icon)
		end
	end
end

local function CreateWindow(name, width, height, strata)
	local frame = CreateFrame("Frame", name, UIParent, "ButtonFrameTemplate")
	frame:SetSize(width, height)
	frame:SetPoint("CENTER")
	frame:SetFrameStrata(strata or "HIGH")
	frame:SetToplevel(true)
	frame:SetClampedToScreen(true)
	frame:SetMovable(true)
	frame:EnableMouse(true)
	frame:RegisterForDrag("LeftButton")
	frame:SetScript("OnDragStart", frame.StartMoving)
	frame:SetScript("OnDragStop", frame.StopMovingOrSizing)
	frame:Hide()
	tinsert(UISpecialFrames, name) -- Échap ferme la fenêtre
	return frame
end

local function Label(parent, text, template, color)
	local fs = parent:CreateFontString(nil, "OVERLAY", template or "GameFontNormalSmall")
	fs:SetJustifyH("LEFT")
	if text then
		fs:SetText(text)
	end
	if color then
		fs:SetTextColor(unpack(color))
	end
	return fs
end

local function AddRowBackground(row)
	row.stripe = row:CreateTexture(nil, "BACKGROUND")
	row.stripe:SetAllPoints()
	row.stripe:SetColorTexture(unpack(COLOR.stripe))
	row.selectedTex = row:CreateTexture(nil, "BACKGROUND", nil, 1)
	row.selectedTex:SetAllPoints()
	row.selectedTex:SetColorTexture(unpack(COLOR.selected))
	row.selectedTex:Hide()
	local highlight = row:CreateTexture(nil, "HIGHLIGHT")
	highlight:SetAllPoints()
	highlight:SetColorTexture(unpack(COLOR.hover))
end

local function CreateSearchBox(parent, width, instructions, onChange)
	local box = CreateFrame("EditBox", nil, parent, "SearchBoxTemplate")
	box:SetSize(width, 20)
	box:SetAutoFocus(false)
	if box.Instructions then
		box.Instructions:SetText(instructions)
	end
	box:SetScript("OnTextChanged", function(self, userInput)
		if SearchBoxTemplate_OnTextChanged then
			SearchBoxTemplate_OnTextChanged(self)
		end
		onChange(self:GetText() or "", userInput)
	end)
	return box
end

-- Liste virtualisée (ScrollBox) : `setup` construit une ligne une seule fois,
-- `update` la remplit à chaque recyclage.
local function CreateScrollList(parent, extent, setup, update)
	local box = CreateFrame("Frame", nil, parent, "WowScrollBoxList")
	local bar = CreateFrame("EventFrame", nil, parent, "MinimalScrollBar")
	bar:SetPoint("TOPLEFT", box, "TOPRIGHT", 6, 0)
	bar:SetPoint("BOTTOMLEFT", box, "BOTTOMRIGHT", 6, 0)
	local view = CreateScrollBoxListLinearView()
	view:SetElementExtent(extent)
	view:SetElementInitializer("Button", function(row, data)
		if not row.gpReady then
			setup(row)
			row.gpReady = true
		end
		update(row, data)
	end)
	ScrollUtil.InitScrollBoxListWithScrollBar(box, bar, view)
	if ScrollUtil.AddManagedScrollBarVisibilityBehavior then
		ScrollUtil.AddManagedScrollBarVisibilityBehavior(box, bar)
	end
	function box:GPSetItems(items)
		self:SetDataProvider(CreateDataProvider(items), ScrollBoxConstants.RetainScrollPosition)
	end
	return box
end

local function SetCheckLabel(check, text)
	local label = check.Text or check.text
	if label then
		label:SetText(text)
		label:SetFontObject("GameFontHighlightSmall")
	end
end

-- Onglet Métiers ----------------------------------------------------------------------

local FILTER_WIDTH = 190
local ROW_HEIGHT = 26
local COL = { icon = 6, name = 32, prof = 200, level = 326, recipes = 452, source = 520 }

local professionsList
local profButtonPool = {}

local function RecipeCount(prof)
	return prof.recipes and #prof.recipes or 0
end

local function CompareEntries(a, b)
	local key, asc = state.sortKey, state.sortAsc
	local va, vb
	if key == "name" then
		va, vb = a.char.name, b.char.name
	elseif key == "level" then
		va, vb = a.prof.level or 0, b.prof.level or 0
	elseif key == "recipes" then
		va, vb = a.recipeCount, b.recipeCount
	else
		va, vb = a.prof.name, b.prof.name
	end
	if va ~= vb then
		if asc then
			return va < vb
		end
		return va > vb
	end
	-- Départage stable : métier, niveau décroissant, nom.
	if a.prof.name ~= b.prof.name then
		return a.prof.name < b.prof.name
	end
	if (a.prof.level or 0) ~= (b.prof.level or 0) then
		return (a.prof.level or 0) > (b.prof.level or 0)
	end
	return a.char.name < b.char.name
end

-- Liste plate (1 ligne = 1 couple personnage/métier) selon les filtres.
local function BuildEntries()
	local entries = {}
	local search = string.lower(state.search or "")
	for _, char in ipairs(GP.GetRoster()) do
		for _, prof in ipairs(char.professions or {}) do
			local keep = not state.profession or prof.name == state.profession
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
				entries[#entries + 1] = { char = char, prof = prof, matched = matched, recipeCount = RecipeCount(prof) }
			end
		end
	end
	table.sort(entries, CompareEntries)
	for i, entry in ipairs(entries) do
		entry.index = i
	end
	return entries
end

local function ProfessionCounts()
	local counts, names = {}, {}
	for _, char in ipairs(GP.GetRoster()) do
		for _, prof in ipairs(char.professions or {}) do
			if not counts[prof.name] then
				counts[prof.name] = { count = 0, icon = GP.GetProfessionIcon(prof) }
				names[#names + 1] = prof.name
			end
			counts[prof.name].count = counts[prof.name].count + 1
		end
	end
	table.sort(names)
	return names, counts
end

local function ProfessionRowTooltip(row)
	local entry = row.entry
	local prof, char = entry.prof, entry.char
	GameTooltip:SetOwner(row, "ANCHOR_RIGHT")
	GameTooltip:SetText(char.name, ClassColor(char.classFile))
	local rank = RankName(prof.max)
	GameTooltip:AddLine(("%s %d / %d%s"):format(prof.name, prof.level or 0, prof.max or 0,
		rank and (" — " .. rank) or ""), 1, 1, 1)
	AddOnlineLine(char.name)
	if prof.source == "addon" then
		GameTooltip:AddLine("Relevé en jeu le " .. (prof.scannedAt or "?"), unpack(COLOR.muted))
	else
		GameTooltip:AddLine("Déclaré sur Discord", unpack(COLOR.muted))
	end
	if prof.note and prof.note ~= "" then
		GameTooltip:AddLine(prof.note, 0.9, 0.9, 0.9, true)
	end
	GameTooltip:AddLine(" ")
	if entry.recipeCount > 0 then
		GameTooltip:AddLine(("Clic : voir ses %d recettes"):format(entry.recipeCount), unpack(COLOR.positive))
		if char.name ~= GP.GetPlayerFullName() then
			GameTooltip:AddLine("et les lui commander", unpack(COLOR.positive))
		end
	else
		GameTooltip:AddLine("Aucune recette relevée pour l'instant.", unpack(COLOR.muted))
	end
	GameTooltip:Show()
end

local function SetupProfessionRow(row)
	AddRowBackground(row)
	row:RegisterForClicks("LeftButtonUp")

	row.icon = row:CreateTexture(nil, "ARTWORK")
	row.icon:SetSize(20, 20)
	row.icon:SetPoint("LEFT", COL.icon, 0)
	row.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)

	row.online = row:CreateTexture(nil, "ARTWORK")
	row.online:SetSize(14, 14)
	row.online:SetPoint("LEFT", COL.name, 0)

	row.name = Label(row, nil, "GameFontNormal")
	row.name:SetPoint("LEFT", COL.name + 16, 0)
	row.name:SetWidth(COL.prof - COL.name - 22)
	row.name:SetWordWrap(false)

	row.prof = Label(row, nil, "GameFontHighlight")
	row.prof:SetPoint("LEFT", COL.prof, 0)
	row.prof:SetWidth(COL.level - COL.prof - 6)
	row.prof:SetWordWrap(false)

	row.bar = CreateFrame("StatusBar", nil, row)
	row.bar:SetPoint("LEFT", COL.level, 0)
	row.bar:SetSize(COL.recipes - COL.level - 14, 14)
	row.bar:SetStatusBarTexture("Interface\\TargetingFrame\\UI-StatusBar")
	local barBg = row.bar:CreateTexture(nil, "BACKGROUND")
	barBg:SetAllPoints()
	barBg:SetColorTexture(0, 0, 0, 0.5)
	row.bar.text = Label(row.bar, nil, "GameFontHighlightSmall")
	row.bar.text:SetPoint("CENTER")

	row.recipes = Label(row, nil, "GameFontHighlightSmall")
	row.recipes:SetPoint("LEFT", COL.recipes, 0)
	row.recipes:SetWidth(COL.source - COL.recipes - 6)

	row.source = Label(row, nil, "GameFontHighlightSmall")
	row.source:SetPoint("LEFT", COL.source, 0)
	row.source:SetPoint("RIGHT", -6, 0)
	row.source:SetWordWrap(false)

	row:SetScript("OnEnter", ProfessionRowTooltip)
	row:SetScript("OnLeave", GameTooltip_Hide)
	row:SetScript("OnClick", function(self)
		if self.entry.recipeCount > 0 then
			GP.OpenRecipesDialog(self.entry.char, self.entry.prof)
		end
	end)
end

local function UpdateProfessionRow(row, entry)
	row.entry = entry
	local char, prof = entry.char, entry.prof
	row.stripe:SetShown(entry.index % 2 == 0)
	row.icon:SetTexture(GP.GetProfessionIcon(prof))
	SetOnlineIcon(row.online, char.name)
	row.name:SetText(char.name)
	row.name:SetTextColor(ClassColor(char.classFile))
	row.prof:SetText(prof.name)

	local level, max = prof.level or 0, prof.max or 300
	if max <= 0 then
		max = 300
	end
	row.bar:SetMinMaxValues(0, max)
	row.bar:SetValue(level)
	row.bar:SetStatusBarColor(unpack(level >= max and COLOR.barMax or COLOR.bar))
	row.bar.text:SetText(level .. " / " .. max)

	if entry.recipeCount > 0 then
		row.recipes:SetText(Paint(COLOR.gold, entry.recipeCount))
	else
		row.recipes:SetText(Paint(COLOR.muted, "—"))
	end

	if entry.matched then
		row.source:SetText(Paint(COLOR.positive, entry.matched))
	elseif prof.source == "addon" then
		row.source:SetText(Paint(COLOR.muted, "scan " .. (prof.scannedAt or "")))
	else
		row.source:SetText(Paint(COLOR.muted, "Discord"))
	end
end

local function RefreshProfessionButtons()
	local parent = mainFrame.filterPanel
	for _, button in ipairs(profButtonPool) do
		button:Hide()
	end
	local names, counts = ProfessionCounts()
	local index, y = 1, mainFrame.profListTop
	local function addButton(value, label, icon, count)
		local button = profButtonPool[index]
		if not button then
			button = CreateFrame("Button", nil, parent)
			button:SetHeight(22)
			AddRowBackground(button)
			button.stripe:Hide()
			button.marker = button:CreateTexture(nil, "ARTWORK")
			button.marker:SetColorTexture(unpack(COLOR.gold))
			button.marker:SetPoint("TOPLEFT")
			button.marker:SetPoint("BOTTOMLEFT")
			button.marker:SetWidth(2)
			button.icon = button:CreateTexture(nil, "ARTWORK")
			button.icon:SetSize(16, 16)
			button.icon:SetPoint("LEFT", 8, 0)
			button.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)
			button.count = Label(button, nil, "GameFontHighlightSmall", COLOR.muted)
			button.count:SetPoint("RIGHT", -6, 0)
			button.text = Label(button, nil, "GameFontHighlightSmall")
			button.text:SetPoint("LEFT", button.icon, "RIGHT", 6, 0)
			button.text:SetPoint("RIGHT", button.count, "LEFT", -4, 0)
			button.text:SetWordWrap(false)
			button:SetScript("OnClick", function(self)
				state.profession = self.value
				PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
				GP.RefreshUI()
			end)
			profButtonPool[index] = button
		end
		button:ClearAllPoints()
		button:SetPoint("TOPLEFT", parent, "TOPLEFT", 4, y)
		button:SetPoint("RIGHT", parent, "RIGHT", -4, 0)
		button.value = value
		local selected = state.profession == value
		button.selectedTex:SetShown(selected)
		button.marker:SetShown(selected)
		button.text:SetText(label)
		button.text:SetTextColor(unpack(selected and COLOR.gold or COLOR.text))
		button.icon:SetTexture(icon)
		button.count:SetText(count or "")
		button:Show()
		index = index + 1
		y = y - 22
	end
	local total = 0
	for _, name in ipairs(names) do
		total = total + counts[name].count
	end
	addButton(nil, "Tous les métiers", GP.ICON, total)
	for _, name in ipairs(names) do
		addButton(name, name, counts[name].icon, counts[name].count)
	end
end

local sortHeaders = {}

local function UpdateSortHeaders()
	for _, header in ipairs(sortHeaders) do
		local active = header.key == state.sortKey
		header.arrow:SetShown(active)
		if active then
			if state.sortAsc then
				header.arrow:SetTexCoord(0, 0.5625, 1, 0)
			else
				header.arrow:SetTexCoord(0, 0.5625, 0, 1)
			end
		end
	end
end

local function CreateSortHeader(parent, key, text, x, width)
	local header = CreateFrame("Button", nil, parent)
	header:SetSize(width, 18)
	header:SetPoint("TOPLEFT", x, 0)
	header.key = key
	header.label = Label(header, text, "GameFontNormalSmall")
	header.label:SetPoint("LEFT")
	header.arrow = header:CreateTexture(nil, "ARTWORK")
	header.arrow:SetTexture("Interface\\Buttons\\UI-SortArrow")
	header.arrow:SetSize(9, 8)
	header.arrow:SetPoint("LEFT", header.label, "RIGHT", 3, -1)
	local highlight = header:CreateTexture(nil, "HIGHLIGHT")
	highlight:SetAllPoints()
	highlight:SetColorTexture(unpack(COLOR.hover))
	header:SetScript("OnClick", function(self)
		if state.sortKey == self.key then
			state.sortAsc = not state.sortAsc
		else
			state.sortKey = self.key
			-- Texte : A→Z ; nombres : du plus grand au plus petit.
			state.sortAsc = self.key == "name" or self.key == "prof"
		end
		PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		GP.RefreshUI()
	end)
	sortHeaders[#sortHeaders + 1] = header
	return header
end

local function RefreshProfessions()
	local entries = BuildEntries()
	professionsList:GPSetItems(entries)
	local empty = #entries == 0
	mainFrame.emptyText:SetShown(empty)
	mainFrame.emptyHint:SetShown(empty)
	if empty then
		if #GP.GetRoster() == 0 then
			mainFrame.emptyText:SetText("Aucune donnée de guilde pour l'instant.")
			mainFrame.emptyHint:SetText("Lance l'app compagnon, ou colle la chaîne du bot avec le bouton Importer.")
		else
			mainFrame.emptyText:SetText("Aucun artisan ne correspond à ces filtres.")
			mainFrame.emptyHint:SetText("Efface la recherche, baisse le niveau minimum ou choisis « Tous les métiers ».")
		end
	end
	RefreshProfessionButtons()
	UpdateSortHeaders()
end

local function BuildProfessionsTab(frame, parent)
	local filterPanel = CreateFrame("Frame", nil, parent)
	filterPanel:SetWidth(FILTER_WIDTH)
	filterPanel:SetPoint("TOPLEFT", 4, -4)
	filterPanel:SetPoint("BOTTOMLEFT", 4, 4)
	local filterBg = filterPanel:CreateTexture(nil, "BACKGROUND")
	filterBg:SetAllPoints()
	filterBg:SetColorTexture(unpack(COLOR.panel))
	frame.filterPanel = filterPanel

	local searchLabel = Label(filterPanel, "Recherche de patron")
	searchLabel:SetPoint("TOPLEFT", 10, -10)
	local searchBox = CreateSearchBox(filterPanel, FILTER_WIDTH - 26, "Ex. : bottes, potion…", function(text)
		state.search = text
		if professionsList then
			RefreshProfessions()
		end
	end)
	searchBox:SetPoint("TOPLEFT", 16, -26)

	local levelLabel = Label(filterPanel, "Niveau minimum")
	levelLabel:SetPoint("TOPLEFT", 10, -58)
	local levelBox = CreateFrame("EditBox", nil, filterPanel, "InputBoxTemplate")
	levelBox:SetSize(46, 20)
	levelBox:SetPoint("TOPLEFT", 16, -74)
	levelBox:SetAutoFocus(false)
	levelBox:SetNumeric(true)
	levelBox:SetMaxLetters(3)
	levelBox:SetJustifyH("CENTER")
	levelBox:SetScript("OnTextChanged", function(self)
		state.minLevel = tonumber(self:GetText()) or 0
		if professionsList then
			RefreshProfessions()
		end
	end)
	levelBox:SetScript("OnEnterPressed", levelBox.ClearFocus)
	levelBox:SetScript("OnEscapePressed", levelBox.ClearFocus)
	local levelHint = Label(filterPanel, "vide = tous", "GameFontHighlightSmall", COLOR.muted)
	levelHint:SetPoint("LEFT", levelBox, "RIGHT", 8, 0)

	local profLabel = Label(filterPanel, "Métier")
	profLabel:SetPoint("TOPLEFT", 10, -106)
	frame.profListTop = -122

	-- Zone liste : en-têtes triables + ScrollBox
	local listArea = CreateFrame("Frame", nil, parent)
	listArea:SetPoint("TOPLEFT", filterPanel, "TOPRIGHT", 8, -4)
	listArea:SetPoint("BOTTOMRIGHT", -22, 4)

	local headerBar = CreateFrame("Frame", nil, listArea)
	headerBar:SetPoint("TOPLEFT")
	headerBar:SetPoint("TOPRIGHT")
	headerBar:SetHeight(18)
	CreateSortHeader(headerBar, "name", "Personnage", COL.name + 16, COL.prof - COL.name - 22)
	CreateSortHeader(headerBar, "prof", "Métier", COL.prof, COL.level - COL.prof - 6)
	CreateSortHeader(headerBar, "level", "Niveau", COL.level, COL.recipes - COL.level - 14)
	CreateSortHeader(headerBar, "recipes", "Recettes", COL.recipes, COL.source - COL.recipes - 6)
	local infoHeader = Label(headerBar, "Infos")
	infoHeader:SetPoint("LEFT", COL.source, 0)
	local divider = headerBar:CreateTexture(nil, "ARTWORK")
	divider:SetColorTexture(1, 0.82, 0, 0.25)
	divider:SetHeight(1)
	divider:SetPoint("BOTTOMLEFT", 0, -2)
	divider:SetPoint("BOTTOMRIGHT", 0, -2)

	professionsList = CreateScrollList(listArea, ROW_HEIGHT, SetupProfessionRow, UpdateProfessionRow)
	professionsList:SetPoint("TOPLEFT", headerBar, "BOTTOMLEFT", 0, -4)
	professionsList:SetPoint("BOTTOMRIGHT", listArea, "BOTTOMRIGHT")

	frame.emptyText = Label(listArea, nil, "GameFontNormal")
	frame.emptyText:SetPoint("CENTER", professionsList, "CENTER", 0, 10)
	frame.emptyText:SetJustifyH("CENTER")
	frame.emptyHint = Label(listArea, nil, "GameFontHighlightSmall", COLOR.muted)
	frame.emptyHint:SetPoint("TOP", frame.emptyText, "BOTTOM", 0, -6)
	frame.emptyHint:SetWidth(380)
	frame.emptyHint:SetJustifyH("CENTER")
end

-- Fenêtre « Recettes connues » ----------------------------------------------------------

local recipesDialog

local function RecipeIcon(id)
	if C_Spell and C_Spell.GetSpellTexture then
		return C_Spell.GetSpellTexture(id)
	end
	return GetSpellTexture and GetSpellTexture(id)
end

local function SetupRecipeRow(row)
	AddRowBackground(row)
	row:RegisterForClicks("LeftButtonUp")
	row.icon = row:CreateTexture(nil, "ARTWORK")
	row.icon:SetSize(18, 18)
	row.icon:SetPoint("LEFT", 6, 0)
	row.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)
	row.orderButton = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
	row.orderButton:SetSize(90, 20)
	row.orderButton:SetPoint("RIGHT", -4, 0)
	row.orderButton:SetText("Commander")
	row.orderButton:SetScript("OnClick", function(self)
		GP.OpenOrderDialog(recipesDialog.crafterName, self:GetParent().data.name, self:GetParent().data.icon)
	end)
	row.text = Label(row, nil, "GameFontHighlight")
	row.text:SetPoint("LEFT", row.icon, "RIGHT", 6, 0)
	row.text:SetPoint("RIGHT", row.orderButton, "LEFT", -6, 0)
	row.text:SetWordWrap(false)
	row:SetScript("OnEnter", function(self)
		GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
		-- Tooltip natif de la recette ; repli sur le nom si l'API diffère.
		if not pcall(GameTooltip.SetSpellByID, GameTooltip, self.data.id) then
			GameTooltip:SetText(self.data.name)
		end
		GameTooltip:Show()
	end)
	row:SetScript("OnLeave", GameTooltip_Hide)
	-- Maj+clic : lien de la recette dans le chat (pour la demander à l'artisan).
	row:SetScript("OnClick", function(self)
		if IsModifiedClick("CHATLINK") then
			local link = C_Spell and C_Spell.GetSpellLink and C_Spell.GetSpellLink(self.data.id)
			if link then
				ChatEdit_InsertLink(link)
			end
		end
	end)
end

local function UpdateRecipeRow(row, data)
	row.data = data
	row.stripe:SetShown(data.index % 2 == 0)
	row.icon:SetTexture(data.icon or "Interface\\Icons\\INV_Misc_QuestionMark")
	row.text:SetText(data.name)
	row.orderButton:SetShown(recipesDialog.canOrder)
end

local function RefreshRecipesDialog()
	local dialog = recipesDialog
	local search = string.lower(dialog.searchText or "")
	local items = {}
	for _, id in ipairs(dialog.recipes or {}) do
		local name = GP.GetRecipeName(id) or ("Recette #" .. id)
		if search == "" or string.lower(name):find(search, 1, true) then
			items[#items + 1] = { id = id, name = name, icon = RecipeIcon(id) }
		end
	end
	table.sort(items, function(a, b) return a.name < b.name end)
	for i, item in ipairs(items) do
		item.index = i
	end
	dialog.list:GPSetItems(items)
	dialog.countText:SetText(("%d / %d"):format(#items, #(dialog.recipes or {})))
	dialog.emptyText:SetShown(#items == 0)
end

local function CreateRecipesDialog()
	local dialog = CreateWindow("GuildProfessionsRecipesDialog", 400, 520, "DIALOG")
	local inset = dialog.Inset

	dialog.subtitle = Label(dialog, nil, "GameFontHighlightSmall", COLOR.muted)
	dialog.subtitle:SetPoint("TOPLEFT", 66, -34)
	dialog.subtitle:SetPoint("RIGHT", -12, 0)
	dialog.subtitle:SetWordWrap(false)

	dialog.searchBox = CreateSearchBox(inset, 250, "Chercher une recette", function(text)
		dialog.searchText = text
		if dialog.list then
			RefreshRecipesDialog()
		end
	end)
	dialog.searchBox:SetPoint("TOPLEFT", 12, -8)

	dialog.countText = Label(inset, nil, "GameFontHighlightSmall", COLOR.muted)
	dialog.countText:SetPoint("LEFT", dialog.searchBox, "RIGHT", 10, 0)

	dialog.list = CreateScrollList(inset, 26, SetupRecipeRow, UpdateRecipeRow)
	dialog.list:SetPoint("TOPLEFT", 4, -34)
	dialog.list:SetPoint("BOTTOMRIGHT", -20, 4)

	dialog.emptyText = Label(inset, "Aucune recette ne correspond.", "GameFontHighlight", COLOR.muted)
	dialog.emptyText:SetPoint("CENTER")

	local hint = Label(dialog, "Survol : détail  ·  Maj+clic : lien dans le chat", "GameFontHighlightSmall", COLOR.muted)
	hint:SetPoint("BOTTOM", 0, 8)

	return dialog
end

function GP.OpenRecipesDialog(char, prof)
	recipesDialog = recipesDialog or CreateRecipesDialog()
	local dialog = recipesDialog
	local r, g, b = ClassColor(char.classFile)
	SetWindowTitle(dialog, ("|cff%02x%02x%02x%s|r"):format(math.floor(r * 255), math.floor(g * 255), math.floor(b * 255), char.name))
	SetWindowPortrait(dialog, GP.GetProfessionIcon(prof))
	local rank = RankName(prof.max)
	dialog.subtitle:SetText(("%s %d/%d%s%s"):format(prof.name, prof.level or 0, prof.max or 0,
		rank and (" · " .. rank) or "", prof.scannedAt and ("  ·  relevé le " .. prof.scannedAt) or ""))
	dialog.recipes = prof.recipes
	dialog.crafterName = char.name
	-- Pas de commande à soi-même, ni depuis un perso hors guilde.
	dialog.canOrder = char.name ~= GP.GetPlayerFullName() and GP.IsEligibleCharacter()
	dialog.searchText = ""
	dialog.searchBox:SetText("")
	if mainFrame and mainFrame:IsShown() then
		dialog:ClearAllPoints()
		dialog:SetPoint("TOPLEFT", mainFrame, "TOPRIGHT", 4, 0)
	end
	RefreshRecipesDialog()
	dialog:Show()
end

-- Dialogue « Commander » ------------------------------------------------------------------

local orderDialog

local function CreateOrderDialog()
	local dialog = CreateFrame("Frame", "GuildProfessionsOrderDialog", UIParent, "BackdropTemplate")
	dialog:SetSize(340, 180)
	dialog:SetPoint("CENTER")
	dialog:SetFrameStrata("FULLSCREEN_DIALOG")
	dialog:SetToplevel(true)
	dialog:EnableMouse(true)
	dialog:SetMovable(true)
	dialog:SetClampedToScreen(true)
	dialog:RegisterForDrag("LeftButton")
	dialog:SetScript("OnDragStart", dialog.StartMoving)
	dialog:SetScript("OnDragStop", dialog.StopMovingOrSizing)
	dialog:SetBackdrop({
		bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
		edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
		tile = true, tileSize = 32, edgeSize = 32,
		insets = { left = 11, right = 12, top = 12, bottom = 11 },
	})
	tinsert(UISpecialFrames, "GuildProfessionsOrderDialog")

	dialog.title = Label(dialog, nil, "GameFontNormal")
	dialog.title:SetPoint("TOP", 0, -18)
	dialog.title:SetJustifyH("CENTER")

	-- La recette vient de la ligne cliquée : affichée, pas éditable.
	dialog.icon = dialog:CreateTexture(nil, "ARTWORK")
	dialog.icon:SetSize(30, 30)
	dialog.icon:SetPoint("TOPLEFT", 26, -44)
	dialog.itemText = Label(dialog, nil, "GameFontHighlight", COLOR.gold)
	dialog.itemText:SetPoint("LEFT", dialog.icon, "RIGHT", 8, 0)
	dialog.itemText:SetPoint("RIGHT", -24, 0)

	local qtyLabel = Label(dialog, "Quantité", "GameFontHighlight")
	qtyLabel:SetPoint("TOPLEFT", dialog.icon, "BOTTOMLEFT", 0, -16)
	dialog.qtyBox = CreateFrame("EditBox", nil, dialog, "InputBoxTemplate")
	dialog.qtyBox:SetSize(50, 20)
	dialog.qtyBox:SetPoint("LEFT", qtyLabel, "RIGHT", 14, 0)
	dialog.qtyBox:SetAutoFocus(false)
	dialog.qtyBox:SetNumeric(true)
	dialog.qtyBox:SetMaxLetters(3)
	dialog.qtyBox:SetJustifyH("CENTER")
	local qtyHint = Label(dialog, "Entrée pour envoyer", "GameFontHighlightSmall", COLOR.muted)
	qtyHint:SetPoint("LEFT", dialog.qtyBox, "RIGHT", 10, 0)

	local function Submit()
		local qty = tonumber(dialog.qtyBox:GetText()) or 0
		if qty < 1 then
			dialog.qtyBox:SetText("1")
			dialog.qtyBox:HighlightText()
			return
		end
		GP.CreateOrder(dialog.crafter, dialog.item, qty, nil)
		dialog:Hide()
		PlaySound(SOUNDKIT.IG_QUEST_LIST_COMPLETE or SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		print(("|cff33ff99GuildProfessions|r — commande de %d× %s envoyée à %s. Synchronisée au prochain /reload ou déco.")
			:format(qty, dialog.item, dialog.crafter))
	end
	dialog.qtyBox:SetScript("OnEnterPressed", Submit)
	dialog.qtyBox:SetScript("OnEscapePressed", function() dialog:Hide() end)

	local accept = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	accept:SetSize(130, 24)
	accept:SetPoint("BOTTOMRIGHT", dialog, "BOTTOM", -6, 18)
	accept:SetText("Commander")
	accept:SetScript("OnClick", Submit)

	local cancel = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	cancel:SetSize(130, 24)
	cancel:SetPoint("BOTTOMLEFT", dialog, "BOTTOM", 6, 18)
	cancel:SetText(CANCEL or "Annuler")
	cancel:SetScript("OnClick", function() dialog:Hide() end)

	return dialog
end

function GP.OpenOrderDialog(crafter, item, icon)
	orderDialog = orderDialog or CreateOrderDialog()
	orderDialog.crafter = crafter
	orderDialog.item = item
	orderDialog.title:SetText("Commande à " .. crafter)
	orderDialog.icon:SetTexture(icon or "Interface\\Icons\\INV_Misc_QuestionMark")
	orderDialog.itemText:SetText(item)
	orderDialog.qtyBox:SetText("1")
	orderDialog:Show()
	orderDialog.qtyBox:SetFocus()
	orderDialog.qtyBox:HighlightText()
end

-- Fenêtres Importer / Exporter ------------------------------------------------------------

local textDialog

local function CreateTextDialog()
	local dialog = CreateWindow("GuildProfessionsTextDialog", 540, 360, "DIALOG")
	SetWindowPortrait(dialog, GP.ICON)
	local inset = dialog.Inset

	dialog.hint = Label(dialog, nil, "GameFontHighlightSmall", COLOR.muted)
	dialog.hint:SetPoint("TOPLEFT", 66, -34)
	dialog.hint:SetPoint("RIGHT", -12, 0)

	local scroll = CreateFrame("ScrollFrame", nil, inset, "UIPanelScrollFrameTemplate")
	scroll:SetPoint("TOPLEFT", 8, -8)
	scroll:SetPoint("BOTTOMRIGHT", -28, 8)

	local editBox = CreateFrame("EditBox", nil, scroll)
	editBox:SetMultiLine(true)
	editBox:SetFontObject(ChatFontNormal)
	editBox:SetWidth(470)
	editBox:SetAutoFocus(false)
	editBox:SetScript("OnEscapePressed", function() dialog:Hide() end)
	scroll:SetScrollChild(editBox)
	dialog.editBox = editBox
	-- Clic n'importe où dans la zone : focus sur le texte.
	scroll:SetScript("OnMouseDown", function() editBox:SetFocus() end)

	dialog.actionButton = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	dialog.actionButton:SetSize(120, 22)
	dialog.actionButton:SetPoint("BOTTOMLEFT", 6, 4)

	dialog.status = Label(dialog, nil, "GameFontHighlightSmall")
	dialog.status:SetPoint("LEFT", dialog.actionButton, "RIGHT", 10, 0)
	dialog.status:SetPoint("RIGHT", -110, 0)

	local close = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	close:SetSize(96, 22)
	close:SetPoint("BOTTOMRIGHT", -6, 4)
	close:SetText(CLOSE or "Fermer")
	close:SetScript("OnClick", function() dialog:Hide() end)

	return dialog
end

local function OpenImportDialog()
	textDialog = textDialog or CreateTextDialog()
	local dialog = textDialog
	SetWindowTitle(dialog, "Importer les données de la guilde")
	dialog.hint:SetText("Colle la chaîne du bot Discord (commande /export-addon), puis Importer.")
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
	textDialog = textDialog or CreateTextDialog()
	local dialog = textDialog
	SetWindowTitle(dialog, "Exporter mes données")
	dialog.hint:SetText("Ctrl+C pour copier, puis /import sur Discord (ou donne la chaîne à un officier).")
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

-- Onglet Membres / armurerie ------------------------------------------------------------

local membersList
local selectedMemberName
local slotButtons = {}
local memberProfChips = {}

local SLOT_LABELS = {
	[1] = "Tête", [2] = "Cou", [3] = "Épaules", [4] = "Chemise", [5] = "Torse",
	[6] = "Taille", [7] = "Jambes", [8] = "Pieds", [9] = "Poignets", [10] = "Mains",
	[11] = "Doigt", [12] = "Doigt", [13] = "Bijou", [14] = "Bijou", [15] = "Dos",
	[16] = "Main droite", [17] = "Main gauche", [18] = "À distance", [19] = "Tabard",
}
local SLOT_EMPTY = {
	[1] = "Head", [2] = "Neck", [3] = "Shoulder", [4] = "Shirt", [5] = "Chest",
	[6] = "Waist", [7] = "Legs", [8] = "Feet", [9] = "Wrists", [10] = "Hands",
	[11] = "Finger", [12] = "Finger", [13] = "Trinket", [14] = "Trinket", [15] = "Chest",
	[16] = "MainHand", [17] = "SecondaryHand", [18] = "Ranged", [19] = "Tabard",
}
-- Disposition de la feuille de personnage : deux colonnes + rangée d'armes.
local SLOTS_LEFT = { 1, 2, 3, 15, 5, 4, 19, 9 }
local SLOTS_RIGHT = { 10, 6, 7, 8, 11, 12, 13, 14 }
local SLOTS_BOTTOM = { 16, 17, 18 }
local SLOT_SIZE, SLOT_STEP = 37, 41

-- L'équipement arrive avec des clés chaîne (export JSON) ou numériques (Data.lua).
local function NormalizedEquipment(member)
	local out = {}
	for key, link in pairs(member.equipment or {}) do
		local slot = tonumber(key)
		if slot then
			out[slot] = link
		end
	end
	return out
end

local function AverageItemLevel(equipment)
	local total, count = 0, 0
	for slot, link in pairs(equipment) do
		if slot ~= 4 and slot ~= 19 then
			local ok, itemLevel = pcall(function()
				if C_Item and C_Item.GetDetailedItemLevelInfo then
					return C_Item.GetDetailedItemLevelInfo(link)
				end
				return GetDetailedItemLevelInfo and GetDetailedItemLevelInfo(link) or nil
			end)
			if ok and itemLevel and itemLevel > 0 then
				total = total + itemLevel
				count = count + 1
			end
		end
	end
	if count > 0 then
		return math.floor(total / count + 0.5)
	end
end

local function ItemIcon(link)
	local getInfo = (C_Item and C_Item.GetItemInfoInstant) or GetItemInfoInstant
	if getInfo then
		local ok, _, _, _, _, icon = pcall(getInfo, link)
		if ok then
			return icon
		end
	end
end

local function ItemQuality(link)
	if C_Item and C_Item.GetItemQualityByID then
		local ok, quality = pcall(C_Item.GetItemQualityByID, link)
		if ok then
			return quality
		end
	end
	if GetItemInfo then
		return select(3, GetItemInfo(link))
	end
end

local function TargetMatchesMember(name)
	if not UnitExists("target") or not UnitIsPlayer("target") then
		return false
	end
	local targetName, targetSurname = UnitFullName("target")
	local full = (targetSurname and targetSurname ~= "") and (targetName .. " " .. targetSurname) or targetName
	return full == name
end

local function SetModelFacing(model, radians)
	model.facing = radians
	if model.SetFacing then
		pcall(model.SetFacing, model, radians)
	else
		pcall(model.SetRotation, model, radians)
	end
end

local function SetSlotItem(button, slot, link)
	button.link = link
	local texture = link and ItemIcon(link) or ("Interface\\Paperdoll\\UI-PaperDoll-Slot-" .. SLOT_EMPTY[slot])
	if SetItemButtonTexture then
		SetItemButtonTexture(button, texture)
	elseif button.icon then
		button.icon:SetTexture(texture)
	end
	local function applyQuality(quality)
		if button.link ~= link then
			return -- le bouton a été réutilisé entre-temps
		end
		if SetItemButtonQuality then
			SetItemButtonQuality(button, quality, link)
		end
	end
	if not link then
		applyQuality(nil)
		return
	end
	applyQuality(ItemQuality(link))
	-- Objet pas encore en cache : qualité appliquée à son arrivée.
	if Item and Item.CreateFromItemLink then
		local item = Item:CreateFromItemLink(link)
		if item and not item:IsItemEmpty() then
			item:ContinueOnItemLoad(function()
				applyQuality(item:GetItemQuality())
			end)
		end
	end
end

local function CreateSlotButton(parent, slot)
	local button = CreateFrame("Button", nil, parent, "ItemButtonTemplate")
	button:SetSize(SLOT_SIZE, SLOT_SIZE)
	button.slot = slot
	button:RegisterForClicks("LeftButtonUp")
	button:SetScript("OnEnter", function(self)
		GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
		if self.link then
			pcall(GameTooltip.SetHyperlink, GameTooltip, self.link)
		else
			GameTooltip:SetText(SLOT_LABELS[self.slot], 1, 1, 1)
			GameTooltip:AddLine("Vide ou pas encore relevé", unpack(COLOR.muted))
		end
		GameTooltip:Show()
	end)
	button:SetScript("OnLeave", GameTooltip_Hide)
	-- Maj+clic : lien dans le chat ; Ctrl+clic : cabine d'essayage.
	button:SetScript("OnClick", function(self)
		if self.link and HandleModifiedItemClick then
			HandleModifiedItemClick(self.link)
		end
	end)
	slotButtons[slot] = button
	return button
end

local function UpdateMemberProfessions(panel, member)
	for _, chip in ipairs(memberProfChips) do
		chip:Hide()
	end
	local previous
	for i, prof in ipairs(member.professions or {}) do
		local chip = memberProfChips[i]
		if not chip then
			chip = CreateFrame("Button", nil, panel)
			chip:SetSize(110, 20)
			chip.icon = chip:CreateTexture(nil, "ARTWORK")
			chip.icon:SetSize(18, 18)
			chip.icon:SetPoint("LEFT")
			chip.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)
			chip.text = Label(chip, nil, "GameFontHighlightSmall")
			chip.text:SetPoint("LEFT", chip.icon, "RIGHT", 4, 0)
			chip:SetScript("OnEnter", function(self)
				GameTooltip:SetOwner(self, "ANCHOR_BOTTOM")
				GameTooltip:SetText(self.prof.name, 1, 1, 1)
				local rank = RankName(self.prof.max)
				GameTooltip:AddLine(("%d / %d%s"):format(self.prof.level or 0, self.prof.max or 0, rank and (" — " .. rank) or ""), unpack(COLOR.muted))
				if RecipeCount(self.prof) > 0 then
					GameTooltip:AddLine("Clic : voir les recettes", unpack(COLOR.positive))
				end
				GameTooltip:Show()
			end)
			chip:SetScript("OnLeave", GameTooltip_Hide)
			chip:SetScript("OnClick", function(self)
				if RecipeCount(self.prof) > 0 then
					GP.OpenRecipesDialog(self.member, self.prof)
				end
			end)
			memberProfChips[i] = chip
		end
		chip.prof, chip.member = prof, member
		chip.icon:SetTexture(GP.GetProfessionIcon(prof))
		chip.text:SetText(("%s %d"):format(prof.name, prof.level or 0))
		chip:SetWidth(chip.text:GetStringWidth() + 30)
		chip:ClearAllPoints()
		if previous then
			chip:SetPoint("LEFT", previous, "RIGHT", 10, 0)
		else
			chip:SetPoint("TOPLEFT", panel, "TOPLEFT", 14, -54)
		end
		chip:Show()
		previous = chip
	end
end

local function ShowMemberDetail(member)
	local panel = mainFrame.memberDetail
	panel:Show()
	mainFrame.memberHint:Hide()

	local r, g, b = ClassColor(member.classFile)
	panel.title:SetText(member.name)
	panel.title:SetTextColor(r, g, b)

	local equipment = NormalizedEquipment(member)
	local averageItemLevel = AverageItemLevel(equipment)
	local className = member.classFile and LOCALIZED_CLASS_NAMES_MALE and LOCALIZED_CLASS_NAMES_MALE[member.classFile]
	local parts = { ("Niveau %s"):format(member.level or "?") }
	if className then
		parts[#parts + 1] = className
	end
	if averageItemLevel then
		parts[#parts + 1] = ("ilvl moyen %d"):format(averageItemLevel)
	end
	local online = IsOnline(member.name)
	if online ~= nil then
		parts[#parts + 1] = online and Paint(COLOR.positive, "en ligne") or Paint(COLOR.muted, "hors ligne")
	end
	panel.subtitle:SetText(table.concat(parts, "  ·  "))
	UpdateMemberProfessions(panel, member)

	-- Modèle 3D : fidèle pour soi-même et pour la cible (SetUnit), générique
	-- habillé (SetCustomRace + TryOn) pour un membre hors ligne — ses
	-- personnalisations (visage...) ne sont pas accessibles aux addons.
	local model = panel.model
	local rendered, note = false, nil
	pcall(model.ClearModel, model)
	if member.name == GP.GetPlayerFullName() then
		rendered = pcall(model.SetUnit, model, "player")
	elseif TargetMatchesMember(member.name) then
		rendered = pcall(model.SetUnit, model, "target")
		note = "rendu fidèle (cible)"
	elseif member.raceId and model.SetCustomRace then
		local ok = pcall(model.SetCustomRace, model, member.raceId, (member.gender or 2) - 2)
		if ok then
			pcall(model.Undress, model)
			for _, slots in ipairs({ SLOTS_LEFT, SLOTS_RIGHT, SLOTS_BOTTOM }) do
				for _, slot in ipairs(slots) do
					if equipment[slot] then
						pcall(model.TryOn, model, equipment[slot])
					end
				end
			end
			rendered = true
			note = "modèle générique + équipement relevé"
		end
	end
	if rendered then
		SetModelFacing(model, 0.4)
	end
	model:SetShown(rendered)
	panel.modelFallback:SetShown(not rendered)
	panel.modelNote:SetText(rendered and ((note and (note .. "  ·  ") or "") .. "glisser pour tourner") or "")

	local known = 0
	for slot, button in pairs(slotButtons) do
		SetSlotItem(button, slot, equipment[slot])
		if equipment[slot] then
			known = known + 1
		end
	end
	panel.equipEmpty:SetShown(known == 0)
end

local function SetupMemberRow(row)
	AddRowBackground(row)
	row:RegisterForClicks("LeftButtonUp")
	row.classIcon = row:CreateTexture(nil, "ARTWORK")
	row.classIcon:SetSize(18, 18)
	row.classIcon:SetPoint("LEFT", 6, 0)
	row.online = row:CreateTexture(nil, "ARTWORK")
	row.online:SetSize(14, 14)
	row.online:SetPoint("LEFT", row.classIcon, "RIGHT", 4, 0)
	row.level = Label(row, nil, "GameFontHighlightSmall", COLOR.muted)
	row.level:SetPoint("RIGHT", -6, 0)
	row.name = Label(row, nil, "GameFontNormal")
	row.name:SetPoint("LEFT", row.online, "RIGHT", 4, 0)
	row.name:SetPoint("RIGHT", row.level, "LEFT", -4, 0)
	row.name:SetWordWrap(false)
	row:SetScript("OnClick", function(self)
		selectedMemberName = self.member.name
		PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		GP.RefreshUI()
	end)
end

local function UpdateMemberRow(row, data)
	local member = data.member
	row.member = member
	row.stripe:SetShown(data.index % 2 == 0)
	row.selectedTex:SetShown(member.name == selectedMemberName)
	SetClassIcon(row.classIcon, member.classFile)
	SetOnlineIcon(row.online, member.name)
	row.name:SetText(member.name)
	row.name:SetTextColor(ClassColor(member.classFile))
	row.level:SetText(member.level and tostring(member.level) or "")
end

local function RefreshMembers()
	local search = string.lower(state.memberSearch or "")
	local items, selected = {}, nil
	for _, member in ipairs(GP.GetRoster()) do
		if search == "" or string.lower(member.name):find(search, 1, true) then
			items[#items + 1] = { member = member }
		end
		if member.name == selectedMemberName then
			selected = member
		end
	end
	-- Membres en ligne d'abord, puis ordre alphabétique.
	table.sort(items, function(a, b)
		local oa, ob = IsOnline(a.member.name) == true, IsOnline(b.member.name) == true
		if oa ~= ob then
			return oa
		end
		return a.member.name < b.member.name
	end)
	for i, item in ipairs(items) do
		item.index = i
	end
	membersList:GPSetItems(items)
	mainFrame.membersEmpty:SetShown(#items == 0)
	if selected then
		ShowMemberDetail(selected)
	else
		selectedMemberName = nil
		mainFrame.memberDetail:Hide()
		mainFrame.memberHint:Show()
	end
end

local function BuildMembersTab(frame, parent)
	local left = CreateFrame("Frame", nil, parent)
	left:SetPoint("TOPLEFT", 4, -4)
	left:SetPoint("BOTTOMLEFT", 4, 4)
	left:SetWidth(236)
	local leftBg = left:CreateTexture(nil, "BACKGROUND")
	leftBg:SetAllPoints()
	leftBg:SetColorTexture(unpack(COLOR.panel))

	local search = CreateSearchBox(left, 206, "Chercher un membre", function(text)
		state.memberSearch = text
		if membersList then
			RefreshMembers()
		end
	end)
	search:SetPoint("TOPLEFT", 14, -8)

	membersList = CreateScrollList(left, 24, SetupMemberRow, UpdateMemberRow)
	membersList:SetPoint("TOPLEFT", 4, -34)
	membersList:SetPoint("BOTTOMRIGHT", -16, 4)

	frame.membersEmpty = Label(left, "Aucun membre trouvé.", "GameFontHighlightSmall", COLOR.muted)
	frame.membersEmpty:SetPoint("TOP", membersList, "TOP", 0, -20)

	frame.memberHint = Label(parent, "Sélectionne un membre pour voir\nson équipement et son apparence.", "GameFontHighlight", COLOR.muted)
	frame.memberHint:SetPoint("CENTER", left:GetWidth() / 2 + 4, 0)
	frame.memberHint:SetJustifyH("CENTER")

	-- Panneau de droite : armurerie façon feuille de personnage.
	local detail = CreateFrame("Frame", nil, parent)
	detail:SetPoint("TOPLEFT", left, "TOPRIGHT", 6, 0)
	detail:SetPoint("BOTTOMRIGHT", -4, 4)
	detail:Hide()
	frame.memberDetail = detail

	detail.title = Label(detail, nil, "GameFontNormalLarge")
	detail.title:SetPoint("TOPLEFT", 14, -10)
	detail.subtitle = Label(detail, nil, "GameFontHighlightSmall", { 0.85, 0.85, 0.85 })
	detail.subtitle:SetPoint("TOPLEFT", 14, -32)

	local top = -84
	for i, slot in ipairs(SLOTS_LEFT) do
		CreateSlotButton(detail, slot):SetPoint("TOPLEFT", 14, top - (i - 1) * SLOT_STEP)
	end
	for i, slot in ipairs(SLOTS_RIGHT) do
		CreateSlotButton(detail, slot):SetPoint("TOPRIGHT", -14, top - (i - 1) * SLOT_STEP)
	end
	for i, slot in ipairs(SLOTS_BOTTOM) do
		CreateSlotButton(detail, slot):SetPoint("BOTTOM", (i - 2) * SLOT_STEP, 8)
	end

	local modelBg = detail:CreateTexture(nil, "BACKGROUND")
	modelBg:SetPoint("TOPLEFT", 14 + SLOT_SIZE + 8, top)
	modelBg:SetPoint("BOTTOMRIGHT", -(14 + SLOT_SIZE + 8), SLOT_SIZE + 18)
	modelBg:SetColorTexture(0, 0, 0, 0.35)

	detail.model = CreateFrame("DressUpModel", nil, detail)
	detail.model:SetAllPoints(modelBg)
	detail.model:EnableMouse(true)
	detail.model:SetScript("OnMouseDown", function(self)
		self.dragX = GetCursorPosition()
		self.dragFacing = self.facing or 0
	end)
	detail.model:SetScript("OnMouseUp", function(self)
		self.dragX = nil
	end)
	detail.model:SetScript("OnUpdate", function(self)
		if self.dragX then
			SetModelFacing(self, self.dragFacing + (GetCursorPosition() - self.dragX) / 80)
		end
	end)

	detail.modelFallback = Label(detail, "Aperçu 3D indisponible\npour ce personnage.", "GameFontHighlight", COLOR.muted)
	detail.modelFallback:SetPoint("CENTER", modelBg, "CENTER")
	detail.modelFallback:SetJustifyH("CENTER")

	detail.modelNote = Label(detail, nil, "GameFontHighlightSmall", COLOR.muted)
	detail.modelNote:SetPoint("BOTTOM", modelBg, "BOTTOM", 0, 6)

	detail.equipEmpty = Label(detail, "Équipement inconnu — relevé au prochain\npassage du membre avec l'addon.", "GameFontHighlightSmall", COLOR.muted)
	detail.equipEmpty:SetPoint("TOP", modelBg, "TOP", 0, -10)
	detail.equipEmpty:SetJustifyH("CENTER")
end

-- Onglet Commandes ------------------------------------------------------------------------

local ordersList
local filterButtons = {}

local STATUS_DISPLAY = {
	open = { label = "ouverte", color = COLOR.gold, icon = "Interface\\GossipFrame\\AvailableQuestIcon" },
	accepted = { label = "acceptée", color = COLOR.info, icon = "Interface\\RaidFrame\\ReadyCheck-Waiting" },
	done = { label = "terminée", color = COLOR.positive, icon = "Interface\\RaidFrame\\ReadyCheck-Ready" },
	cancelled = { label = "annulée", color = COLOR.muted, icon = "Interface\\RaidFrame\\ReadyCheck-NotReady" },
}

local function IsActiveOrder(order)
	return order.status == "open" or order.status == "accepted"
end

-- Commandes qui attendent une action du joueur (pastille d'onglet, infobulle).
function GP.CountOrdersToHandle()
	if not GuildProfessionsDB then
		return 0
	end
	local me, count = GP.GetPlayerFullName(), 0
	for _, order in ipairs(GP.GetOrders()) do
		if order.crafter == me and IsActiveOrder(order) then
			count = count + 1
		end
	end
	return count
end

StaticPopupDialogs["GUILDPROFESSIONS_CANCEL_ORDER"] = {
	text = "Annuler la commande « %s » ?",
	button1 = YES or "Oui",
	button2 = NO or "Non",
	OnAccept = function(_, order)
		GP.SetOrderStatus(order, "cancelled")
	end,
	timeout = 0,
	whileDead = true,
	hideOnEscape = true,
	showAlert = true,
}

local function Who(name, me)
	return name == me and Paint(COLOR.gold, "toi") or (name or "?")
end

local function ConfigureOrderButton(button, label, handler)
	if label then
		button:SetText(label)
		button:SetScript("OnClick", handler)
		button:Show()
	else
		button:Hide()
	end
end

local function SetupOrderRow(row)
	AddRowBackground(row)
	row.statusIcon = row:CreateTexture(nil, "ARTWORK")
	row.statusIcon:SetSize(18, 18)
	row.statusIcon:SetPoint("LEFT", 8, 0)

	row.button2 = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
	row.button2:SetSize(84, 22)
	row.button2:SetPoint("RIGHT", -6, 0)
	row.button1 = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
	row.button1:SetSize(84, 22)
	row.button1:SetPoint("RIGHT", row.button2, "LEFT", -4, 0)

	row.line1 = Label(row, nil, "GameFontNormal")
	row.line1:SetPoint("TOPLEFT", row.statusIcon, "TOPRIGHT", 8, 6)
	row.line1:SetPoint("RIGHT", row.button1, "LEFT", -8, 0)
	row.line1:SetWordWrap(false)

	row.line2 = Label(row, nil, "GameFontHighlightSmall", COLOR.muted)
	row.line2:SetPoint("TOPLEFT", row.line1, "BOTTOMLEFT", 0, -4)
	row.line2:SetPoint("RIGHT", row.button1, "LEFT", -8, 0)
	row.line2:SetWordWrap(false)
end

local function UpdateOrderRow(row, data)
	local order, me = data.order, data.me
	row.stripe:SetShown(data.index % 2 == 0)
	local display = STATUS_DISPLAY[order.status] or { label = order.status or "?", color = COLOR.text }
	row.statusIcon:SetTexture(display.icon)
	row.statusIcon:SetShown(display.icon ~= nil)
	local pending = order.pendingSync and Paint(COLOR.muted, "  · synchro au prochain /reload") or ""
	row.line1:SetText(("%d× %s   %s%s"):format(order.qty, order.item, Paint(display.color, display.label), pending))
	local parts = { ("%s → %s"):format(Who(order.requester, me), Who(order.crafter, me)) }
	parts[#parts + 1] = order.id and ("#" .. order.id) or "locale"
	if order.createdAt then
		parts[#parts + 1] = order.createdAt
	end
	if order.note then
		parts[#parts + 1] = order.note
	end
	row.line2:SetText(table.concat(parts, "  ·  "))

	local isCrafter = order.crafter == me
	local isRequester = order.requester == me

	local label1, handler1
	if isCrafter and order.status == "open" then
		label1, handler1 = "Accepter", function() GP.SetOrderStatus(order, "accepted") end
	elseif isCrafter and order.status == "accepted" then
		label1, handler1 = "Terminé", function() GP.SetOrderStatus(order, "done") end
	end
	local label2, handler2
	if (isCrafter or isRequester) and IsActiveOrder(order) then
		label2, handler2 = CANCEL or "Annuler", function()
			StaticPopup_Show("GUILDPROFESSIONS_CANCEL_ORDER", order.item, nil, order)
		end
	end
	ConfigureOrderButton(row.button1, label1, handler1)
	ConfigureOrderButton(row.button2, label2, handler2)
end

local function RefreshOrders()
	local me = GP.GetPlayerFullName()
	local items = {}
	for _, order in ipairs(GP.GetOrders()) do
		local keep = state.showClosed or IsActiveOrder(order)
		if keep and state.ordersFilter == "crafter" then
			keep = order.crafter == me
		elseif keep and state.ordersFilter == "requester" then
			keep = order.requester == me
		end
		if keep then
			items[#items + 1] = { order = order, me = me, index = #items + 1 }
		end
	end
	ordersList:GPSetItems(items)
	for _, button in ipairs(filterButtons) do
		local selected = button.value == state.ordersFilter
		button.selectedTex:SetShown(selected)
		button.underline:SetShown(selected)
		button.text:SetTextColor(unpack(selected and COLOR.gold or COLOR.text))
	end
	mainFrame.ordersEmptyText:SetShown(#items == 0)
end

local function BuildOrdersTab(frame, parent)
	local toolbar = CreateFrame("Frame", nil, parent)
	toolbar:SetPoint("TOPLEFT", 8, -6)
	toolbar:SetPoint("TOPRIGHT", -8, -6)
	toolbar:SetHeight(24)

	local previous
	for _, def in ipairs({
		{ "all", "Toutes" },
		{ "crafter", "À fabriquer" },
		{ "requester", "Mes demandes" },
	}) do
		local button = CreateFrame("Button", nil, toolbar)
		button:SetHeight(24)
		button.value = def[1]
		AddRowBackground(button)
		button.stripe:Hide()
		button.text = Label(button, def[2], "GameFontHighlight")
		button.text:SetPoint("CENTER")
		button:SetWidth(button.text:GetStringWidth() + 24)
		button.underline = button:CreateTexture(nil, "ARTWORK")
		button.underline:SetColorTexture(unpack(COLOR.gold))
		button.underline:SetHeight(2)
		button.underline:SetPoint("BOTTOMLEFT")
		button.underline:SetPoint("BOTTOMRIGHT")
		button:SetScript("OnClick", function(self)
			state.ordersFilter = self.value
			PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
			GP.RefreshUI()
		end)
		if previous then
			button:SetPoint("LEFT", previous, "RIGHT", 4, 0)
		else
			button:SetPoint("LEFT")
		end
		filterButtons[#filterButtons + 1] = button
		previous = button
	end

	local showClosed = CreateFrame("CheckButton", nil, toolbar, "UICheckButtonTemplate")
	showClosed:SetSize(24, 24)
	showClosed:SetPoint("RIGHT", -150, 0)
	SetCheckLabel(showClosed, "Afficher terminées et annulées")
	showClosed:SetScript("OnClick", function(self)
		state.showClosed = self:GetChecked() and true or false
		GP.RefreshUI()
	end)

	ordersList = CreateScrollList(parent, 46, SetupOrderRow, UpdateOrderRow)
	ordersList:SetPoint("TOPLEFT", toolbar, "BOTTOMLEFT", -4, -8)
	ordersList:SetPoint("BOTTOMRIGHT", parent, "BOTTOMRIGHT", -22, 4)

	frame.ordersEmptyText = Label(parent, "Aucune commande ici.\nPour commander : onglet Métiers → clic sur un artisan → « Commander » face à la recette.",
		"GameFontHighlight", COLOR.muted)
	frame.ordersEmptyText:SetPoint("CENTER", ordersList, "CENTER")
	frame.ordersEmptyText:SetJustifyH("CENTER")
	frame.ordersEmptyText:SetWidth(460)
end

-- Fenêtre principale --------------------------------------------------------------------

local TABS = { "Métiers", "Membres", "Commandes" }

local function UpdateBanner()
	local server = GP.GetServerData()
	local text
	if server.generatedUtc then
		text = "Données du " .. server.generatedUtc
	else
		text = Paint(COLOR.gold, "Aucune donnée serveur — lance l'app compagnon ou importe une chaîne.")
	end
	if server.mock then
		text = text .. " " .. Paint(COLOR.gold, "(démonstration)")
	end
	local eligible, reason = GP.IsEligibleCharacter()
	if not eligible then
		text = Paint(COLOR.negative, ("Personnage %s : relevés et commandes désactivés."):format(reason)) .. "  " .. text
	end
	mainFrame.banner:SetText(text)
end

local function UpdateTabBadges()
	local count = GP.CountOrdersToHandle()
	local tab = mainFrame.Tabs[3]
	tab:SetText(count > 0 and ("Commandes (%d)"):format(count) or "Commandes")
	if PanelTemplates_TabResize then
		PanelTemplates_TabResize(tab, 0)
	end
end

local function SelectTab(index)
	state.tab = index
	mainFrame.tabProfessions:SetShown(index == 1)
	mainFrame.tabMembers:SetShown(index == 2)
	mainFrame.tabOrders:SetShown(index == 3)
	PanelTemplates_SetTab(mainFrame, index)
	GP.RefreshUI()
end

local function CreateMainFrame()
	local frame = CreateWindow("GuildProfessionsFrame", 900, 580)
	mainFrame = frame
	SetWindowTitle(frame, "GuildProfessions")
	SetWindowPortrait(frame, GP.ICON)

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

	frame.banner = Label(frame, nil, "GameFontHighlightSmall", COLOR.muted)
	frame.banner:SetPoint("LEFT", exportButton, "RIGHT", 12, 0)
	frame.banner:SetPoint("RIGHT", -12, 0)
	frame.banner:SetJustifyH("RIGHT")
	frame.banner:SetWordWrap(false)

	-- Onglets natifs sous la fenêtre
	frame.Tabs = {}
	for i, label in ipairs(TABS) do
		local tab = CreateFrame("Button", "GuildProfessionsFrameTab" .. i, frame, "PanelTabButtonTemplate")
		tab:SetID(i)
		tab:SetText(label)
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

	local inset = frame.Inset
	frame.tabProfessions = CreateFrame("Frame", nil, inset)
	frame.tabProfessions:SetAllPoints()
	BuildProfessionsTab(frame, frame.tabProfessions)

	frame.tabMembers = CreateFrame("Frame", nil, inset)
	frame.tabMembers:SetAllPoints()
	BuildMembersTab(frame, frame.tabMembers)

	frame.tabOrders = CreateFrame("Frame", nil, inset)
	frame.tabOrders:SetAllPoints()
	BuildOrdersTab(frame, frame.tabOrders)

	frame:HookScript("OnShow", function()
		PlaySound(SOUNDKIT.IG_CHARACTER_INFO_OPEN)
		-- Demande un roster frais (présence en ligne) ; le client limite la cadence.
		if C_GuildInfo and C_GuildInfo.GuildRoster then
			C_GuildInfo.GuildRoster()
		elseif GuildRoster then
			GuildRoster()
		end
		RebuildGuildOnline()
		if GP.OnMainFrameVisibilityChanged then
			GP.OnMainFrameVisibilityChanged()
		end
	end)
	frame:HookScript("OnHide", function()
		PlaySound(SOUNDKIT.IG_CHARACTER_INFO_CLOSE)
		if GP.OnMainFrameVisibilityChanged then
			GP.OnMainFrameVisibilityChanged()
		end
	end)

	SelectTab(state.tab)
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

-- API publique -------------------------------------------------------------------------

function GP.IsMainFrameShown()
	return mainFrame ~= nil and mainFrame:IsShown()
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
	if state.tab == 1 then
		RefreshProfessions()
	elseif state.tab == 2 then
		RefreshMembers()
	elseif state.tab == 3 then
		RefreshOrders()
	end
	local guild = GP.GetPlayerGuildName()
	mainFrame.guildLine:SetText(guild and ("<" .. guild .. ">  ·  annuaire des métiers") or "annuaire des métiers")
	UpdateTabBadges()
	UpdateBanner()
end

-- Présence en ligne : le roster arrive en différé après la demande.
local events = CreateFrame("Frame")
events:RegisterEvent("GUILD_ROSTER_UPDATE")
events:SetScript("OnEvent", function()
	RebuildGuildOnline()
	GP.RefreshUI()
end)
