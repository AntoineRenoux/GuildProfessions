local _, GP = ...

-- Module Quêtes, partie interface : onglet « Quêtes ». À gauche, les quêtes
-- élites, de donjon et de raid de la guilde, rangées par type et par lieu ; à
-- droite, qui les a (en ligne, objectifs remplis), avec chuchoter, inviter et
-- partager au groupe.

local Q = GP.Quests
local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label

local view = {
	kind = "all", -- all | elite | dungeon | raid
	mineOnly = false,
	search = "",
	selected = nil, -- id de quête
}
local panel

local KIND_ORDER = { elite = 1, dungeon = 2, raid = 3 }

local function ClassName(name, classFile)
	local r, g, b = UI.ClassColor(classFile)
	return UI.Hex({ r, g, b }) .. name .. "|r"
end

local function Report(ok, err)
	if not ok and err then
		print("|cff33ff99Guildkeep|r — " .. err)
	end
end

local function Count(holders)
	local n, online = 0, 0
	for name in pairs(holders) do
		n = n + 1
		if UI.IsOnline(name) then
			online = online + 1
		end
	end
	return n, online
end

-- Liste de gauche : en-têtes « type · lieu » et quêtes ----------------------------------------

local function SetupQuestRow(row)
	UI.AddRowBackground(row)
	row.header = Label(row, nil, "GameFontNormal")
	row.header:SetPoint("LEFT", 6, 0)
	row.title = Label(row, nil, "GameFontHighlight")
	row.title:SetPoint("LEFT", 14, 0)
	row.title:SetWidth(210)
	row.title:SetWordWrap(false)
	row.count = Label(row, nil, "GameFontHighlightSmall")
	row.count:SetPoint("RIGHT", -8, 0)
	row.count:SetJustifyH("RIGHT")
	row:SetScript("OnClick", function(self)
		if self.quest then
			view.selected = self.quest.id
			PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
			GP.RefreshUI()
		end
	end)
end

local function UpdateQuestRow(row, data)
	row.quest = data.quest
	row.header:SetShown(data.header ~= nil)
	row.title:SetShown(data.quest ~= nil)
	row.count:SetShown(data.quest ~= nil)
	row.stripe:SetShown(false)
	row.selectedTex:SetShown(data.quest ~= nil and data.quest.id == view.selected)
	if data.header then
		row.header:SetText(data.header)
		return
	end
	local quest = data.quest
	local mine = quest.holders[GP.GetPlayerFullName()] and Paint(COLOR.gold, "• ") or ""
	row.title:SetText(mine .. quest.title .. (quest.level and Paint(COLOR.muted, (" [%d]"):format(quest.level)) or ""))
	local n, online = Count(quest.holders)
	row.count:SetText(("%d%s"):format(n, online > 0 and Paint(COLOR.positive, (" · %d en ligne"):format(online)) or ""))
end

-- Détail : qui a la quête ------------------------------------------------------------------------

local function SetupHolderRow(row)
	UI.AddRowBackground(row)
	row.online = row:CreateTexture(nil, "ARTWORK")
	row.online:SetSize(14, 14)
	row.online:SetPoint("LEFT", 6, 0)
	row.name = Label(row, nil, "GameFontHighlight")
	row.name:SetPoint("LEFT", row.online, "RIGHT", 6, 0)
	row.name:SetWidth(180)
	row.name:SetWordWrap(false)
	row.state = Label(row, nil, "GameFontHighlightSmall")
	row.state:SetPoint("LEFT", row.name, "RIGHT", 6, 0)
	row.invite = UI.Button(row, "Inviter", 70, function(self)
		local name = self:GetParent().holder
		local invite = C_PartyInfo and C_PartyInfo.InviteUnit or InviteUnit
		pcall(invite, (name:gsub(" ", "-", 1)))
	end)
	row.invite:SetPoint("RIGHT", -4, 0)
	row.whisper = UI.Button(row, "Chuchoter", 80, function(self)
		local name = self:GetParent().holder
		if ChatFrame_SendTell then
			ChatFrame_SendTell((name:gsub(" ", "-", 1)))
		end
	end)
	row.whisper:SetPoint("RIGHT", row.invite, "LEFT", -2, 0)
end

local function UpdateHolderRow(row, data)
	row.holder = data.name
	row.stripe:SetShown(data.index % 2 == 0)
	UI.SetOnlineIcon(row.online, data.name)
	row.name:SetText(ClassName(data.name, data.classFile) .. (data.me and Paint(COLOR.gold, " (toi)") or ""))
	row.state:SetText(data.complete and Paint(COLOR.positive, "objectifs remplis") or Paint(COLOR.muted, "en cours"))
	local canReach = not data.me and UI.IsOnline(data.name) == true
	row.whisper:SetShown(canReach)
	row.invite:SetShown(canReach)
end

-- Construction ------------------------------------------------------------------------------------

local function BuildQuestsTab(_, parent)
	panel = parent
	local left = CreateFrame("Frame", nil, parent)
	left:SetPoint("TOPLEFT", 8, -6)
	left:SetPoint("BOTTOMLEFT", 8, 4)
	left:SetWidth(340)

	panel.kindButtons = {}
	local previous
	for _, def in ipairs({ { "all", "Toutes" }, { "elite", "Élites" }, { "dungeon", "Donjons" }, { "raid", "Raids" } }) do
		local button = UI.Toggle(left, def[2], function(self)
			view.kind = self.kind
			GP.RefreshUI()
		end)
		button.kind = def[1]
		button:SetPoint("TOPLEFT", previous or left, previous and "TOPRIGHT" or "TOPLEFT", previous and 2 or 0, 0)
		panel.kindButtons[#panel.kindButtons + 1] = button
		previous = button
	end

	panel.mineOnly = CreateFrame("CheckButton", nil, left, "UICheckButtonTemplate")
	panel.mineOnly:SetSize(22, 22)
	panel.mineOnly:SetPoint("TOPLEFT", 0, -28)
	UI.SetCheckLabel(panel.mineOnly, "Seulement mes quêtes")
	panel.mineOnly:SetScript("OnClick", function(self)
		view.mineOnly = self:GetChecked() and true or false
		GP.RefreshUI()
	end)
	panel.search = UI.CreateSearchBox(left, 150, "Chercher", function(text)
		view.search = text:lower()
		GP.RefreshUI()
	end)
	panel.search:SetPoint("TOPRIGHT", -2, -29)

	panel.list = UI.CreateScrollList(left, 24, SetupQuestRow, UpdateQuestRow)
	panel.list:SetPoint("TOPLEFT", 0, -56)
	panel.list:SetPoint("BOTTOMRIGHT", -18, 18)
	panel.hidden = Label(left, nil, "GameFontHighlightSmall", COLOR.muted)
	panel.hidden:SetPoint("BOTTOMLEFT", 4, 2)
	panel.empty = Label(left, "Aucune quête élite, de donjon ou de raid\nrelevée dans la guilde pour l'instant.", "GameFontHighlight", COLOR.muted)
	panel.empty:SetPoint("CENTER", panel.list, "CENTER")
	panel.empty:SetJustifyH("CENTER")

	local detail = CreateFrame("Frame", nil, parent)
	detail:SetPoint("TOPLEFT", left, "TOPRIGHT", 24, 0)
	detail:SetPoint("BOTTOMRIGHT", -8, 4)
	panel.detail = detail
	panel.share = UI.Button(detail, "Partager au groupe", 140, function()
		if view.selected then
			Report(Q.Share(view.selected))
		end
	end)
	panel.share:SetPoint("TOPRIGHT", 0, 0)
	panel.title = Label(detail, nil, "GameFontNormalLarge")
	panel.title:SetPoint("TOPLEFT", 0, -2)
	panel.title:SetPoint("RIGHT", panel.share, "LEFT", -8, 0)
	panel.title:SetWordWrap(false)
	panel.sub = Label(detail, nil, "GameFontHighlightSmall", COLOR.muted)
	panel.sub:SetPoint("TOPLEFT", panel.title, "BOTTOMLEFT", 0, -6)
	panel.sub:SetPoint("RIGHT", 0, 0)
	panel.holders = UI.CreateScrollList(detail, 26, SetupHolderRow, UpdateHolderRow)
	panel.holders:SetPoint("TOPLEFT", panel.sub, "BOTTOMLEFT", 0, -10)
	panel.holders:SetPoint("BOTTOMRIGHT", -18, 0)
	panel.noSelection = Label(detail, "Choisis une quête pour voir qui la fait.", "GameFontHighlight", COLOR.muted)
	panel.noSelection:SetPoint("CENTER")

	local divider = parent:CreateTexture(nil, "ARTWORK")
	divider:SetColorTexture(1, 0.82, 0, 0.18)
	divider:SetWidth(1)
	divider:SetPoint("TOPLEFT", left, "TOPRIGHT", 12, 0)
	divider:SetPoint("BOTTOMLEFT", left, "BOTTOMRIGHT", 12, 0)
end

local function RefreshQuests()
	local me = GP.GetPlayerFullName()
	local all = Q.GetAll()
	for _, button in ipairs(panel.kindButtons) do
		button:GPSetSelected(button.kind == view.kind)
	end
	panel.mineOnly:SetChecked(view.mineOnly)

	-- Filtres ; les quêtes déjà rendues par ce personnage sont masquées (rien à y faire).
	local visible, hidden = {}, 0
	for _, quest in pairs(all) do
		local keep = (view.kind == "all" or quest.kind == view.kind)
			and (not view.mineOnly or quest.holders[me] ~= nil)
			and (view.search == "" or quest.title:lower():find(view.search, 1, true) or (quest.location or ""):lower():find(view.search, 1, true))
		if keep and not quest.holders[me] and Q.TurnedIn(quest.id) then
			keep = false
			hidden = hidden + 1
		end
		if keep then
			visible[#visible + 1] = quest
		end
	end
	table.sort(visible, function(a, b)
		if a.kind ~= b.kind then
			local ka, kb = KIND_ORDER[a.kind] or 99, KIND_ORDER[b.kind] or 99
			if ka ~= kb then
				return ka < kb
			end
			return tostring(a.kind) < tostring(b.kind)
		end
		if (a.location or "") ~= (b.location or "") then
			return (a.location or "") < (b.location or "")
		end
		return a.title < b.title
	end)
	local items, lastGroup = {}, nil
	for _, quest in ipairs(visible) do
		local group = tostring(quest.kind) .. "|" .. (quest.location or "?")
		if group ~= lastGroup then
			items[#items + 1] = { header = ("%s · %s"):format(Q.KIND_LABELS[quest.kind] or "?", quest.location or "?") }
			lastGroup = group
		end
		items[#items + 1] = { quest = quest }
	end
	panel.list:GPSetItems(items)
	panel.empty:SetShown(#items == 0)
	panel.hidden:SetText(hidden > 0 and ("%d quête%s déjà rendue%s masquée%s"):format(hidden, hidden > 1 and "s" or "",
		hidden > 1 and "s" or "", hidden > 1 and "s" or "") or "")

	local quest = view.selected and all[view.selected]
	panel.title:SetShown(quest ~= nil)
	panel.sub:SetShown(quest ~= nil)
	panel.holders:SetShown(quest ~= nil)
	panel.noSelection:SetShown(quest == nil)
	panel.share:SetShown(quest ~= nil and quest.holders[me] ~= nil and IsInGroup())
	if not quest then
		return
	end
	panel.title:SetText(quest.title)
	panel.sub:SetText(("%s · %s%s"):format(Q.KIND_LABELS[quest.kind] or "?", quest.location or "?", quest.level and (" · niveau " .. quest.level) or ""))
	local holders = {}
	for name, h in pairs(quest.holders) do
		holders[#holders + 1] = { name = name, complete = h.complete, classFile = h.classFile, me = name == me }
	end
	table.sort(holders, function(a, b)
		local oa, ob = UI.IsOnline(a.name) == true, UI.IsOnline(b.name) == true
		if oa ~= ob then
			return oa
		end
		return a.name < b.name
	end)
	for i, h in ipairs(holders) do
		h.index = i
	end
	panel.holders:GPSetItems(holders)
end

GP.Modules.Define("quests", "ui", function(ctx)
	ctx:Tutorial("quests", {
		tab = "tabQuests",
		title = "Quêtes",
		steps = {
			{ title = "Les quêtes de la guilde", text = "Les quêtes élites, de donjon et de raid du journal de chaque membre, relevées automatiquement, pour trouver qui les fait aussi." },
			{ target = function() return panel and panel.kindButtons and panel.kindButtons[1] end, title = "Type de quête",
				text = "Toutes, Élites, Donjons ou Raids. Les quêtes sont rangées par zone ou par donjon." },
			{ target = function() return panel and panel.mineOnly end, title = "Seulement mes quêtes",
				text = "Ne garde que les quêtes que tu as. Celles que tu as déjà rendues sont masquées d'office (le nombre s'affiche en bas)." },
			{ target = function() return panel and panel.search end, title = "Chercher", text = "Une partie du titre ou du lieu." },
			{ target = function() return panel and panel.holders end, title = "Qui l'a",
				text = "Les membres qui ont la quête, en ligne d'abord, et s'ils ont rempli les objectifs. « Chuchoter » et « Inviter » pour former un groupe.",
				hint = "Choisis d'abord une quête dans la liste." },
			{ target = function() return panel and panel.share end, title = "Partager au groupe",
				text = "Partage la quête aux membres de ton groupe, par le partage de quête du jeu.", hint = "Visible si tu as la quête et que tu es en groupe." },
		},
	})
	ctx:Slash({ "quetes", "quests", "quete", "quest" }, function()
		GP.ShowTab("tabQuests")
	end)
	ctx:AddTab({
		label = "Quêtes",
		key = "tabQuests",
		order = 70,
		build = BuildQuestsTab,
		refresh = RefreshQuests,
	})
end)
