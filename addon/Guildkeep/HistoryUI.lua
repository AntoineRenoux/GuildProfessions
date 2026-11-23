local _, GP = ...

-- Module Présence et butin, partie interface : onglet « Présence ». À gauche
-- les joueurs (taux sur 30/60/90 jours et tendance) ou les raids relevés ; à
-- droite le détail du joueur ou du raid, avec les corrections réservées aux
-- officiers.

local A = GP.Attendance
local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label

local view = {
	mode = "players", -- players | raids
	days = 30,
	search = "",
	player = nil,
	raid = nil, -- clé du raid affiché
}
local panel

local function Pct(rate)
	return rate.percent and ("%d%%"):format(math.floor(rate.percent + 0.5)) or "—"
end

local function RateColor(rate)
	if not rate.percent then
		return COLOR.muted
	end
	return rate.percent >= 75 and COLOR.positive or (rate.percent >= 50 and COLOR.gold or COLOR.negative)
end

local function Trend(stats)
	local now, before = stats.d30.percent, stats.previous30.percent
	if not now or not before then
		return ""
	end
	local delta = math.floor(now - before + 0.5)
	if math.abs(delta) < 5 then
		return Paint(COLOR.muted, " =")
	end
	return delta > 0 and Paint(COLOR.positive, (" +%d"):format(delta)) or Paint(COLOR.negative, (" %d"):format(delta))
end

local function Pick(stats)
	return view.days == 90 and stats.d90 or (view.days == 60 and stats.d60 or stats.d30)
end

local function ClassName(name, classFile)
	local r, g, b = UI.ClassColor(classFile)
	return UI.Hex({ r, g, b }) .. name .. "|r"
end

local function Report(ok, err)
	if not ok and err then
		print("|cff33ff99Guildkeep|r — " .. err)
	end
end

local function FindRaid(key)
	for _, raid in ipairs(A.GetRaids()) do
		if raid.key == key then
			return raid
		end
	end
end

-- Liste de gauche ------------------------------------------------------------------------------

local function SetupLeftRow(row)
	UI.AddRowBackground(row)
	row.title = Label(row, nil, "GameFontHighlight")
	row.title:SetPoint("LEFT", 8, 0)
	row.title:SetWidth(170)
	row.title:SetWordWrap(false)
	row.value = Label(row, nil, "GameFontHighlight")
	row.value:SetPoint("RIGHT", -8, 0)
	row.value:SetJustifyH("RIGHT")
	row.sub = Label(row, nil, "GameFontHighlightSmall", COLOR.muted)
	row.sub:SetPoint("LEFT", row.title, "RIGHT", 4, 0)
	row.sub:SetPoint("RIGHT", row.value, "LEFT", -6, 0)
	row.sub:SetWordWrap(false)
	row:SetScript("OnClick", function(self)
		if self.data.stats then
			view.player = self.data.stats.character
		else
			view.raid = self.data.raid.key
		end
		PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		GP.RefreshUI()
	end)
end

local function UpdateLeftRow(row, data)
	row.data = data
	row.stripe:SetShown(data.index % 2 == 0)
	if data.stats then
		local s = data.stats
		local rate = Pick(s)
		row.selectedTex:SetShown(s.character == view.player)
		row.title:SetText(ClassName(s.character, s.classFile))
		row.sub:SetText(("%d/%d%s"):format(rate.attended, rate.eligible, s.loot > 0 and (" · %d obj."):format(s.loot) or ""))
		row.value:SetText(Paint(RateColor(rate), Pct(rate)) .. Trend(s))
	else
		local raid = data.raid
		row.selectedTex:SetShown(raid.key == view.raid)
		row.title:SetText(raid.instance)
		local loot = 0
		for _, l in ipairs(raid.loot) do
			if not l.voided then
				loot = loot + 1
			end
		end
		row.sub:SetText(date("%d/%m", raid.startedAt) .. (A.IsGuildRaid(raid) and "" or " · hors décompte")
			.. (raid.pendingSync and " · local" or ""))
		row.value:SetText(("%d pers. · %d obj."):format(raid.count, loot))
	end
end

-- Détail ----------------------------------------------------------------------------------------

local function SetupDetailRow(row)
	UI.AddRowBackground(row)
	row.icon = row:CreateTexture(nil, "ARTWORK")
	row.icon:SetSize(16, 16)
	row.icon:SetPoint("LEFT", 6, 0)
	row.header = Label(row, nil, "GameFontNormal")
	row.header:SetPoint("LEFT", 6, 0)
	row.text = Label(row, nil, "GameFontHighlight")
	row.text:SetPoint("LEFT", row.icon, "RIGHT", 6, 0)
	row.text:SetWordWrap(false)
	row.actions = {}
	local previous
	for i = 3, 1, -1 do
		local button = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
		button:SetSize(70, 20)
		if previous then
			button:SetPoint("RIGHT", previous, "LEFT", -2, 0)
		else
			button:SetPoint("RIGHT", -4, 0)
		end
		row.actions[i] = button
		previous = button
	end
	row.text:SetPoint("RIGHT", row.actions[1], "LEFT", -6, 0)
	row:SetScript("OnEnter", function(self)
		if self.data and self.data.itemId then
			GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
			if not pcall(GameTooltip.SetHyperlink, GameTooltip, "item:" .. self.data.itemId) then
				GameTooltip:SetText("Objet #" .. self.data.itemId)
			end
			if self.data.note then
				GameTooltip:AddLine(self.data.note, 1, 1, 1, true)
			end
			GameTooltip:Show()
		elseif self.data and self.data.note then
			GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
			GameTooltip:SetText(self.data.note, 1, 1, 1, 1, true)
			GameTooltip:Show()
		end
	end)
	row:SetScript("OnLeave", GameTooltip_Hide)
end

local function UpdateDetailRow(row, data)
	row.data = data
	row.stripe:SetShown(not data.header and data.index % 2 == 0)
	row.header:SetShown(data.header ~= nil)
	row.icon:SetShown(data.header == nil)
	row.text:SetShown(data.header == nil)
	if data.header then
		row.header:SetText(data.header)
	else
		row.icon:SetTexture(data.icon)
		row.text:SetText(data.text)
	end
	for i = 1, 3 do
		local action = data.actions and data.actions[i]
		local button = row.actions[i]
		if action then
			button:SetText(action[1])
			button:SetScript("OnClick", action[2])
			button:Show()
		else
			button:Hide()
		end
	end
end

local STATE_ICONS = {
	[true] = "Interface\\RaidFrame\\ReadyCheck-Ready",
	[false] = "Interface\\RaidFrame\\ReadyCheck-NotReady",
	excused = "Interface\\RaidFrame\\ReadyCheck-Waiting",
}

local function LootRows(items, raid, loot, canCorrect, showReceiver)
	local name, link, icon = GP.ItemInfo(loot.itemId, loot.itemName)
	local label = link or name
	if loot.voided then
		label = Paint(COLOR.muted, name .. " (annulé)")
	end
	local text = showReceiver and ("%s → %s"):format(label, loot.character) or ("%s · %s"):format(label, raid.instance .. " " .. date("%d/%m", raid.startedAt))
	local actions
	if canCorrect then
		actions = { { loot.voided and "Rétablir" or "Annuler", function()
			Report(A.SetVoid(raid, loot, not loot.voided, nil))
		end } }
	end
	items[#items + 1] = { icon = icon, text = text, itemId = loot.itemId, note = loot.voided and loot.voidNote or nil,
		actions = actions, index = #items + 1 }
end

local function PlayerItems(raids, character)
	local items = {}
	local stats = A.Stats(raids, character)
	panel.title:SetText(character)
	panel.sub:SetText(("30 j : %s (%d/%d)%s   ·   60 j : %s   ·   90 j : %s   ·   30 j d'avant : %s"):format(
		Paint(RateColor(stats.d30), Pct(stats.d30)), stats.d30.attended, stats.d30.eligible, Trend(stats),
		Paint(RateColor(stats.d60), Pct(stats.d60)), Paint(RateColor(stats.d90), Pct(stats.d90)), Pct(stats.previous30)))
	items[#items + 1] = { header = "Raids" }
	for _, raid in ipairs(raids) do
		if A.IsGuildRaid(raid) then
			local state = A.Attended(raid, character)
			local minutes = raid.participants[character] and raid.participants[character].minutes or 0
			local correction = raid.corrections[character]
			local text = ("%s · %s · %d min"):format(date("%d/%m", raid.startedAt), raid.instance, minutes)
			if correction then
				text = text .. Paint(COLOR.gold, " · " .. (A.STATUS_LABELS[correction.status] or "?"):lower() .. " (officier)")
			end
			items[#items + 1] = { icon = STATE_ICONS[state == nil and "excused" or state], text = text,
				note = correction and correction.note, index = #items + 1 }
		end
	end
	items[#items + 1] = { header = ("Butin (%d)"):format(stats.loot) }
	for _, raid in ipairs(raids) do
		for _, loot in ipairs(raid.loot) do
			if loot.character == character then
				LootRows(items, raid, loot, false, false)
			end
		end
	end
	return items
end

local function RaidItems(raid, canCorrect)
	local items = {}
	panel.title:SetText(("%s — %s"):format(raid.instance, date("%d/%m/%Y", raid.startedAt)))
	panel.sub:SetText(("Chef de raid : %s · %s → %s%s%s"):format(raid.leader or "?", date("%H:%M", raid.startedAt), date("%H:%M", raid.endedAt),
		A.IsGuildRaid(raid) and "" or Paint(COLOR.gold, " · moins de 5 membres : hors décompte"),
		raid.pendingSync and Paint(COLOR.muted, " · relevé local, pas encore sur le serveur") or ""))

	local names = {}
	for name in pairs(raid.participants) do
		names[#names + 1] = name
	end
	for name in pairs(raid.corrections) do
		if not raid.participants[name] then
			names[#names + 1] = name
		end
	end
	table.sort(names)
	items[#items + 1] = { header = ("Présence (%d)"):format(#names) }
	for _, name in ipairs(names) do
		local p = raid.participants[name]
		local state = A.Attended(raid, name)
		local correction = raid.corrections[name]
		local text = ("%s · %d min"):format(ClassName(name, p and p.classFile), p and p.minutes or 0)
		if correction then
			text = text .. Paint(COLOR.gold, " · " .. (A.STATUS_LABELS[correction.status] or "?"):lower())
		end
		local actions
		if canCorrect then
			actions = {}
			for _, status in ipairs({ "present", "absent", "excused" }) do
				if not correction or correction.status ~= status then
					actions[#actions + 1] = { A.STATUS_LABELS[status], function()
						Report(A.Correct(raid, name, status, nil))
					end }
				end
			end
		end
		items[#items + 1] = { icon = STATE_ICONS[state == nil and "excused" or state], text = text,
			note = correction and correction.note, actions = actions, index = #items + 1 }
	end
	items[#items + 1] = { header = ("Butin (%d)"):format(#raid.loot) }
	for _, loot in ipairs(raid.loot) do
		LootRows(items, raid, loot, canCorrect, true)
	end
	return items
end

-- Construction et rafraîchissement ---------------------------------------------------------------

local function BuildHistoryTab(_, parent)
	panel = parent
	local left = CreateFrame("Frame", nil, parent)
	left:SetPoint("TOPLEFT", 8, -6)
	left:SetPoint("BOTTOMLEFT", 8, 4)
	left:SetWidth(318)

	panel.modeButtons = {}
	local previous
	for _, def in ipairs({ { "players", "Joueurs" }, { "raids", "Raids" } }) do
		local button = UI.Toggle(left, def[2], function(self)
			view.mode = self.mode
			GP.RefreshUI()
		end)
		button.mode = def[1]
		button:SetPoint("TOPLEFT", previous or left, previous and "TOPRIGHT" or "TOPLEFT", previous and 4 or 0, 0)
		panel.modeButtons[#panel.modeButtons + 1] = button
		previous = button
	end
	panel.dayButtons = {}
	local nextAnchor
	for i = 3, 1, -1 do
		local days = ({ 30, 60, 90 })[i]
		local button = UI.Toggle(left, days .. " j", function(self)
			view.days = self.days
			GP.RefreshUI()
		end)
		button.days = days
		button:SetPoint("TOPRIGHT", nextAnchor or left, nextAnchor and "TOPLEFT" or "TOPRIGHT", nextAnchor and -2 or 0, 0)
		panel.dayButtons[#panel.dayButtons + 1] = button
		nextAnchor = button
	end

	panel.search = UI.CreateSearchBox(left, 300, "Chercher un joueur", function(text)
		view.search = text:lower()
		GP.RefreshUI()
	end)
	panel.search:SetPoint("TOPLEFT", 6, -30)

	panel.list = UI.CreateScrollList(left, 26, SetupLeftRow, UpdateLeftRow)
	panel.list:SetPoint("TOPLEFT", 0, -56)
	panel.list:SetPoint("BOTTOMRIGHT", -18, 0)
	panel.empty = Label(left, "Aucun raid relevé pour l'instant.\nLa présence est notée en raid dès que\n5 membres de la guilde y sont ensemble.",
		"GameFontHighlight", COLOR.muted)
	panel.empty:SetPoint("CENTER", panel.list, "CENTER")
	panel.empty:SetJustifyH("CENTER")

	local detail = CreateFrame("Frame", nil, parent)
	detail:SetPoint("TOPLEFT", left, "TOPRIGHT", 24, 0)
	detail:SetPoint("BOTTOMRIGHT", -8, 4)
	panel.detail = detail
	panel.title = Label(detail, nil, "GameFontNormalLarge")
	panel.title:SetPoint("TOPLEFT", 0, -2)
	panel.title:SetPoint("RIGHT", 0, 0)
	panel.sub = Label(detail, nil, "GameFontHighlightSmall", COLOR.muted)
	panel.sub:SetPoint("TOPLEFT", panel.title, "BOTTOMLEFT", 0, -6)
	panel.sub:SetPoint("RIGHT", 0, 0)
	panel.sub:SetWordWrap(false)

	-- Officier : marquer présent un joueur absent du relevé (pas d'addon, déconnexion…).
	panel.addBox = UI.InputBox(detail, 140, 64)
	panel.addBox:SetPoint("BOTTOMLEFT", 6, 4)
	panel.addButton = UI.Button(detail, "Marquer présent", 130, function()
		local raid = FindRaid(view.raid)
		local name = strtrim(panel.addBox:GetText() or "")
		if raid and name ~= "" then
			local ok, err = A.Correct(raid, name, "present", "Ajouté par un officier")
			Report(ok, err)
			if ok then
				panel.addBox:SetText("")
			end
		end
	end)
	panel.addButton:SetPoint("LEFT", panel.addBox, "RIGHT", 6, 0)

	panel.rows = UI.CreateScrollList(detail, 24, SetupDetailRow, UpdateDetailRow)
	panel.rows:SetPoint("TOPLEFT", panel.sub, "BOTTOMLEFT", 0, -10)
	panel.rows:SetPoint("BOTTOMRIGHT", -18, 32)
	panel.noSelection = Label(detail, "Choisis un joueur ou un raid.", "GameFontHighlight", COLOR.muted)
	panel.noSelection:SetPoint("CENTER")

	local divider = parent:CreateTexture(nil, "ARTWORK")
	divider:SetColorTexture(1, 0.82, 0, 0.18)
	divider:SetWidth(1)
	divider:SetPoint("TOPLEFT", left, "TOPRIGHT", 12, 0)
	divider:SetPoint("BOTTOMLEFT", left, "BOTTOMRIGHT", 12, 0)
end

local function RefreshHistory()
	local raids = A.GetRaids()
	local canCorrect = GP.IsOfficer()
	for _, button in ipairs(panel.modeButtons) do
		button:GPSetSelected(button.mode == view.mode)
	end
	for _, button in ipairs(panel.dayButtons) do
		button:GPSetSelected(button.days == view.days)
		button:SetShown(view.mode == "players")
	end
	panel.search:SetShown(view.mode == "players")

	local items = {}
	if view.mode == "players" then
		local stats = A.Summary(raids)
		table.sort(stats, function(a, b)
			local ra, rb = Pick(a).percent or -1, Pick(b).percent or -1
			if ra ~= rb then
				return ra > rb
			end
			return a.character < b.character
		end)
		for _, s in ipairs(stats) do
			if view.search == "" or s.character:lower():find(view.search, 1, true) then
				items[#items + 1] = { stats = s, index = #items + 1 }
			end
		end
		view.player = view.player or (items[1] and items[1].stats.character)
	else
		for _, raid in ipairs(raids) do
			items[#items + 1] = { raid = raid, index = #items + 1 }
		end
		view.raid = view.raid or (raids[1] and raids[1].key)
	end
	panel.list:GPSetItems(items)
	panel.empty:SetShown(#raids == 0)

	local detailItems
	local raid = view.mode == "raids" and FindRaid(view.raid) or nil
	if view.mode == "players" and view.player then
		detailItems = PlayerItems(raids, view.player)
	elseif raid then
		detailItems = RaidItems(raid, canCorrect)
	end
	panel.title:SetShown(detailItems ~= nil)
	panel.sub:SetShown(detailItems ~= nil)
	panel.rows:SetShown(detailItems ~= nil)
	panel.noSelection:SetShown(detailItems == nil)
	panel.rows:GPSetItems(detailItems or {})
	panel.addBox:SetShown(raid ~= nil and canCorrect)
	panel.addButton:SetShown(raid ~= nil and canCorrect)
end

GP.Modules.Define("attendance", "ui", function(ctx)
	ctx:Slash({ "presence", "attendance", "butin", "loot" }, function()
		GP.ShowTab("tabHistory")
	end)
	ctx:AddTab({
		label = "Présence",
		key = "tabHistory",
		order = 60,
		build = BuildHistoryTab,
		refresh = RefreshHistory,
	})
end)
