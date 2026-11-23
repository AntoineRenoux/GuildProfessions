local _, GP = ...

-- Module Artisanat, partie interface : l'onglet Commandes (commandes passées
-- et reçues, demandes ouvertes et offres), le catalogue des recettes de la
-- guilde et le dialogue « Commander » (composants à fournir). Offre au socle
-- le service « craft.order », utilisé par la fenêtre des recettes.

local Craft = GP.Craft
local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label
local CreateWindow, SetWindowTitle, SetWindowPortrait = UI.CreateWindow, UI.SetWindowTitle, UI.SetWindowPortrait
local AddRowBackground, CreateScrollList, CreateSearchBox, SetCheckLabel = UI.AddRowBackground, UI.CreateScrollList, UI.CreateSearchBox, UI.SetCheckLabel
local IsOnline = UI.IsOnline
local state = UI.State

local mainFrame -- fenêtre principale, connue à la construction de l'onglet

-- Dialogue « Commander » + composants à fournir -------------------------------------------
--
-- Quand A commande à B, A voit ce qu'il doit envoyer : les composants directs
-- de la recette, multipliés par la quantité, avec ce qu'il a déjà dans ses
-- sacs. Un composant lui-même fabricable dans la guilde porte un [+] : le
-- déplier montre ses propres composants (et qui sait le fabriquer). Replié
-- par défaut : la liste reste celle de « quoi envoyer ».

local orderDialog
local schematicCache = {}
local producerIndex = {}
local expandedPaths = {}
local MAX_TREE_DEPTH = 4
local TREE_ROW_HEIGHT = 22
local TREE_INDENT = 16

-- Composants obligatoires d'une recette (API Mainline, valable pour une
-- recette que le joueur ne connaît pas). nil si le client ne la décrit pas.
local function GetSchematic(recipeID)
	local cached = schematicCache[recipeID]
	if cached ~= nil then
		return cached or nil
	end
	local result = false
	if C_TradeSkillUI and C_TradeSkillUI.GetRecipeSchematic then
		local ok, schematic = pcall(C_TradeSkillUI.GetRecipeSchematic, recipeID, false)
		if ok and schematic then
			local reagents = {}
			for _, slot in ipairs(schematic.reagentSlotSchematics or {}) do
				local first = slot.reagents and slot.reagents[1]
				if slot.required ~= false and first and first.itemID then
					reagents[#reagents + 1] = { itemID = first.itemID, quantity = slot.quantityRequired or 1 }
				end
			end
			result = {
				outputItemID = schematic.outputItemID,
				yield = math.max(1, schematic.quantityMin or 1),
				reagents = reagents,
			}
		end
	end
	schematicCache[recipeID] = result
	return result or nil
end

-- Objet → recette de la guilde qui le produit, et qui sait la faire.
local function BuildProducerIndex()
	wipe(producerIndex)
	for _, char in ipairs(GP.GetRoster()) do
		for _, prof in ipairs(char.professions or {}) do
			for _, recipeID in ipairs(prof.recipes or {}) do
				local schematic = GetSchematic(recipeID)
				local itemID = schematic and schematic.outputItemID
				if itemID then
					local entry = producerIndex[itemID]
					if not entry then
						entry = { recipeID = recipeID, crafters = {}, seen = {} }
						producerIndex[itemID] = entry
					end
					if not entry.seen[char.name] then
						entry.seen[char.name] = true
						entry.crafters[#entry.crafters + 1] = char.name
					end
				end
			end
		end
	end
end

local function ItemCount(itemID)
	if C_Item and C_Item.GetItemCount then
		local ok, count = pcall(C_Item.GetItemCount, itemID, true)
		if ok then
			return count or 0
		end
	end
	return GetItemCount and GetItemCount(itemID, true) or 0
end

local RefreshOrderTree

-- Nom et icône ; un objet pas encore en cache rafraîchit l'arbre à son arrivée.
local function ItemNameAndIcon(itemID)
	local name = C_Item and C_Item.GetItemNameByID and C_Item.GetItemNameByID(itemID)
	local icon = C_Item and C_Item.GetItemIconByID and C_Item.GetItemIconByID(itemID)
	if not name and Item and Item.CreateFromItemID then
		Item:CreateFromItemID(itemID):ContinueOnItemLoad(function()
			if orderDialog and orderDialog:IsShown() and not orderDialog.refreshQueued then
				orderDialog.refreshQueued = true
				C_Timer.After(0.1, function()
					orderDialog.refreshQueued = false
					RefreshOrderTree()
				end)
			end
		end)
	end
	return name or "Chargement...", icon or "Interface\\Icons\\INV_Misc_QuestionMark"
end

-- Aplatit l'arbre en lignes visibles (seuls les nœuds dépliés descendent).
local function AddTreeRows(out, itemID, quantity, depth, path, ancestry)
	local producer = producerIndex[itemID]
	if depth >= MAX_TREE_DEPTH or ancestry[itemID] then
		producer = nil -- garde-fou : profondeur et recettes circulaires
	end
	local node = { itemID = itemID, quantity = quantity, depth = depth, path = path, producer = producer }
	out[#out + 1] = node
	if producer and expandedPaths[path] then
		local schematic = GetSchematic(producer.recipeID)
		if schematic then
			local crafts = math.ceil(quantity / schematic.yield)
			ancestry[itemID] = true
			for _, reagent in ipairs(schematic.reagents) do
				AddTreeRows(out, reagent.itemID, reagent.quantity * crafts, depth + 1, path .. ">" .. reagent.itemID, ancestry)
			end
			ancestry[itemID] = nil
		end
	end
end

local function TreeRowTooltip(row)
	local node = row.node
	GameTooltip:SetOwner(row, "ANCHOR_RIGHT")
	if not pcall(GameTooltip.SetItemByID, GameTooltip, node.itemID) then
		GameTooltip:SetText(row.name:GetText())
	end
	GameTooltip:AddLine(" ")
	if node.producer then
		GameTooltip:AddLine("Fabricable dans la guilde par : " .. table.concat(node.producer.crafters, ", "), unpack(COLOR.positive))
		GameTooltip:AddLine(expandedPaths[node.path] and "Clic : masquer ses composants" or "Clic : voir ses composants", unpack(COLOR.gold))
	end
	GameTooltip:AddLine("Maj+clic : lien dans le chat", unpack(COLOR.muted))
	GameTooltip:Show()
end

local function SetupTreeRow(row)
	AddRowBackground(row)
	row:RegisterForClicks("LeftButtonUp")

	row.toggle = row:CreateTexture(nil, "ARTWORK")
	row.toggle:SetSize(14, 14)

	row.icon = row:CreateTexture(nil, "ARTWORK")
	row.icon:SetSize(18, 18)
	row.icon:SetPoint("LEFT", row.toggle, "RIGHT", 4, 0)
	row.icon:SetTexCoord(0.08, 0.92, 0.08, 0.92)

	row.have = Label(row, nil, "GameFontHighlightSmall")
	row.have:SetPoint("RIGHT", -6, 0)
	row.have:SetWidth(70)
	row.have:SetJustifyH("RIGHT")

	row.need = Label(row, nil, "GameFontHighlight")
	row.need:SetPoint("RIGHT", row.have, "LEFT", -6, 0)
	row.need:SetWidth(56)
	row.need:SetJustifyH("RIGHT")

	row.name = Label(row, nil, "GameFontHighlight")
	row.name:SetPoint("LEFT", row.icon, "RIGHT", 6, 0)
	row.name:SetPoint("RIGHT", row.need, "LEFT", -6, 0)
	row.name:SetWordWrap(false)

	row:SetScript("OnEnter", TreeRowTooltip)
	row:SetScript("OnLeave", GameTooltip_Hide)
	row:SetScript("OnClick", function(self)
		local node = self.node
		if IsModifiedClick("CHATLINK") then
			local _, link = (C_Item and C_Item.GetItemInfo or GetItemInfo)(node.itemID)
			if link then
				ChatEdit_InsertLink(link)
			end
			return
		end
		if node.producer then
			expandedPaths[node.path] = not expandedPaths[node.path] or nil
			PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
			RefreshOrderTree()
			if self:IsMouseOver() then
				TreeRowTooltip(self)
			end
		end
	end)
end

local function UpdateTreeRow(row, node)
	row.node = node
	row.stripe:SetShown(node.index % 2 == 0)
	row.toggle:ClearAllPoints()
	row.toggle:SetPoint("LEFT", 6 + node.depth * TREE_INDENT, 0)
	if node.producer then
		row.toggle:SetTexture(expandedPaths[node.path] and "Interface\\Buttons\\UI-MinusButton-Up" or "Interface\\Buttons\\UI-PlusButton-Up")
		row.toggle:SetAlpha(1)
	else
		row.toggle:SetTexture(nil)
	end
	local name, icon = ItemNameAndIcon(node.itemID)
	row.icon:SetTexture(icon)
	row.name:SetText(name)
	-- Sous-composants en retrait et plus discrets : ils ne sont pas à envoyer.
	row.name:SetTextColor(unpack(node.depth == 0 and COLOR.text or { 0.8, 0.8, 0.8 }))
	row.need:SetText("×" .. node.quantity)
	local have = ItemCount(node.itemID)
	if node.depth == 0 then
		local enough = have >= node.quantity
		local mark = enough and "|TInterface\\RaidFrame\\ReadyCheck-Ready:12|t " or "|TInterface\\RaidFrame\\ReadyCheck-NotReady:12|t "
		row.have:SetText(mark .. Paint(enough and COLOR.positive or COLOR.negative, tostring(have)))
	else
		row.have:SetText(Paint(COLOR.muted, tostring(have)))
	end
end

RefreshOrderTree = function()
	local dialog = orderDialog
	if not dialog or not dialog:IsShown() then
		return
	end
	local schematic = dialog.recipeID and GetSchematic(dialog.recipeID)
	local available = schematic ~= nil and #schematic.reagents > 0
	dialog.treeArea:SetShown(available)
	dialog.unavailable:SetShown(not available)
	if not available then
		dialog.yieldText:SetText("")
		return
	end

	local qty = math.max(1, tonumber(dialog.qtyBox:GetText()) or 1)
	local crafts = math.ceil(qty / schematic.yield)
	dialog.yieldText:SetText(schematic.yield > 1
		and Paint(COLOR.muted, ("%d par fabrication, soit %d fabrication%s"):format(schematic.yield, crafts, crafts > 1 and "s" or ""))
		or "")

	local rows = {}
	local ancestry = { [schematic.outputItemID or 0] = true }
	for _, reagent in ipairs(schematic.reagents) do
		AddTreeRows(rows, reagent.itemID, reagent.quantity * crafts, 0, tostring(reagent.itemID), ancestry)
	end
	local missing, direct, craftable = 0, 0, false
	for i, node in ipairs(rows) do
		node.index = i
		if node.depth == 0 then
			direct = direct + 1
			if ItemCount(node.itemID) < node.quantity then
				missing = missing + 1
			end
			craftable = craftable or node.producer ~= nil
		end
	end
	dialog.treeList:GPSetItems(rows)
	dialog.legend:SetShown(craftable)

	if missing == 0 then
		dialog.summary:SetText(Paint(COLOR.positive, "Tu as tous les composants dans tes sacs ou ta banque."))
	else
		dialog.summary:SetText(Paint(COLOR.negative, ("Il te manque %d composant%s sur %d."):format(missing, missing > 1 and "s" or "", direct)))
	end
end

local function CreateOrderDialog()
	local dialog = CreateWindow("GuildkeepOrderDialog", 460, 470, "FULLSCREEN_DIALOG")
	local inset = dialog.Inset

	dialog.itemText = Label(dialog, nil, "GameFontHighlight", COLOR.gold)
	dialog.itemText:SetPoint("TOPLEFT", 66, -34)
	dialog.itemText:SetPoint("RIGHT", -12, 0)
	dialog.itemText:SetWordWrap(false)

	local qtyLabel = Label(inset, "Quantité", "GameFontNormal")
	qtyLabel:SetPoint("TOPLEFT", 12, -14)
	dialog.qtyBox = CreateFrame("EditBox", nil, inset, "InputBoxTemplate")
	dialog.qtyBox:SetSize(46, 20)
	dialog.qtyBox:SetPoint("LEFT", qtyLabel, "RIGHT", 14, 0)
	dialog.qtyBox:SetAutoFocus(false)
	dialog.qtyBox:SetNumeric(true)
	dialog.qtyBox:SetMaxLetters(3)
	dialog.qtyBox:SetJustifyH("CENTER")
	dialog.qtyBox:SetScript("OnTextChanged", function()
		RefreshOrderTree()
	end)
	dialog.yieldText = Label(inset, nil, "GameFontHighlightSmall")
	dialog.yieldText:SetPoint("LEFT", dialog.qtyBox, "RIGHT", 10, 0)

	local noteLabel = Label(inset, "Note", "GameFontNormal")
	noteLabel:SetPoint("LEFT", dialog.qtyBox, "RIGHT", 150, 0)
	dialog.noteBox = CreateFrame("EditBox", nil, inset, "InputBoxTemplate")
	dialog.noteBox:SetSize(150, 20)
	dialog.noteBox:SetPoint("LEFT", noteLabel, "RIGHT", 10, 0)
	dialog.noteBox:SetAutoFocus(false)
	dialog.noteBox:SetMaxLetters(120)

	-- Zone des composants
	local treeArea = CreateFrame("Frame", nil, inset)
	treeArea:SetPoint("TOPLEFT", 4, -44)
	treeArea:SetPoint("BOTTOMRIGHT", -4, 4)
	dialog.treeArea = treeArea

	dialog.componentsTitle = Label(treeArea, nil, "GameFontNormal")
	dialog.componentsTitle:SetPoint("TOPLEFT", 8, -2)

	local headerName = Label(treeArea, "Composant", "GameFontNormalSmall")
	headerName:SetPoint("TOPLEFT", 8, -24)
	local headerHave = Label(treeArea, "Tu as", "GameFontNormalSmall")
	headerHave:SetPoint("TOPRIGHT", -24, -24)
	local headerNeed = Label(treeArea, "Quantité", "GameFontNormalSmall")
	headerNeed:SetPoint("TOPRIGHT", -100, -24)
	local divider = treeArea:CreateTexture(nil, "ARTWORK")
	divider:SetColorTexture(1, 0.82, 0, 0.25)
	divider:SetHeight(1)
	divider:SetPoint("TOPLEFT", 4, -40)
	divider:SetPoint("TOPRIGHT", -4, -40)

	dialog.treeList = CreateScrollList(treeArea, TREE_ROW_HEIGHT, SetupTreeRow, UpdateTreeRow)
	dialog.treeList:SetPoint("TOPLEFT", 0, -44)
	dialog.treeList:SetPoint("BOTTOMRIGHT", -18, 44)

	dialog.legend = Label(treeArea, nil, "GameFontHighlightSmall", COLOR.muted)
	dialog.legend:SetPoint("BOTTOMLEFT", 8, 26)
	dialog.legend:SetPoint("RIGHT", -8, 0)
	dialog.legend:SetText("|TInterface\\Buttons\\UI-PlusButton-Up:14|t fabricable dans la guilde : clique pour voir ses propres composants")

	dialog.summary = Label(treeArea, nil, "GameFontHighlight")
	dialog.summary:SetPoint("BOTTOMLEFT", 8, 6)
	dialog.summary:SetPoint("RIGHT", -8, 0)

	dialog.unavailable = Label(inset, "Le client ne décrit pas les composants de cette recette.\nDemande-les directement à l'artisan.",
		"GameFontHighlight", COLOR.muted)
	dialog.unavailable:SetPoint("TOP", 0, -80)
	dialog.unavailable:SetJustifyH("CENTER")

	local function Submit()
		local qty = tonumber(dialog.qtyBox:GetText()) or 0
		if qty < 1 then
			dialog.qtyBox:SetText("1")
			dialog.qtyBox:HighlightText()
			return
		end
		Craft.CreateOrder(dialog.crafter, dialog.item, qty, dialog.noteBox:GetText(), dialog.recipeID)
		dialog:Hide()
		PlaySound(SOUNDKIT.IG_QUEST_LIST_COMPLETE or SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		if dialog.crafter then
			print(("|cff33ff99Guildkeep|r — commande de %d× %s envoyée à %s. Synchronisée au prochain /reload ou déco.")
				:format(qty, dialog.item, dialog.crafter))
		else
			print(("|cff33ff99Guildkeep|r — demande de %d× %s publiée : les artisans connectés sont prévenus, elle reste ouverte 5 jours.")
				:format(qty, dialog.item))
		end
	end
	dialog.qtyBox:SetScript("OnEnterPressed", Submit)
	dialog.qtyBox:SetScript("OnEscapePressed", function() dialog:Hide() end)

	local cancel = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	cancel:SetSize(110, 22)
	cancel:SetPoint("BOTTOMRIGHT", -6, 4)
	cancel:SetText(CANCEL or "Annuler")
	cancel:SetScript("OnClick", function() dialog:Hide() end)

	local accept = CreateFrame("Button", nil, dialog, "UIPanelButtonTemplate")
	accept:SetSize(130, 22)
	accept:SetPoint("RIGHT", cancel, "LEFT", -4, 0)
	accept:SetText("Commander")
	accept:SetScript("OnClick", Submit)

	local hint = Label(dialog, "Entrée pour envoyer", "GameFontHighlightSmall", COLOR.muted)
	hint:SetPoint("BOTTOMLEFT", 10, 9)

	return dialog
end

function Craft.OpenOrderDialog(crafter, item, icon, recipeID)
	orderDialog = orderDialog or CreateOrderDialog()
	local dialog = orderDialog
	dialog.crafter = crafter
	dialog.item = item
	dialog.recipeID = recipeID
	wipe(expandedPaths)
	BuildProducerIndex()
	SetWindowTitle(dialog, crafter and ("Commande à " .. crafter) or "Demande à tous les artisans")
	SetWindowPortrait(dialog, icon or "Interface\\Icons\\INV_Misc_QuestionMark")
	dialog.itemText:SetText(item)
	dialog.componentsTitle:SetText(crafter and ("Composants à envoyer à " .. crafter) or "Composants à prévoir pour l'artisan")
	dialog.noteBox:SetText("")
	local recipesDialog = UI.GetRecipesDialog()
	if recipesDialog and recipesDialog:IsShown() then
		dialog:ClearAllPoints()
		dialog:SetPoint("TOPLEFT", recipesDialog, "TOPRIGHT", 4, 0)
	end
	dialog:Show()
	dialog.qtyBox:SetText("1")
	dialog.qtyBox:SetFocus()
	dialog.qtyBox:HighlightText()
	RefreshOrderTree()
end

-- Onglet Commandes ------------------------------------------------------------------------

local ordersList
local filterButtons = {}

local STATUS_DISPLAY = {
	open = { label = "ouverte", color = COLOR.gold, icon = "Interface\\GossipFrame\\AvailableQuestIcon" },
	accepted = { label = "acceptée", color = COLOR.info, icon = "Interface\\RaidFrame\\ReadyCheck-Waiting" },
	ready = { label = "prête à échanger", color = COLOR.positive, icon = "Interface\\GossipFrame\\ActiveQuestIcon" },
	mailed = { label = "envoyée par courrier", color = COLOR.positive, icon = "Interface\\Icons\\INV_Letter_15" },
	done = { label = "reçue", color = COLOR.positive, icon = "Interface\\RaidFrame\\ReadyCheck-Ready" },
	cancelled = { label = "annulée", color = COLOR.muted, icon = "Interface\\RaidFrame\\ReadyCheck-NotReady" },
	expired = { label = "expirée", color = COLOR.muted, icon = "Interface\\RaidFrame\\ReadyCheck-NotReady" },
}

local function IsActiveOrder(order)
	return Craft.ORDER_ACTIVE[order.status] == true
end

-- Commandes qui attendent une action du joueur (pastille d'onglet, infobulle) :
-- à accepter ou fabriquer, offres à départager, réception à confirmer.
function Craft.CountOrdersToHandle()
	if not GuildkeepDB then
		return 0
	end
	local me, count = GP.GetPlayerFullName(), 0
	for _, order in ipairs(Craft.GetOrders()) do
		local crafterTurn = order.crafter == me and (order.status == "open" or order.status == "accepted" or order.status == "ready")
		local requesterTurn = order.requester == me and ((order.status == "open" and #order.offers > 0) or order.status == "mailed" or order.status == "ready")
		if crafterTurn or requesterTurn then
			count = count + 1
		end
	end
	return count
end

local function Who(name, me)
	if not name then
		return Paint(COLOR.muted, "tous les artisans")
	end
	return name == me and Paint(COLOR.gold, "toi") or name
end

-- Fenêtre des offres d'une demande : le demandeur en retient une.
local offersDialog

local function OpenOffersDialog(order)
	if not offersDialog then
		local dialog = CreateWindow("GuildkeepOffersDialog", 420, 300, "DIALOG")
		SetWindowPortrait(dialog, GP.ICON)
		dialog.rows = {}
		for i = 1, 8 do
			local row = CreateFrame("Frame", nil, dialog.Inset)
			row:SetHeight(28)
			row:SetPoint("TOPLEFT", 8, -8 - (i - 1) * 30)
			row:SetPoint("RIGHT", -8, 0)
			row.pick = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
			row.pick:SetSize(90, 22)
			row.pick:SetPoint("RIGHT")
			row.pick:SetText("Retenir")
			row.text = Label(row, nil, "GameFontHighlight")
			row.text:SetPoint("LEFT", 4, 0)
			row.text:SetPoint("RIGHT", row.pick, "LEFT", -8, 0)
			row.text:SetWordWrap(false)
			dialog.rows[i] = row
		end
		offersDialog = dialog
	end
	local dialog = offersDialog
	SetWindowTitle(dialog, ("Offres — %d× %s"):format(order.qty, order.item))
	for i, row in ipairs(dialog.rows) do
		local offer = order.offers[i]
		row:SetShown(offer ~= nil)
		if offer then
			local online = IsOnline(offer.crafter) and Paint(COLOR.positive, " ●") or ""
			row.text:SetText(offer.crafter .. online .. (offer.note and Paint(COLOR.muted, "  « " .. offer.note .. " »") or ""))
			row.pick:SetScript("OnClick", function()
				Craft.SetOrderStatus(order, "accepted", offer.crafter)
				dialog:Hide()
			end)
		end
	end
	dialog:Show()
end

-- Choisir une recette dans le catalogue de la guilde pour une demande ouverte à tous.
local catalogDialog

local function RefreshCatalogDialog()
	local dialog = catalogDialog
	local search = (dialog.search:GetText() or ""):lower()
	local crafters = {}
	for _, member in ipairs(GP.GetRoster()) do
		for _, prof in ipairs(member.professions or {}) do
			for _, id in ipairs(prof.recipes or {}) do
				crafters[id] = (crafters[id] or 0) + 1
			end
		end
	end
	local items = {}
	for id, entry in pairs(Craft.GetCatalog()) do
		if search == "" or entry.name:lower():find(search, 1, true) then
			items[#items + 1] = { id = id, name = entry.name, profession = entry.profession, crafters = crafters[id] or 0 }
		end
	end
	table.sort(items, function(a, b) return a.name < b.name end)
	for i, item in ipairs(items) do
		item.index = i
	end
	dialog.list:GPSetItems(items)
	dialog.count:SetText(("%d recette%s"):format(#items, #items > 1 and "s" or ""))
end

local function OpenCatalogDialog()
	if not catalogDialog then
		local dialog = CreateWindow("GuildkeepCatalogDialog", 480, 460, "DIALOG")
		SetWindowTitle(dialog, "Nouvelle demande à la guilde")
		SetWindowPortrait(dialog, GP.ICON)
		local hint = Label(dialog, "Choisis la recette : tous les artisans de la guilde pourront proposer de la faire.", "GameFontHighlightSmall", COLOR.muted)
		hint:SetPoint("TOPLEFT", 66, -34)
		hint:SetPoint("RIGHT", -12, 0)
		dialog.search = CreateSearchBox(dialog.Inset, 300, "Chercher une recette", function()
			RefreshCatalogDialog()
		end)
		dialog.search:SetPoint("TOPLEFT", 12, -8)
		dialog.count = Label(dialog.Inset, nil, "GameFontHighlightSmall", COLOR.muted)
		dialog.count:SetPoint("LEFT", dialog.search, "RIGHT", 10, 0)
		dialog.list = CreateScrollList(dialog.Inset, 24, function(row)
			AddRowBackground(row)
			row.icon = row:CreateTexture(nil, "ARTWORK")
			row.icon:SetSize(18, 18)
			row.icon:SetPoint("LEFT", 6, 0)
			row.name = Label(row, nil, "GameFontHighlight")
			row.name:SetPoint("LEFT", row.icon, "RIGHT", 6, 0)
			row.name:SetWidth(230)
			row.name:SetWordWrap(false)
			row.info = Label(row, nil, "GameFontHighlightSmall", COLOR.muted)
			row.info:SetPoint("LEFT", row.name, "RIGHT", 6, 0)
			row.info:SetPoint("RIGHT", -6, 0)
			row.info:SetWordWrap(false)
			row:SetScript("OnClick", function(self)
				local data = self.data
				catalogDialog:Hide()
				Craft.OpenOrderDialog(nil, data.name, GP.GetProfessionIcon({ name = data.profession }), data.id)
			end)
		end, function(row, data)
			row.data = data
			row.stripe:SetShown(data.index % 2 == 0)
			row.icon:SetTexture(GP.GetProfessionIcon({ name = data.profession }))
			row.name:SetText(data.name)
			row.info:SetText(data.profession .. " · " .. (data.crafters > 0 and Paint(COLOR.positive, ("%d artisan%s"):format(data.crafters, data.crafters > 1 and "s" or ""))
				or "personne ne la connaît encore"))
		end)
		dialog.list:SetPoint("TOPLEFT", 4, -36)
		dialog.list:SetPoint("BOTTOMRIGHT", -22, 4)
		catalogDialog = dialog
	end
	catalogDialog.search:SetText("")
	catalogDialog:Show()
	RefreshCatalogDialog()
	catalogDialog.search:SetFocus()
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

	row.button3 = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
	row.button3:SetSize(84, 22)
	row.button3:SetPoint("RIGHT", -6, 0)
	row.button2 = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
	row.button2:SetSize(84, 22)
	row.button2:SetPoint("RIGHT", row.button3, "LEFT", -4, 0)
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

-- Boutons d'une commande selon le rôle du joueur et l'étape.
local function OrderActions(order, me, canMake)
	local actions = {}
	local function add(label, handler)
		actions[#actions + 1] = { label, handler }
	end
	local isRequester, isCrafter = order.requester == me, order.crafter == me
	local cancel = function()
		StaticPopup_Show("GUILDKEEP_CANCEL_ORDER", order.item, nil, order)
	end
	if order.status == "open" then
		if isRequester then
			if #order.offers > 0 then
				add(("Offres (%d)"):format(#order.offers), function() OpenOffersDialog(order) end)
			end
			add(CANCEL or "Annuler", cancel)
		elseif isCrafter then
			add("Accepter", function() Craft.SetOrderStatus(order, "accepted") end)
			add("Refuser", cancel)
		elseif not order.crafter then
			if Craft.MyOffer(order) then
				add("Retirer l'offre", function() Craft.SetOffer(order, nil, true) end)
			else
				add(canMake and "Proposer" or "Proposer…", function()
					StaticPopup_Show("GUILDKEEP_OFFER", order.item, nil, order)
				end)
			end
		end
	elseif order.status == "accepted" or order.status == "ready" then
		if isCrafter then
			if order.status == "accepted" then
				add("Prête", function() Craft.SetOrderStatus(order, "ready") end)
			end
			add("Envoyée", function() Craft.SetOrderStatus(order, "mailed") end)
			add(CANCEL or "Annuler", cancel)
		elseif isRequester then
			add("Reçue", function() Craft.SetOrderStatus(order, "done") end)
			add(CANCEL or "Annuler", cancel)
		end
	elseif order.status == "mailed" and isRequester then
		add("Reçue", function() Craft.SetOrderStatus(order, "done") end)
	end
	return actions
end

local function UpdateOrderRow(row, data)
	local order, me = data.order, data.me
	row.stripe:SetShown(data.index % 2 == 0)
	local display = STATUS_DISPLAY[order.status] or { label = order.status or "?", color = COLOR.text }
	row.statusIcon:SetTexture(display.icon)
	row.statusIcon:SetShown(display.icon ~= nil)
	local pending = order.pendingSync and Paint(COLOR.muted, "  · synchro au prochain /reload") or ""
	local status = order.status == "open" and not order.crafter and "demande ouverte" or display.label
	local title = ("%d× %s"):format(order.qty, order.item)
	-- Titre vert : ta demande a reçu des offres.
	if order.requester == me and order.status == "open" and #order.offers > 0 then
		title = Paint(COLOR.positive, title)
	end
	row.line1:SetText(("%s   %s%s"):format(title, Paint(display.color, status), pending))
	local parts = { ("%s » %s"):format(Who(order.requester, me), Who(order.crafter, me)) }
	parts[#parts + 1] = order.id and ("#" .. order.id) or "locale"
	if order.status == "open" and #order.offers > 0 then
		local names = {}
		for _, offer in ipairs(order.offers) do
			names[#names + 1] = offer.crafter == me and "toi" or offer.crafter
		end
		parts[#parts + 1] = Paint(COLOR.positive, "offres : " .. table.concat(names, ", "))
	end
	if order.status == "open" and order.expiresTs then
		local days = math.max(0, math.floor((order.expiresTs - (GetServerTime and GetServerTime() or time())) / 86400))
		parts[#parts + 1] = days > 0 and ("expire dans %d j"):format(days) or "expire aujourd'hui"
	elseif order.createdAt then
		parts[#parts + 1] = order.createdAt
	end
	if order.note then
		parts[#parts + 1] = order.note
	end
	row.line2:SetText(table.concat(parts, "  ·  "))

	local actions = OrderActions(order, me, data.canMake)
	for i, button in ipairs({ row.button1, row.button2, row.button3 }) do
		local action = actions[i]
		ConfigureOrderButton(button, action and action[1], action and action[2])
	end
end

local function RefreshOrders()
	local me = GP.GetPlayerFullName()
	local ids, names = Craft.MyKnownRecipes()
	local known = { ids = ids, names = names }
	local items = {}
	for _, order in ipairs(Craft.GetOrders()) do
		local canMake = Craft.CanMake(order, known) and true or false
		local keep = state.showClosed or IsActiveOrder(order)
		if keep and state.ordersFilter == "crafter" then
			keep = order.crafter == me
		elseif keep and state.ordersFilter == "requester" then
			keep = order.requester == me
		elseif keep and state.ordersFilter == "canmake" then
			keep = order.status == "open" and not order.crafter and order.requester ~= me and canMake
		elseif keep and state.ordersFilter == "offers" then
			keep = Craft.MyOffer(order) ~= nil
		end
		if keep then
			items[#items + 1] = { order = order, me = me, canMake = canMake, index = #items + 1 }
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
	mainFrame = frame
	local toolbar = CreateFrame("Frame", nil, parent)
	toolbar:SetPoint("TOPLEFT", 8, -6)
	toolbar:SetPoint("TOPRIGHT", -8, -6)
	toolbar:SetHeight(24)

	local previous
	for _, def in ipairs({
		{ "all", "Toutes" },
		{ "crafter", "À fabriquer" },
		{ "requester", "Mes demandes" },
		{ "canmake", "Que je peux faire" },
		{ "offers", "Mes offres" },
	}) do
		local button = CreateFrame("Button", nil, toolbar)
		button:SetHeight(24)
		button.value = def[1]
		AddRowBackground(button)
		button.stripe:Hide()
		button.text = Label(button, def[2], "GameFontHighlight")
		button.text:SetPoint("CENTER")
		button:SetWidth(button.text:GetStringWidth() + 20)
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
			button:SetPoint("LEFT", previous, "RIGHT", 2, 0)
		else
			button:SetPoint("LEFT")
		end
		filterButtons[#filterButtons + 1] = button
		previous = button
	end

	local newRequest = CreateFrame("Button", nil, toolbar, "UIPanelButtonTemplate")
	newRequest:SetSize(130, 22)
	newRequest:SetPoint("RIGHT", 0, 0)
	newRequest:SetText("Nouvelle demande")
	newRequest:SetScript("OnClick", OpenCatalogDialog)

	local showClosed = CreateFrame("CheckButton", nil, toolbar, "UICheckButtonTemplate")
	showClosed:SetSize(24, 24)
	showClosed:SetPoint("RIGHT", newRequest, "LEFT", -90, 0)
	SetCheckLabel(showClosed, "Terminées")
	showClosed:SetScript("OnClick", function(self)
		state.showClosed = self:GetChecked() and true or false
		GP.RefreshUI()
	end)

	ordersList = CreateScrollList(parent, 46, SetupOrderRow, UpdateOrderRow)
	ordersList:SetPoint("TOPLEFT", toolbar, "BOTTOMLEFT", -4, -8)
	ordersList:SetPoint("BOTTOMRIGHT", parent, "BOTTOMRIGHT", -22, 4)

	frame.ordersEmptyText = Label(parent, "Aucune commande ici.\n« Nouvelle demande » pour demander à tous les artisans, ou onglet Métiers, « Voir » sur un artisan, puis « Commander ».",
		"GameFontHighlight", COLOR.muted)
	frame.ordersEmptyText:SetPoint("CENTER", ordersList, "CENTER")
	frame.ordersEmptyText:SetJustifyH("CENTER")
	frame.ordersEmptyText:SetWidth(520)
end

-- Fenêtres de confirmation, déclarées au chargement du module.
local function DefinePopups()
	StaticPopupDialogs["GUILDKEEP_CANCEL_ORDER"] = {
		text = "Annuler la commande « %s » ?",
		button1 = YES or "Oui",
		button2 = NO or "Non",
		OnAccept = function(_, order)
			Craft.SetOrderStatus(order, "cancelled")
		end,
		timeout = 0,
		whileDead = true,
		hideOnEscape = true,
		showAlert = true,
	}

	StaticPopupDialogs["GUILDKEEP_OFFER"] = {
		text = "Proposer de faire « %s » ?\nUn mot pour le demandeur (facultatif : prix, délai…)",
		button1 = "Proposer",
		button2 = CANCEL or "Annuler",
		hasEditBox = true,
		maxLetters = 120,
		OnAccept = function(self, order)
			local box = self.editBox or self.EditBox
			Craft.SetOffer(order, box and box:GetText() or nil, false)
		end,
		EditBoxOnEnterPressed = function(self)
			local parent = self:GetParent()
			Craft.SetOffer(parent.data, self:GetText(), false)
			parent:Hide()
		end,
		timeout = 0,
		whileDead = true,
		hideOnEscape = true,
	}
end


GP.Modules.Define("craft", "ui", function(ctx)
	DefinePopups()
	ctx:Provide("craft.order", Craft.OpenOrderDialog)
	-- Sacs modifiés pendant que la fenêtre est ouverte : colonne « Tu as » à jour.
	ctx:Listen({ "BAG_UPDATE_DELAYED" }, RefreshOrderTree)
	ctx:AddTab({
		label = "Commandes",
		key = "tabOrders",
		order = 30,
		build = BuildOrdersTab,
		refresh = RefreshOrders,
		badge = Craft.CountOrdersToHandle,
		badgeHint = function(count)
			return ("%d commande%s à traiter"):format(count, count > 1 and "s" or "")
		end,
	})
end)
