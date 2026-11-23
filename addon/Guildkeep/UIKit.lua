local _, GP = ...

-- Boîte à outils d'interface (socle), bâtie sur les gabarits natifs du client
-- Mainline (ButtonFrameTemplate, ScrollBox, SearchBoxTemplate…) pour se fondre
-- dans l'UI Blizzard. Partagée par les onglets de tous les modules, avec les
-- fenêtres communes : recettes d'un artisan et texte à copier.

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

-- État d'affichage partagé (filtres de chaque onglet, onglet choisi), conservé entre ouvertures.
local state = {
	tab = nil, -- clé de l'onglet affiché
	profession = nil,
	minLevel = 0,
	search = "",
	sortKey = "prof",
	sortAsc = true,
	memberSearch = "",
	ordersFilter = "all",
	showClosed = false,
}

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

local function RecipeCount(prof)
	return prof.recipes and #prof.recipes or 0
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
-- Boutons et champs -----------------------------------------------------------------------


local function Button(parent, text, width, onClick)
	local button = CreateFrame("Button", nil, parent, "UIPanelButtonTemplate")
	button:SetSize(width, 22)
	button:SetText(text)
	button:SetScript("OnClick", onClick)
	return button
end

-- Bouton à bascule (filtres, réponses, type) : fond doré et soulignement quand choisi.
local function Toggle(parent, text, onClick)
	local button = CreateFrame("Button", nil, parent)
	button:SetHeight(22)
	AddRowBackground(button)
	button.stripe:Hide()
	button.text = Label(button, text, "GameFontHighlight")
	button.text:SetPoint("CENTER")
	button:SetWidth(button.text:GetStringWidth() + 20)
	button.underline = button:CreateTexture(nil, "ARTWORK")
	button.underline:SetColorTexture(unpack(COLOR.gold))
	button.underline:SetHeight(2)
	button.underline:SetPoint("BOTTOMLEFT")
	button.underline:SetPoint("BOTTOMRIGHT")
	button:SetScript("OnClick", function(self)
		PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		onClick(self)
	end)
	function button:GPSetSelected(selected, color)
		self.selectedTex:SetShown(selected)
		self.underline:SetShown(selected)
		self.text:SetTextColor(unpack(selected and (color or COLOR.gold) or COLOR.text))
	end
	return button
end

local function InputBox(parent, width, maxLetters, numeric)
	local box = CreateFrame("EditBox", nil, parent, "InputBoxTemplate")
	box:SetSize(width, 20)
	box:SetAutoFocus(false)
	box:SetMaxLetters(maxLetters)
	if numeric then
		box:SetNumeric(true)
		box:SetJustifyH("CENTER")
	end
	box:SetScript("OnEnterPressed", box.ClearFocus)
	box:SetScript("OnEscapePressed", box.ClearFocus)
	return box
end

-- Boîte à outils partagée avec les onglets des modules.
GP.UI = {
	COLOR = COLOR,
	Hex = Hex,
	Paint = Paint,
	Label = Label,
	CreateWindow = CreateWindow,
	SetWindowTitle = SetWindowTitle,
	SetWindowPortrait = SetWindowPortrait,
	AddRowBackground = AddRowBackground,
	CreateScrollList = CreateScrollList,
	CreateSearchBox = CreateSearchBox,
	SetCheckLabel = SetCheckLabel,
	Button = Button,
	Toggle = Toggle,
	InputBox = InputBox,
	ClassColor = ClassColor,
	SetClassIcon = SetClassIcon,
	RankName = RankName,
	RecipeCount = RecipeCount,
	RebuildGuildOnline = RebuildGuildOnline,
	IsOnline = IsOnline,
	SetOnlineIcon = SetOnlineIcon,
	AddOnlineLine = AddOnlineLine,
	State = state,
}

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
		local data = self:GetParent().data
		-- Service offert par le module Artisanat (absent s'il est désactivé).
		local order = GP.Modules.Service("craft.order")
		if order then
			order(recipesDialog.crafterName, data.name, data.icon, data.id)
		end
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
	row.orderButton:SetShown(recipesDialog.canOrder and GP.Modules.Service("craft.order") ~= nil)
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
	local dialog = CreateWindow("GuildkeepRecipesDialog", 400, 520, "DIALOG")
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
	local mainFrame = GP.UI.GetMainFrame and GP.UI.GetMainFrame()
	if mainFrame and mainFrame:IsShown() then
		dialog:ClearAllPoints()
		dialog:SetPoint("TOPLEFT", mainFrame, "TOPRIGHT", 4, 0)
	end
	RefreshRecipesDialog()
	dialog:Show()
end

-- Fenêtre des recettes ouverte (pour y accoler le dialogue de commande).
function GP.UI.GetRecipesDialog()
	return recipesDialog
end

-- Fenêtre de texte (import, export, texte à copier) -----------------------------------------

local textDialog

local function CreateTextDialog()
	local dialog = CreateWindow("GuildkeepTextDialog", 540, 360, "DIALOG")
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

-- Fenêtre de texte partagée (import et export dans MainFrame.lua).
function GP.UI.TextDialog()
	textDialog = textDialog or CreateTextDialog()
	return textDialog
end

-- Texte à copier (roster d'un événement…) dans la même fenêtre que l'export.
function GP.UI.OpenCopyDialog(title, hint, text)
	textDialog = textDialog or CreateTextDialog()
	local dialog = textDialog
	SetWindowTitle(dialog, title)
	dialog.hint:SetText(hint)
	dialog.actionButton:Hide()
	dialog.status:SetText("")
	dialog.editBox:SetText(text)
	dialog.editBox:HighlightText()
	dialog.editBox:SetFocus()
	dialog.editBox:SetScript("OnTextChanged", function(self, userInput)
		if userInput then
			self:SetText(text)
			self:HighlightText()
		end
	end)
	dialog:Show()
end
