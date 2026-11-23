local _, GP = ...

-- Module Membres, partie interface : la liste des membres de la guilde et leur
-- armurerie (modèle 3D, équipement, niveau d'objet moyen, métiers). Un clic sur
-- un métier ouvre la fenêtre des recettes du socle.

local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label
local ClassColor, SetClassIcon, IsOnline, SetOnlineIcon = UI.ClassColor, UI.SetClassIcon, UI.IsOnline, UI.SetOnlineIcon
local AddRowBackground, CreateScrollList, CreateSearchBox = UI.AddRowBackground, UI.CreateScrollList, UI.CreateSearchBox
local RankName, RecipeCount = UI.RankName, UI.RecipeCount
local state = UI.State

local mainFrame -- fenêtre principale, connue à la construction de l'onglet

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
	button.icon:SetTexture(texture)
	local function applyQuality(quality)
		if button.link ~= link then
			return -- le bouton a été réutilisé entre-temps
		end
		-- Liseré à la couleur de qualité de l'objet ; gris sombre si vide ou inconnu.
		local r, g, b
		if quality and quality >= 1 then
			if C_Item and C_Item.GetItemQualityColor then
				r, g, b = C_Item.GetItemQualityColor(quality)
			elseif ITEM_QUALITY_COLORS and ITEM_QUALITY_COLORS[quality] then
				local c = ITEM_QUALITY_COLORS[quality]
				r, g, b = c.r, c.g, c.b
			end
		end
		button.border:SetColorTexture(r or 0.25, g or 0.22, b or 0.17, 1)
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
	-- Dessinée par l'addon : ItemButtonTemplate n'existe pas dans le client WoW: Forever,
	-- et son absence interrompait la construction de toute la fenêtre.
	local button = CreateFrame("Button", nil, parent)
	button:SetSize(SLOT_SIZE, SLOT_SIZE)
	button.border = button:CreateTexture(nil, "BACKGROUND")
	button.border:SetAllPoints()
	button.border:SetColorTexture(0.25, 0.22, 0.17, 1)
	button.icon = button:CreateTexture(nil, "ARTWORK")
	button.icon:SetPoint("TOPLEFT", 2, -2)
	button.icon:SetPoint("BOTTOMRIGHT", -2, 2)
	button.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)
	button:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square", "ADD")
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
	if not model then
		-- Modèle 3D indisponible dans ce client : l'équipement s'affiche quand même.
	elseif member.name == GP.GetPlayerFullName() then
		pcall(model.ClearModel, model)
		rendered = pcall(model.SetUnit, model, "player")
	elseif TargetMatchesMember(member.name) then
		pcall(model.ClearModel, model)
		rendered = pcall(model.SetUnit, model, "target")
		note = "rendu fidèle (cible)"
	elseif member.raceId and model.SetCustomRace then
		pcall(model.ClearModel, model)
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
	if model then
		if not rendered then
			pcall(model.ClearModel, model)
		end
		if rendered then
			SetModelFacing(model, 0.4)
		end
		model:SetShown(rendered)
	end
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
	mainFrame = frame
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

GP.Modules.Define("members", "ui", function(ctx)
	ctx:AddTab({
		label = "Membres",
		key = "tabMembers",
		order = 20,
		build = BuildMembersTab,
		refresh = RefreshMembers,
	})
end)
