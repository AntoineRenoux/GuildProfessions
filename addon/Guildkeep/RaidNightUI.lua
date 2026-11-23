local _, GP = ...

-- Module Soirée de raid, partie interface : sections « Réserves » et « Soirée »
-- du détail d'un événement, ajoutées à l'onglet Événements par
-- GP.EventsUI.Extend (dépendance déclarée). La logique vit dans RaidNight.lua ;
-- ici, on affiche et on relaie les clics.

local E, R = GP.Events, GP.RaidNight
local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label

local currentEvent

local function Selected()
	return currentEvent and E.Find(currentEvent.key)
end

local function Report(ok, err)
	if not ok and err then
		print("|cff33ff99Guildkeep|r — " .. err)
	end
end

-- Shift-clic sur un objet (sac, chat, butin) : le lien va dans notre champ s'il a
-- le focus. Branché au chargement du module (hooksecurefunc ne se retire pas :
-- sans champ créé, il ne fait rien).
local itemBoxes = {}
local function OnInsertLink(link)
	if type(link) ~= "string" or not link:find("|Hitem:") then
		return
	end
	for _, box in ipairs(itemBoxes) do
		if box:HasFocus() then
			box:SetText(link)
			return
		end
	end
end

local function ItemBox(parent, width)
	local box = UI.InputBox(parent, width, 255)
	itemBoxes[#itemBoxes + 1] = box
	box:SetScript("OnEnter", function(self)
		GameTooltip:SetOwner(self, "ANCHOR_TOP")
		GameTooltip:SetText("Objet")
		GameTooltip:AddLine("Shift-clic sur un objet (sacs, chat, butin, Atlas), ou son numéro.", 1, 1, 1, true)
		GameTooltip:Show()
	end)
	box:SetScript("OnLeave", GameTooltip_Hide)
	return box
end

local function ItemLabel(itemId, name)
	local text, link = GP.ItemInfo(itemId, name)
	return link or text
end

local function ShowItemTooltip(owner, itemId)
	GameTooltip:SetOwner(owner, "ANCHOR_RIGHT")
	local ok = pcall(GameTooltip.SetHyperlink, GameTooltip, "item:" .. itemId)
	if not ok then
		GameTooltip:SetText("Objet #" .. itemId)
	end
	GameTooltip:Show()
end

-- Onglet Réserves ----------------------------------------------------------------------------

local function SetupReserveRow(row)
	UI.AddRowBackground(row)
	row.icon = row:CreateTexture(nil, "ARTWORK")
	row.icon:SetSize(20, 20)
	row.icon:SetPoint("LEFT", 6, 0)
	row.hard = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
	row.hard:SetSize(70, 20)
	row.hard:SetPoint("RIGHT", -4, 0)
	row.name = Label(row, nil, "GameFontHighlight")
	row.name:SetPoint("LEFT", row.icon, "RIGHT", 6, 0)
	row.name:SetWidth(170)
	row.name:SetWordWrap(false)
	row.who = Label(row, nil, "GameFontHighlightSmall")
	row.who:SetPoint("LEFT", row.name, "RIGHT", 6, 0)
	row.who:SetPoint("RIGHT", row.hard, "LEFT", -6, 0)
	row.who:SetWordWrap(false)
	row:SetScript("OnEnter", function(self)
		if self.itemId then
			ShowItemTooltip(self, self.itemId)
		end
	end)
	row:SetScript("OnLeave", GameTooltip_Hide)
end

local function UpdateReserveRow(row, data)
	row.stripe:SetShown(data.index % 2 == 0)
	row.itemId = data.itemId
	local _, _, icon = GP.ItemInfo(data.itemId, data.itemName)
	row.icon:SetTexture(icon)
	row.name:SetText(ItemLabel(data.itemId, data.itemName))
	if data.hard then
		row.who:SetText(Paint(COLOR.gold, "Réservé d'office") .. (data.hard.holder and (" pour " .. data.hard.holder) or ""))
	else
		local parts = {}
		for _, r in ipairs(data.rows) do
			parts[#parts + 1] = ("%s %s"):format(r.character, Paint(COLOR.muted, "(" .. R.PriorityLabel(r.priority):lower() .. ")"))
		end
		local conflict = #data.rows > 1 and Paint(COLOR.negative, ("%d sur l'objet · "):format(#data.rows)) or ""
		row.who:SetText(conflict .. table.concat(parts, ", "))
	end
	row.hard:SetShown(data.canManage)
	row.hard:SetText(data.hard and "Lever" or "D'office")
	row.hard:SetScript("OnClick", function()
		local event = Selected()
		if event then
			Report(R.SetHardReserve(event, data.itemId, data.itemName, nil, not data.hard))
		end
	end)
end

local function BuildReserves(detail)
	local panel = CreateFrame("Frame", nil, detail)
	panel:SetPoint("TOPLEFT", detail.sectionTop, "BOTTOMLEFT", 0, -6)
	panel:SetPoint("BOTTOMRIGHT", 0, 28)

	local title = Label(panel, "Mes réservations", "GameFontNormal")
	title:SetPoint("TOPLEFT", 0, 0)
	panel.status = Label(panel, nil, "GameFontHighlightSmall", COLOR.muted)
	panel.status:SetPoint("LEFT", title, "RIGHT", 10, 0)

	panel.lock = UI.Button(panel, "Verrouiller", 100, function()
		local event = Selected()
		if not event then
			return
		end
		Report(E.SetLocked(event, not event.locked))
		if not event.locked and IsInRaid() and UnitIsGroupLeader("player") then
			StaticPopup_Show("GUILDKEEP_MASTER_LOOTER")
		end
	end)
	panel.lock:SetPoint("TOPRIGHT", 0, 2)

	-- Officier : réserver pour un joueur sans addon.
	panel.forBox = UI.InputBox(panel, 120, 64)
	panel.forBox:SetPoint("RIGHT", panel.lock, "LEFT", -10, 0)
	panel.forLabel = Label(panel, "Pour", "GameFontHighlightSmall", COLOR.muted)
	panel.forLabel:SetPoint("RIGHT", panel.forBox, "LEFT", -8, 0)
	panel.forBox:SetScript("OnTextChanged", function() GP.RefreshUI() end)

	panel.mine = {}
	local previous = title
	for _, priority in ipairs(R.PRIORITIES) do
		local line = CreateFrame("Frame", nil, panel)
		line:SetHeight(24)
		line:SetPoint("TOPLEFT", previous, "BOTTOMLEFT", 0, previous == title and -6 or -2)
		line:SetPoint("RIGHT", panel, "RIGHT", 0, 0)
		line.priority = priority
		line.label = Label(line, R.PRIORITY_LABELS[priority], "GameFontNormalSmall")
		line.label:SetPoint("LEFT", 0, 0)
		line.label:SetWidth(56)
		line.icon = line:CreateTexture(nil, "ARTWORK")
		line.icon:SetSize(18, 18)
		line.icon:SetPoint("LEFT", line.label, "RIGHT", 2, 0)
		line.current = Label(line, nil, "GameFontHighlightSmall")
		line.current:SetPoint("LEFT", line.icon, "RIGHT", 6, 0)
		line.current:SetWidth(150)
		line.current:SetWordWrap(false)
		line.warn = Label(line, nil, "GameFontHighlightSmall", COLOR.negative)
		line.warn:SetPoint("LEFT", line.current, "RIGHT", 4, 0)
		line.warn:SetWidth(18)
		line.clear = UI.Button(line, "Retirer", 70, function()
			local event = Selected()
			if event then
				Report(R.SetReserve(event, priority, 0, nil, panel.ForCharacter()))
			end
		end)
		line.clear:SetPoint("RIGHT", 0, 0)
		line.set = UI.Button(line, "Réserver", 76, function()
			local event = Selected()
			local itemId = R.ParseItem(line.box:GetText())
			if not event then
				return
			end
			if not itemId then
				Report(false, "objet illisible : shift-clic sur l'objet, ou son numéro")
				return
			end
			local name = GP.ItemInfo(itemId)
			local ok, err = R.SetReserve(event, priority, itemId, name, panel.ForCharacter())
			Report(ok, err)
			if ok then
				line.box:SetText("")
				line.box:ClearFocus()
			end
		end)
		line.set:SetPoint("RIGHT", line.clear, "LEFT", -2, 0)
		line.box = ItemBox(line, 120)
		line.box:SetPoint("RIGHT", line.set, "LEFT", -8, 0)
		panel.mine[#panel.mine + 1] = line
		previous = line
	end

	function panel.ForCharacter()
		local text = strtrim(panel.forBox:GetText() or "")
		return text ~= "" and text or nil
	end

	local listTitle = Label(panel, "Toutes les réservations", "GameFontNormal")
	listTitle:SetPoint("TOPLEFT", previous, "BOTTOMLEFT", 0, -8)
	panel.list = UI.CreateScrollList(panel, 24, SetupReserveRow, UpdateReserveRow)
	panel.list:SetPoint("TOPLEFT", listTitle, "BOTTOMLEFT", 0, -4)
	panel.list:SetPoint("BOTTOMRIGHT", -18, 0)
	panel.empty = Label(panel, "Aucune réservation pour l'instant.", "GameFontHighlightSmall", COLOR.muted)
	panel.empty:SetPoint("CENTER", panel.list, "CENTER")
	return panel
end

local function RefreshReserves(panel, event, canManage)
	local me = GP.GetPlayerFullName()
	local target = canManage and panel.ForCharacter() or nil
	panel.forBox:SetShown(canManage)
	panel.forLabel:SetShown(canManage)
	panel.lock:SetShown(canManage)
	panel.lock:SetText(event.locked and "Déverrouiller" or "Verrouiller")

	local status
	if event.locked then
		status = canManage and "Verrouillées : seuls les confirmés comptent ; tu peux encore corriger."
			or "Verrouillées : seuls les confirmés comptent."
	else
		status = "Un objet par priorité. Les officiers verrouillent avant le raid."
	end
	panel.status:SetText(status)

	-- Réservations du joueur affiché (soi, ou le joueur saisi par l'officier).
	local who = target or me
	local rows = {}
	for _, r in ipairs(event.reserves or {}) do
		if r.character == who then
			rows[r.priority] = r
		end
	end
	local signup
	for _, row in ipairs(event.signups) do
		if row.character == who then
			signup = row
		end
	end
	local classFile = signup and signup.classFile or (who == me and select(2, UnitClass("player")) or nil)
	local editable = canManage or not event.locked
	for _, line in ipairs(panel.mine) do
		local r = rows[line.priority]
		if r then
			local _, _, icon = GP.ItemInfo(r.itemId, r.itemName)
			line.icon:SetTexture(icon)
			line.icon:Show()
			local label = ItemLabel(r.itemId, r.itemName)
			if r.blockedByHard then
				label = label .. Paint(COLOR.negative, " (réservé d'office)")
			elseif r.droppedByLock then
				label = label .. Paint(COLOR.muted, " (ne compte pas : non confirmé)")
			end
			line.current:SetText(label)
			line.warn:SetText(R.CanUseItem(classFile, r.itemId) == false and "!" or "")
		else
			line.icon:Hide()
			line.current:SetText(Paint(COLOR.muted, "—"))
			line.warn:SetText("")
		end
		line.set:SetEnabled(editable)
		line.clear:SetEnabled(editable and r ~= nil)
		line.box:SetEnabled(editable)
	end

	-- Liste groupée par objet, objets réservés d'office en tête.
	local items = {}
	for _, h in ipairs(event.hardReserves or {}) do
		items[#items + 1] = { itemId = h.itemId, itemName = h.itemName, hard = h, canManage = canManage, index = #items + 1 }
	end
	local groups = R.ReserveGroups(event)
	table.sort(groups, function(a, b)
		if #a.rows ~= #b.rows then
			return #a.rows > #b.rows
		end
		return a.itemId < b.itemId
	end)
	for _, group in ipairs(groups) do
		items[#items + 1] = { itemId = group.itemId, itemName = group.itemName, rows = group.rows, canManage = canManage, index = #items + 1 }
	end
	panel.list:GPSetItems(items)
	panel.empty:SetShown(#items == 0)
end

-- Fenêtre de confirmation, déclarée au chargement du module.
local function DefinePopups()
	StaticPopupDialogs["GUILDKEEP_MASTER_LOOTER"] = {
		text = "Réservations verrouillées. Passer le raid en maître du butin (toi) ?",
		button1 = YES or "Oui",
		button2 = NO or "Non",
		OnAccept = function()
			if not R.SetMasterLooter() then
				print("|cff33ff99Guildkeep|r — impossible de changer le mode de butin (il faut être chef du raid).")
			end
		end,
		timeout = 0,
		whileDead = true,
		hideOnEscape = true,
	}
end

-- Onglet Soirée ----------------------------------------------------------------------------

local function SetupCheckRow(row)
	row.icon = row:CreateTexture(nil, "ARTWORK")
	row.icon:SetSize(14, 14)
	row.icon:SetPoint("LEFT", 4, 0)
	row.text = Label(row, nil, "GameFontHighlightSmall")
	row.text:SetPoint("LEFT", row.icon, "RIGHT", 6, 0)
	row.text:SetPoint("RIGHT", -4, 0)
	row.text:SetWordWrap(false)
	row:SetScript("OnEnter", function(self)
		if self.full then
			GameTooltip:SetOwner(self, "ANCHOR_TOP")
			GameTooltip:SetText(self.full, 1, 1, 1, 1, true)
			GameTooltip:Show()
		end
	end)
	row:SetScript("OnLeave", GameTooltip_Hide)
end

local function UpdateCheckRow(row, data)
	row.full = data.text
	if data.ok then
		row.icon:SetTexture("Interface\\RaidFrame\\ReadyCheck-Ready")
	else
		row.icon:SetTexture(data.blocking and "Interface\\RaidFrame\\ReadyCheck-NotReady" or "Interface\\RaidFrame\\ReadyCheck-Waiting")
	end
	row.text:SetText(data.text)
end

local function BuildNight(detail)
	local panel = CreateFrame("Frame", nil, detail)
	panel:SetPoint("TOPLEFT", detail.sectionTop, "BOTTOMLEFT", 0, -6)
	panel:SetPoint("BOTTOMRIGHT", 0, 28)

	local title = Label(panel, "Rôles de soutien", "GameFontNormal")
	title:SetPoint("TOPLEFT", 0, 0)
	panel.duties = {}
	local previous = title
	for _, duty in ipairs(R.DUTIES) do
		local line = CreateFrame("Frame", nil, panel)
		line:SetHeight(24)
		line:SetPoint("TOPLEFT", previous, "BOTTOMLEFT", 0, previous == title and -6 or -2)
		line:SetPoint("RIGHT", panel, "RIGHT", 0, 0)
		line.label = Label(line, R.DUTY_LABELS[duty], "GameFontNormalSmall")
		line.label:SetPoint("LEFT", 0, 0)
		line.label:SetWidth(130)
		line.holder = Label(line, nil, "GameFontHighlight")
		line.holder:SetPoint("LEFT", line.label, "RIGHT", 4, 0)
		line.holder:SetWidth(150)
		line.holder:SetWordWrap(false)
		line.free = UI.Button(line, "Libérer", 70, function()
			local event = Selected()
			if event then
				Report(R.SetDuty(event, duty, nil))
			end
		end)
		line.free:SetPoint("RIGHT", 0, 0)
		line.assign = UI.Button(line, "Confier", 70, function()
			local event = Selected()
			local name = strtrim(line.box:GetText() or "")
			if event and name ~= "" then
				-- Nom exact de l'inscrit si on le reconnaît (casse, nom de famille).
				for _, row in ipairs(event.signups) do
					if row.character:lower() == name:lower() or row.character:lower():find("^" .. name:lower() .. " ") then
						name = row.character
					end
				end
				local ok, err = R.SetDuty(event, duty, name)
				Report(ok, err)
				if ok then
					line.box:SetText("")
					line.box:ClearFocus()
				end
			end
		end)
		line.assign:SetPoint("RIGHT", line.free, "LEFT", -2, 0)
		line.box = UI.InputBox(line, 110, 64)
		line.box:SetPoint("RIGHT", line.assign, "LEFT", -8, 0)
		line.duty = duty
		panel.duties[#panel.duties + 1] = line
		previous = line
	end

	local checkTitle = Label(panel, "Avant le raid", "GameFontNormal")
	checkTitle:SetPoint("TOPLEFT", previous, "BOTTOMLEFT", 0, -8)
	panel.checklist = UI.CreateScrollList(panel, 18, SetupCheckRow, UpdateCheckRow)
	panel.checklist:SetPoint("TOPLEFT", checkTitle, "BOTTOMLEFT", 0, -4)
	panel.checklist:SetPoint("BOTTOMRIGHT", -18, 0)
	return panel
end

local function RefreshNight(panel, event, canManage)
	local confirmed = {}
	for _, row in ipairs(event.signups) do
		if row.placement == "confirmed" then
			confirmed[row.character] = true
		end
	end
	for _, line in ipairs(panel.duties) do
		local holder = event.duties and event.duties[line.duty]
		if holder then
			line.holder:SetText(confirmed[holder] and holder or (holder .. Paint(COLOR.negative, " (non confirmé)")))
		else
			line.holder:SetText(Paint(COLOR.muted, "personne"))
		end
		line.box:SetShown(canManage)
		line.assign:SetShown(canManage)
		line.free:SetShown(canManage and holder ~= nil)
	end
	local items = {}
	for _, item in ipairs(R.Checklist(event)) do
		items[#items + 1] = item
	end
	if #items == 0 then
		items[1] = { ok = true, text = "Tout est prêt." }
	end
	panel.checklist:GPSetItems(items)
end

-- Branchement dans le détail de l'onglet Événements -------------------------------------------

-- Libellé de la section : nombre de réservations retenues, cadenas si verrouillé.
local function ReservesTitle(event)
	local count = #(event.effectiveReserves or {})
	if count == 0 then
		return "Réserves"
	end
	return ("Réserves (%d)%s"):format(count, event.locked and " |TInterface\\Buttons\\LockButton-Locked-Up:14|t" or "")
end

local function Track(refresh)
	return function(panel, event, canManage)
		currentEvent = event
		refresh(panel, event, canManage)
	end
end

GP.Modules.Define("raid", "ui", function(ctx)
	DefinePopups()
	hooksecurefunc("ChatEdit_InsertLink", OnInsertLink)
	ctx:Track(GP.EventsUI.Extend({
		sections = {
			{ key = "reserves", label = "Réserves", title = ReservesTitle, build = BuildReserves, refresh = Track(RefreshReserves) },
			{ key = "night", label = "Soirée", build = BuildNight, refresh = Track(RefreshNight) },
		},
		exportLines = R.NightExportLines,
	}, ctx.id))
end)
