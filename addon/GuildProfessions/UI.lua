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

		row.orderButton = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
		row.orderButton:SetSize(86, 20)
		row.orderButton:SetPoint("RIGHT", row, "RIGHT", -4, 0)
		row.orderButton:SetText("Commander")
		row.orderButton:SetScript("OnClick", function(self)
			GP.OpenOrderDialog(self.crafter, self.prefillItem)
		end)

		row.info = row:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
		row.info:SetPoint("LEFT", row.bar, "RIGHT", 10, 0)
		row.info:SetPoint("RIGHT", row.orderButton, "LEFT", -6, 0)
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

		row.orderButton.crafter = entry.char.name
		row.orderButton.prefillItem = entry.matched or ""
		row.orderButton:SetShown(entry.char.name ~= GP.GetPlayerFullName() and GP.IsEligibleCharacter())
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
	local eligible, reason = GP.IsEligibleCharacter()
	if not eligible then
		text = ("|cffff5555Personnage %s : scan et commandes désactivés.|r  "):format(reason) .. text
	end
	mainFrame.banner:SetText(text)
end

-- Fenêtre principale ----------------------------------------------------------

local function SelectTab(tabIndex)
	state.tab = tabIndex
	mainFrame.tabProfessions:SetShown(tabIndex == 1)
	mainFrame.tabMembers:SetShown(tabIndex == 2)
	mainFrame.tabOrders:SetShown(tabIndex == 3)
	mainFrame.tabButton1:SetEnabled(tabIndex ~= 1)
	mainFrame.tabButton2:SetEnabled(tabIndex ~= 2)
	mainFrame.tabButton3:SetEnabled(tabIndex ~= 3)
	GP.RefreshUI()
end

-- Dialogue « Commander » --------------------------------------------------------

local orderDialog

local function CreateOrderDialog()
	local dialog = CreateFrame("Frame", "GuildProfessionsOrderDialog", UIParent, "BackdropTemplate")
	dialog:SetSize(340, 210)
	dialog:SetPoint("CENTER")
	dialog:SetFrameStrata("DIALOG")
	dialog:EnableMouse(true)
	dialog:SetMovable(true)
	dialog:RegisterForDrag("LeftButton")
	dialog:SetScript("OnDragStart", dialog.StartMoving)
	dialog:SetScript("OnDragStop", dialog.StopMovingOrSizing)
	dialog:SetBackdrop({
		bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
		edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
		edgeSize = 24,
		insets = { left = 6, right = 6, top = 6, bottom = 6 },
	})
	tinsert(UISpecialFrames, "GuildProfessionsOrderDialog")

	dialog.title = dialog:CreateFontString(nil, "OVERLAY", "GameFontNormal")
	dialog.title:SetPoint("TOP", 0, -16)

	local itemLabel = dialog:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	itemLabel:SetPoint("TOPLEFT", 20, -44)
	itemLabel:SetText("Objet / recette")
	dialog.itemBox = CreateFrame("EditBox", nil, dialog, "InputBoxTemplate")
	dialog.itemBox:SetSize(290, 20)
	dialog.itemBox:SetPoint("TOPLEFT", itemLabel, "BOTTOMLEFT", 6, -4)
	dialog.itemBox:SetAutoFocus(false)

	local qtyLabel = dialog:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	qtyLabel:SetPoint("TOPLEFT", 20, -96)
	qtyLabel:SetText("Quantité")
	dialog.qtyBox = CreateFrame("EditBox", nil, dialog, "InputBoxTemplate")
	dialog.qtyBox:SetSize(50, 20)
	dialog.qtyBox:SetPoint("TOPLEFT", qtyLabel, "BOTTOMLEFT", 6, -4)
	dialog.qtyBox:SetAutoFocus(false)
	dialog.qtyBox:SetNumeric(true)
	dialog.qtyBox:SetMaxLetters(3)

	local noteLabel = dialog:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	noteLabel:SetPoint("TOPLEFT", 110, -96)
	noteLabel:SetText("Note (optionnelle)")
	dialog.noteBox = CreateFrame("EditBox", nil, dialog, "InputBoxTemplate")
	dialog.noteBox:SetSize(196, 20)
	dialog.noteBox:SetPoint("TOPLEFT", noteLabel, "BOTTOMLEFT", 6, -4)
	dialog.noteBox:SetAutoFocus(false)

	local accept = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	accept:SetSize(120, 24)
	accept:SetPoint("BOTTOMLEFT", 24, 18)
	accept:SetText("Commander")
	accept:SetScript("OnClick", function()
		local item = dialog.itemBox:GetText()
		if item == "" then
			return
		end
		GP.CreateOrder(dialog.crafter, item, tonumber(dialog.qtyBox:GetText()) or 1, dialog.noteBox:GetText())
		dialog:Hide()
		print(("|cff33ff99GuildProfessions|r — commande envoyée à %s. Synchronisée au prochain /reload ou déco."):format(dialog.crafter))
	end)

	local cancel = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	cancel:SetSize(120, 24)
	cancel:SetPoint("BOTTOMRIGHT", -24, 18)
	cancel:SetText("Annuler")
	cancel:SetScript("OnClick", function() dialog:Hide() end)

	return dialog
end

function GP.OpenOrderDialog(crafter, prefillItem)
	orderDialog = orderDialog or CreateOrderDialog()
	orderDialog.crafter = crafter
	orderDialog.title:SetText("Commande à " .. crafter)
	orderDialog.itemBox:SetText(prefillItem or "")
	orderDialog.qtyBox:SetText("1")
	orderDialog.noteBox:SetText("")
	orderDialog:Show()
	orderDialog.itemBox:SetFocus()
end

-- Fenêtres Importer / Exporter -----------------------------------------------------

local textDialog

local function CreateTextDialog()
	local dialog = CreateFrame("Frame", "GuildProfessionsTextDialog", UIParent, "BackdropTemplate")
	dialog:SetSize(520, 320)
	dialog:SetPoint("CENTER")
	dialog:SetFrameStrata("DIALOG")
	dialog:EnableMouse(true)
	dialog:SetMovable(true)
	dialog:RegisterForDrag("LeftButton")
	dialog:SetScript("OnDragStart", dialog.StartMoving)
	dialog:SetScript("OnDragStop", dialog.StopMovingOrSizing)
	dialog:SetBackdrop({
		bgFile = "Interface\\DialogFrame\\UI-DialogBox-Background",
		edgeFile = "Interface\\DialogFrame\\UI-DialogBox-Border",
		edgeSize = 24,
		insets = { left = 6, right = 6, top = 6, bottom = 6 },
	})
	tinsert(UISpecialFrames, "GuildProfessionsTextDialog")

	dialog.title = dialog:CreateFontString(nil, "OVERLAY", "GameFontNormal")
	dialog.title:SetPoint("TOP", 0, -16)

	dialog.hint = dialog:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	dialog.hint:SetPoint("TOP", 0, -34)
	dialog.hint:SetTextColor(0.7, 0.7, 0.7)

	local scroll = CreateFrame("ScrollFrame", nil, dialog, "UIPanelScrollFrameTemplate")
	scroll:SetPoint("TOPLEFT", 20, -52)
	scroll:SetPoint("BOTTOMRIGHT", -40, 52)

	local editBox = CreateFrame("EditBox", nil, scroll)
	editBox:SetMultiLine(true)
	editBox:SetFontObject(ChatFontNormal)
	editBox:SetWidth(440)
	editBox:SetAutoFocus(false)
	editBox:SetScript("OnEscapePressed", function() dialog:Hide() end)
	scroll:SetScrollChild(editBox)
	dialog.editBox = editBox

	dialog.status = dialog:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	dialog.status:SetPoint("BOTTOMLEFT", 24, 56)

	dialog.actionButton = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	dialog.actionButton:SetSize(140, 24)
	dialog.actionButton:SetPoint("BOTTOMLEFT", 24, 18)

	local close = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	close:SetSize(100, 24)
	close:SetPoint("BOTTOMRIGHT", -24, 18)
	close:SetText("Fermer")
	close:SetScript("OnClick", function() dialog:Hide() end)

	return dialog
end

local function OpenImportDialog()
	textDialog = textDialog or CreateTextDialog()
	local dialog = textDialog
	dialog.title:SetText("Importer les données de la guilde")
	dialog.hint:SetText("Colle la chaîne du bot Discord (commande /export-addon), puis Importer.")
	dialog.editBox:SetScript("OnTextChanged", nil)
	dialog.editBox:SetText("")
	dialog.status:SetText("")
	dialog.actionButton:SetText("Importer")
	dialog.actionButton:Show()
	dialog.actionButton:SetScript("OnClick", function()
		local ok, err = GP.ImportString(dialog.editBox:GetText())
		if ok then
			dialog.status:SetText("|cff33ff66Import réussi — données à jour.|r")
			dialog.editBox:SetText("")
		else
			dialog.status:SetText("|cffff5555" .. (err or "échec") .. "|r")
		end
	end)
	dialog:Show()
	dialog.editBox:SetFocus()
end

local function OpenExportDialog()
	textDialog = textDialog or CreateTextDialog()
	local dialog = textDialog
	dialog.title:SetText("Exporter mes données")
	dialog.hint:SetText("Ctrl+C pour copier, puis /import sur Discord (ou donne la chaîne à un officier).")
	dialog.actionButton:Hide()
	local text, err = GP.BuildExportString()
	if text then
		dialog.status:SetText(("|cff999999%d caractères|r"):format(#text))
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
		dialog.status:SetText("|cffff5555" .. (err or "échec de l'export") .. "|r")
	end
	dialog:Show()
end

-- Onglet Membres / armurerie --------------------------------------------------------

local memberRowPool = {}
local equipRowPool = {}
local selectedMemberName

local SLOT_ORDER = { 1, 2, 3, 15, 5, 9, 10, 6, 7, 8, 11, 12, 13, 14, 16, 17, 18, 4, 19 }
local SLOT_LABELS = {
	[1] = "Tête", [2] = "Cou", [3] = "Épaules", [4] = "Chemise", [5] = "Torse",
	[6] = "Taille", [7] = "Jambes", [8] = "Pieds", [9] = "Poignets", [10] = "Mains",
	[11] = "Anneau", [12] = "Anneau", [13] = "Bijou", [14] = "Bijou", [15] = "Dos",
	[16] = "Main droite", [17] = "Main gauche", [18] = "À distance", [19] = "Tabard",
}

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

local function TargetMatchesMember(name)
	if not UnitExists("target") or not UnitIsPlayer("target") then
		return false
	end
	local targetName, targetSurname = UnitFullName("target")
	local full = (targetSurname and targetSurname ~= "") and (targetName .. " " .. targetSurname) or targetName
	return full == name
end

local function ShowMemberDetail(member)
	selectedMemberName = member.name
	local panel = mainFrame.memberDetail
	panel:Show()
	mainFrame.memberHint:Hide()

	local r, g, b = ClassColor(member.classFile)
	panel.title:SetText(member.name)
	panel.title:SetTextColor(r, g, b)

	local equipment = NormalizedEquipment(member)
	local averageItemLevel = AverageItemLevel(equipment)
	panel.subtitle:SetText(("Niveau %s%s"):format(member.level or "?",
		averageItemLevel and ("  ·  ilvl moyen %d"):format(averageItemLevel) or ""))

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
			for _, slot in ipairs(SLOT_ORDER) do
				if equipment[slot] then
					pcall(model.TryOn, model, equipment[slot])
				end
			end
			rendered = true
			note = "modèle générique + équipement scanné"
		end
	end
	if rendered then
		pcall(model.SetRotation, model, 0.4)
	end
	model:SetShown(rendered)
	panel.modelFallback:SetShown(not rendered)
	panel.modelNote:SetText(note or "")

	-- Liste d'équipement : liens cliquables avec tooltip natif.
	for _, row in ipairs(equipRowPool) do
		row:Hide()
	end
	local index = 0
	for _, slot in ipairs(SLOT_ORDER) do
		local link = equipment[slot]
		if link then
			index = index + 1
			local row = equipRowPool[index]
			if not row then
				row = CreateFrame("Button", nil, panel.equipList)
				row:SetHeight(17)
				row.slotText = row:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
				row.slotText:SetPoint("LEFT", 0, 0)
				row.slotText:SetWidth(76)
				row.slotText:SetJustifyH("LEFT")
				row.linkText = row:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
				row.linkText:SetPoint("LEFT", row.slotText, "RIGHT", 4, 0)
				row.linkText:SetPoint("RIGHT", row, "RIGHT", 0, 0)
				row.linkText:SetJustifyH("LEFT")
				row.linkText:SetWordWrap(false)
				row:SetScript("OnEnter", function(self)
					if self.link then
						GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
						pcall(GameTooltip.SetHyperlink, GameTooltip, self.link)
						GameTooltip:Show()
					end
				end)
				row:SetScript("OnLeave", function()
					GameTooltip:Hide()
				end)
				equipRowPool[index] = row
			end
			row:SetParent(panel.equipList)
			row:ClearAllPoints()
			row:SetPoint("TOPLEFT", panel.equipList, "TOPLEFT", 0, -(index - 1) * 17)
			row:SetPoint("RIGHT", panel.equipList, "RIGHT", 0, 0)
			row.slotText:SetText(SLOT_LABELS[slot] or slot)
			row.linkText:SetText(link)
			row.link = link
			row:Show()
		end
	end
	panel.equipEmpty:SetShown(index == 0)
end

local function RefreshMembers()
	local roster = GP.GetRoster()
	local content = mainFrame.membersContent
	for _, row in ipairs(memberRowPool) do
		row:Hide()
	end
	local selected
	for i, member in ipairs(roster) do
		local row = memberRowPool[i]
		if not row then
			row = CreateFrame("Button", nil, content)
			row:SetHeight(24)
			row.name = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
			row.name:SetPoint("LEFT", 4, 0)
			row.name:SetPoint("RIGHT", row, "RIGHT", -38, 0)
			row.name:SetJustifyH("LEFT")
			row.name:SetWordWrap(false)
			row.level = row:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
			row.level:SetPoint("RIGHT", -4, 0)
			local highlight = row:CreateTexture(nil, "HIGHLIGHT")
			highlight:SetAllPoints()
			highlight:SetColorTexture(1, 1, 1, 0.08)
			row.selectedTex = row:CreateTexture(nil, "BACKGROUND")
			row.selectedTex:SetAllPoints()
			row.selectedTex:SetColorTexture(1, 0.82, 0, 0.12)
			row:SetScript("OnClick", function(self)
				ShowMemberDetail(self.member)
				RefreshMembers()
			end)
			memberRowPool[i] = row
		end
		row:SetParent(content)
		row:ClearAllPoints()
		row:SetPoint("TOPLEFT", content, "TOPLEFT", 0, -(i - 1) * 24)
		row:SetPoint("RIGHT", content, "RIGHT", 0, 0)
		row.member = member
		row.name:SetText(member.name)
		row.name:SetTextColor(ClassColor(member.classFile))
		row.level:SetText(member.level and ("niv. " .. member.level) or "")
		row.selectedTex:SetShown(member.name == selectedMemberName)
		row:Show()
		if member.name == selectedMemberName then
			selected = member
		end
	end
	content:SetHeight(math.max(#roster * 24, 1))
	if selected then
		ShowMemberDetail(selected)
	else
		selectedMemberName = nil
		mainFrame.memberDetail:Hide()
		mainFrame.memberHint:Show()
	end
end

-- Onglet Commandes ----------------------------------------------------------------

local orderRowPool = {}
local STATUS_DISPLAY = {
	open = { label = "ouverte", color = "|cffffd100" },
	accepted = { label = "acceptée", color = "|cff6699ff" },
	done = { label = "terminée", color = "|cff33ff66" },
	cancelled = { label = "annulée", color = "|cff999999" },
}
local ORDER_ROW_HEIGHT = 44

local function AcquireOrderRow(i, parent)
	local row = orderRowPool[i]
	if not row then
		row = CreateFrame("Frame", nil, parent)
		row:SetHeight(ORDER_ROW_HEIGHT)

		row.line1 = row:CreateFontString(nil, "OVERLAY", "GameFontNormal")
		row.line1:SetPoint("TOPLEFT", 4, -4)
		row.line1:SetJustifyH("LEFT")
		row.line1:SetWidth(430)

		row.line2 = row:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
		row.line2:SetPoint("TOPLEFT", 4, -24)
		row.line2:SetJustifyH("LEFT")
		row.line2:SetWidth(430)
		row.line2:SetWordWrap(false)

		row.button2 = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
		row.button2:SetSize(80, 22)
		row.button2:SetPoint("RIGHT", row, "RIGHT", -4, 0)
		row.button1 = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
		row.button1:SetSize(80, 22)
		row.button1:SetPoint("RIGHT", row.button2, "LEFT", -4, 0)

		local stripe = row:CreateTexture(nil, "BACKGROUND")
		stripe:SetAllPoints()
		stripe:SetColorTexture(1, 1, 1, 0.03)
		row.stripe = stripe

		orderRowPool[i] = row
	end
	row:SetParent(parent)
	return row
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

local function RefreshOrders()
	local orders = GP.GetOrders()
	local me = GP.GetPlayerFullName()
	local content = mainFrame.ordersContent
	for _, row in ipairs(orderRowPool) do
		row:Hide()
	end
	for i, order in ipairs(orders) do
		local row = AcquireOrderRow(i, content)
		row:ClearAllPoints()
		row:SetPoint("TOPLEFT", content, "TOPLEFT", 0, -(i - 1) * ORDER_ROW_HEIGHT)
		row:SetPoint("RIGHT", content, "RIGHT", 0, 0)
		row.stripe:SetShown(i % 2 == 0)

		local display = STATUS_DISPLAY[order.status] or { label = order.status, color = "|cffffffff" }
		local reference = order.id and ("#" .. order.id) or "local"
		local pending = order.pendingSync and " |cff999999(sync au prochain /reload)|r" or ""
		row.line1:SetText(("%s — %d× %s   %s%s|r%s"):format(reference, order.qty, order.item, display.color, display.label, pending))
		row.line2:SetText(("%s → %s%s"):format(order.requester, order.crafter, order.note and ("  —  " .. order.note) or ""))

		local isCrafter = order.crafter == me
		local isRequester = order.requester == me
		local active = order.status == "open" or order.status == "accepted"

		local label1, handler1
		if isCrafter and order.status == "open" then
			label1, handler1 = "Accepter", function() GP.SetOrderStatus(order, "accepted") end
		elseif isCrafter and order.status == "accepted" then
			label1, handler1 = "Terminé", function() GP.SetOrderStatus(order, "done") end
		end
		local label2, handler2
		if (isCrafter or isRequester) and active then
			label2, handler2 = "Annuler", function() GP.SetOrderStatus(order, "cancelled") end
		end
		ConfigureOrderButton(row.button1, label1, handler1)
		ConfigureOrderButton(row.button2, label2, handler2)
		row:Show()
	end
	content:SetHeight(math.max(#orders * ORDER_ROW_HEIGHT, 1))
	mainFrame.ordersEmptyText:SetShown(#orders == 0)
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

	local tab3 = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
	tab3:SetSize(110, 24)
	tab3:SetPoint("LEFT", tab2, "RIGHT", 6, 0)
	tab3:SetText("Commandes")
	tab3:SetScript("OnClick", function() SelectTab(3) end)
	frame.tabButton3 = tab3

	-- Barre du bas : import/export manuel + bandeau d'état
	local importButton = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
	importButton:SetSize(90, 22)
	importButton:SetPoint("BOTTOMLEFT", 14, 10)
	importButton:SetText("Importer")
	importButton:SetScript("OnClick", OpenImportDialog)

	local exportButton = CreateFrame("Button", nil, frame, "UIPanelButtonTemplate")
	exportButton:SetSize(90, 22)
	exportButton:SetPoint("LEFT", importButton, "RIGHT", 6, 0)
	exportButton:SetText("Exporter")
	exportButton:SetScript("OnClick", OpenExportDialog)

	local banner = frame:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	banner:SetPoint("BOTTOM", 40, 14)
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

	-- Colonne gauche : liste des membres
	local membersScroll = CreateFrame("ScrollFrame", nil, tabMembers, "UIPanelScrollFrameTemplate")
	membersScroll:SetPoint("TOPLEFT", 0, 0)
	membersScroll:SetPoint("BOTTOMLEFT", 0, 0)
	membersScroll:SetWidth(210)
	local membersContent = CreateFrame("Frame", nil, membersScroll)
	membersContent:SetSize(1, 1)
	membersScroll:SetScrollChild(membersContent)
	membersScroll:SetScript("OnSizeChanged", function(_, width)
		membersContent:SetWidth(width)
	end)
	frame.membersContent = membersContent

	local memberHint = tabMembers:CreateFontString(nil, "OVERLAY", "GameFontDisable")
	memberHint:SetPoint("CENTER", 110, 0)
	memberHint:SetText("Sélectionne un membre pour voir\nson équipement et son apparence.")
	frame.memberHint = memberHint

	-- Panneau de droite : armurerie du membre sélectionné
	local detail = CreateFrame("Frame", nil, tabMembers, "BackdropTemplate")
	detail:SetPoint("TOPLEFT", membersScroll, "TOPRIGHT", 30, 0)
	detail:SetPoint("BOTTOMRIGHT", 0, 0)
	detail:SetBackdrop({ bgFile = "Interface\\Buttons\\WHITE8x8" })
	detail:SetBackdropColor(0, 0, 0, 0.3)
	detail:Hide()
	frame.memberDetail = detail

	detail.title = detail:CreateFontString(nil, "OVERLAY", "GameFontNormalLarge")
	detail.title:SetPoint("TOPLEFT", 12, -10)

	detail.subtitle = detail:CreateFontString(nil, "OVERLAY", "GameFontHighlightSmall")
	detail.subtitle:SetPoint("TOPLEFT", 13, -32)
	detail.subtitle:SetTextColor(0.8, 0.8, 0.8)

	detail.model = CreateFrame("DressUpModel", nil, detail)
	detail.model:SetPoint("TOPLEFT", 12, -50)
	detail.model:SetSize(220, 330)

	detail.modelFallback = detail:CreateFontString(nil, "OVERLAY", "GameFontDisable")
	detail.modelFallback:SetPoint("CENTER", detail.model, "CENTER")
	detail.modelFallback:SetText("Aperçu 3D indisponible\npour ce personnage.")

	detail.modelNote = detail:CreateFontString(nil, "OVERLAY", "GameFontDisableSmall")
	detail.modelNote:SetPoint("TOP", detail.model, "BOTTOM", 0, -2)

	detail.equipList = CreateFrame("Frame", nil, detail)
	detail.equipList:SetPoint("TOPLEFT", detail.model, "TOPRIGHT", 14, 0)
	detail.equipList:SetPoint("BOTTOMRIGHT", -10, 10)

	detail.equipEmpty = detail:CreateFontString(nil, "OVERLAY", "GameFontDisable")
	detail.equipEmpty:SetPoint("TOPLEFT", detail.equipList, "TOPLEFT", 0, -4)
	detail.equipEmpty:SetText("Aucun équipement connu — données à venir\nau prochain scan de ce membre.")

	-- === Onglet 3 : Commandes ===
	local tabOrders = CreateFrame("Frame", nil, frame)
	tabOrders:SetPoint("TOPLEFT", 10, -70)
	tabOrders:SetPoint("BOTTOMRIGHT", -10, 34)
	tabOrders:Hide()
	frame.tabOrders = tabOrders

	local ordersHeader = tabOrders:CreateFontString(nil, "OVERLAY", "GameFontNormalSmall")
	ordersHeader:SetPoint("TOPLEFT", 4, -2)
	ordersHeader:SetText("|cffffd100Commandes de craft — reçues et passées|r")

	local ordersScroll = CreateFrame("ScrollFrame", nil, tabOrders, "UIPanelScrollFrameTemplate")
	ordersScroll:SetPoint("TOPLEFT", 0, -20)
	ordersScroll:SetPoint("BOTTOMRIGHT", -26, 0)
	local ordersContent = CreateFrame("Frame", nil, ordersScroll)
	ordersContent:SetSize(1, 1)
	ordersScroll:SetScrollChild(ordersContent)
	ordersScroll:SetScript("OnSizeChanged", function(_, width)
		ordersContent:SetWidth(width)
	end)
	frame.ordersContent = ordersContent

	local ordersEmptyText = tabOrders:CreateFontString(nil, "OVERLAY", "GameFontDisable")
	ordersEmptyText:SetPoint("CENTER", ordersScroll, "CENTER")
	ordersEmptyText:SetText("Aucune commande. Onglet Métiers → bouton « Commander » sur un artisan.")
	ordersEmptyText:Hide()
	frame.ordersEmptyText = ordersEmptyText

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
	elseif state.tab == 2 then
		RefreshMembers()
	elseif state.tab == 3 then
		RefreshOrders()
	end
	UpdateBanner()
end
