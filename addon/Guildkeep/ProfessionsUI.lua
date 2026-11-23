local _, GP = ...

-- Module Métiers, partie interface : l'annuaire des artisans (onglet Métiers),
-- filtré par métier, niveau et recette. « Voir » ouvre la fenêtre des recettes
-- du socle.

local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label
local ClassColor, SetOnlineIcon, AddOnlineLine = UI.ClassColor, UI.SetOnlineIcon, UI.AddOnlineLine
local AddRowBackground, CreateScrollList, CreateSearchBox = UI.AddRowBackground, UI.CreateScrollList, UI.CreateSearchBox
local RankName, RecipeCount = UI.RankName, UI.RecipeCount
local state = UI.State

local mainFrame -- fenêtre principale, connue à la construction de l'onglet

local FILTER_WIDTH = 190
local ROW_HEIGHT = 26
local COL = { icon = 6, name = 32, prof = 200, level = 326, recipes = 444, source = 532 }

local professionsList
local profButtonPool = {}

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

	-- Bouton explicite : la ligne entière est cliquable, mais rien ne le
	-- montrait. « Voir (12) » rend l'action et le nombre visibles.
	row.recipesButton = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
	row.recipesButton:SetSize(COL.source - COL.recipes - 10, 20)
	row.recipesButton:SetPoint("LEFT", COL.recipes - 2, 0)
	row.recipesButton:SetNormalFontObject("GameFontNormalSmall")
	row.recipesButton:SetHighlightFontObject("GameFontHighlightSmall")
	row.recipesButton:SetScript("OnClick", function(self)
		local entry = self:GetParent().entry
		GP.OpenRecipesDialog(entry.char, entry.prof)
	end)
	row.recipesButton:SetScript("OnEnter", function(self)
		ProfessionRowTooltip(self:GetParent())
	end)
	row.recipesButton:SetScript("OnLeave", GameTooltip_Hide)

	row.recipesNone = Label(row, Paint(COLOR.muted, "aucune"), "GameFontHighlightSmall")
	row.recipesNone:SetPoint("LEFT", COL.recipes + 4, 0)

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

	local hasRecipes = entry.recipeCount > 0
	row.recipesButton:SetShown(hasRecipes)
	row.recipesNone:SetShown(not hasRecipes)
	if hasRecipes then
		row.recipesButton:SetText(("Voir (%d)"):format(entry.recipeCount))
	else
		-- Jamais relevées (fenêtre du métier pas encore ouverte) ≠ métier sans recettes.
		row.recipesNone:SetText(Paint(COLOR.muted, prof.recipes == nil and "à relever" or "aucune"))
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
	mainFrame = frame
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
	professionsList:SetPoint("BOTTOMRIGHT", listArea, "BOTTOMRIGHT", 0, 20)

	local listHint = Label(listArea, "Clique sur « Voir » (ou sur la ligne) pour la liste des recettes d'un artisan, puis « Commander ».",
		"GameFontHighlightSmall", COLOR.muted)
	listHint:SetPoint("BOTTOMLEFT", 4, 2)
	listHint:SetPoint("BOTTOMRIGHT", 0, 2)
	listHint:SetWordWrap(false)

	frame.emptyText = Label(listArea, nil, "GameFontNormal")
	frame.emptyText:SetPoint("CENTER", professionsList, "CENTER", 0, 10)
	frame.emptyText:SetJustifyH("CENTER")
	frame.emptyHint = Label(listArea, nil, "GameFontHighlightSmall", COLOR.muted)
	frame.emptyHint:SetPoint("TOP", frame.emptyText, "BOTTOM", 0, -6)
	frame.emptyHint:SetWidth(380)
	frame.emptyHint:SetJustifyH("CENTER")
end

GP.Modules.Define("professions", "ui", function(ctx)
	ctx:AddTab({
		label = "Métiers",
		key = "tabProfessions",
		order = 10,
		build = BuildProfessionsTab,
		refresh = RefreshProfessions,
	})
end)
