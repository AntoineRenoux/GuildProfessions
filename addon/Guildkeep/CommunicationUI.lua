local _, GP = ...

-- Module Communication, partie interface : onglet « Guilde ». Trois sections
-- (annonces, charte, objectifs), chacune en liste à gauche et détail à droite.
-- Les officiers rédigent (annonces avec modèles, sections de la charte,
-- objectifs) ; chacun lit et contribue aux objectifs. Les événements proposés
-- pour lier une annonce viennent du service « events.upcoming » (module
-- Événements), absent si ce module est désactivé.

local C = GP.Communication
local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label

local view = {
	section = "announcements", -- announcements | handbook | goals
	selected = { announcements = nil, handbook = nil, goals = nil },
}
local panel

local SECTIONS = {
	{ key = "announcements", label = "Annonces" },
	{ key = "handbook", label = "Charte" },
	{ key = "goals", label = "Objectifs" },
}

local function Report(ok, err)
	if not ok and err then
		GP.Chat(err)
	end
	return ok
end

local function Upcoming()
	local service = GP.Modules.Service("events.upcoming")
	if not service then
		return {}
	end
	local ok, list = pcall(service)
	return ok and type(list) == "table" and list or {}
end

local function EventTitle(eventId)
	if not eventId then
		return nil
	end
	for _, event in ipairs(Upcoming()) do
		if event.id == eventId then
			return ("%s (%s)"):format(event.title, date("%d/%m %H:%M", event.startsAt))
		end
	end
	return ("événement #%d"):format(eventId)
end

-- Zone de texte multiligne défilante (lecture ou saisie).
local function ScrollText(parent, editable)
	local scroll = CreateFrame("ScrollFrame", nil, parent, "UIPanelScrollFrameTemplate")
	local child
	if editable then
		child = CreateFrame("EditBox", nil, scroll)
		child:SetMultiLine(true)
		child:SetAutoFocus(false)
		child:SetFontObject(ChatFontNormal)
		child:SetScript("OnEscapePressed", child.ClearFocus)
		scroll:SetScript("OnMouseDown", function() child:SetFocus() end)
	else
		child = CreateFrame("Frame", nil, scroll)
		child.text = Label(child, nil, "GameFontHighlight")
		child.text:SetPoint("TOPLEFT")
		child.text:SetJustifyV("TOP")
		child.text:SetWordWrap(true)
		child:SetHeight(20)
	end
	scroll:SetScrollChild(child)
	scroll:SetScript("OnSizeChanged", function(self, width)
		child:SetWidth(width)
		if child.text then
			child.text:SetWidth(width)
			child:SetHeight(math.max(20, child.text:GetStringHeight() + 8))
		end
	end)
	function scroll:GPSetText(text)
		if child.text then
			child.text:SetText(text or "")
			child:SetHeight(math.max(20, child.text:GetStringHeight() + 8))
		else
			child:SetText(text or "")
		end
	end
	function scroll:GPGetText()
		return child.GetText and child:GetText() or ""
	end
	scroll.child = child
	return scroll
end

-- Éditeur commun (annonce, section de charte, objectif) ------------------------------------------

local editor

local function CreateEditor()
	local dialog = UI.CreateWindow("GuildkeepCommunicationEditor", 520, 470, "DIALOG")
	UI.SetWindowPortrait(dialog, GP.ICON)
	local inset = dialog.Inset

	local function Row(text, y)
		local label = Label(inset, text, "GameFontNormal")
		label:SetPoint("TOPLEFT", 12, y)
		label:SetWidth(90)
		return label
	end

	dialog.keyLabel = Row("Clé", -14)
	dialog.key = UI.InputBox(inset, 160, C.KEY_MAX)
	dialog.key:SetPoint("LEFT", dialog.keyLabel, "RIGHT", 8, 0)
	dialog.orderLabel = Label(inset, "Ordre", "GameFontNormal")
	dialog.orderLabel:SetPoint("LEFT", dialog.key, "RIGHT", 16, 0)
	dialog.order = UI.InputBox(inset, 30, 3, true)
	dialog.order:SetPoint("LEFT", dialog.orderLabel, "RIGHT", 8, 0)

	dialog.titleLabel = Row("Titre", -44)
	dialog.title = UI.InputBox(inset, 360, C.TITLE_MAX)
	dialog.title:SetPoint("LEFT", dialog.titleLabel, "RIGHT", 8, 0)

	-- Objectif : cible, unité, échéance.
	dialog.targetLabel = Row("Cible", -74)
	dialog.target = UI.InputBox(inset, 70, 7, true)
	dialog.target:SetPoint("LEFT", dialog.targetLabel, "RIGHT", 8, 0)
	dialog.unit = UI.InputBox(inset, 90, 20)
	dialog.unit:SetPoint("LEFT", dialog.target, "RIGHT", 12, 0)
	dialog.deadline = UI.InputBox(inset, 90, 10)
	dialog.deadline:SetPoint("LEFT", dialog.unit, "RIGHT", 12, 0)
	dialog.targetHint = Label(inset, "cible · unité · échéance (jj/mm/aaaa, facultative)", "GameFontHighlightSmall", COLOR.muted)
	dialog.targetHint:SetPoint("TOPLEFT", dialog.target, "BOTTOMLEFT", -4, -2)

	-- Annonce : modèle et événement lié.
	dialog.templateLabel = Row("Modèle", -74)
	dialog.template = UI.InputBox(inset, 120, C.KEY_MAX)
	dialog.template:SetPoint("LEFT", dialog.templateLabel, "RIGHT", 8, 0)
	dialog.applyTemplate = UI.Button(inset, "Appliquer", 80, function()
		local key = C.NormalizeKey(dialog.template:GetText())
		for _, t in ipairs(C.Get().templates) do
			if t.key == key then
				dialog.title:SetText(C.Render(t.title, dialog.event))
				dialog.body:GPSetText(C.Render(t.body, dialog.event))
				return
			end
		end
		GP.Chat(("aucun modèle « %s »."):format(key or ""))
	end)
	dialog.applyTemplate:SetPoint("LEFT", dialog.template, "RIGHT", 6, 0)
	dialog.saveTemplate = UI.Button(inset, "Enregistrer comme modèle", 170, function()
		Report(C.SaveTemplate(dialog.template:GetText(), dialog.title:GetText(), dialog.body:GPGetText()))
	end)
	dialog.saveTemplate:SetPoint("LEFT", dialog.applyTemplate, "RIGHT", 6, 0)
	dialog.eventButton = UI.Button(inset, "Événement : aucun", 360, function()
		-- Fait défiler les prochains événements, puis « aucun ».
		local list = Upcoming()
		local index = 0
		for i, event in ipairs(list) do
			if dialog.event and event.key == dialog.event.key then
				index = i
			end
		end
		dialog.event = list[index + 1]
		dialog.eventButton:SetText(dialog.event and ("Événement : %s (%s)"):format(dialog.event.title, date("%d/%m %H:%M", dialog.event.startsAt))
			or "Événement : aucun")
	end)
	dialog.eventButton:SetPoint("TOPLEFT", dialog.templateLabel, "BOTTOMLEFT", 98, -12)

	dialog.bodyLabel = Row("Texte", -136)
	local bodyFrame = CreateFrame("Frame", nil, inset)
	bodyFrame:SetPoint("TOPLEFT", 12, -156)
	bodyFrame:SetPoint("BOTTOMRIGHT", -30, 40)
	local bg = bodyFrame:CreateTexture(nil, "BACKGROUND")
	bg:SetAllPoints()
	bg:SetColorTexture(unpack(COLOR.panel))
	dialog.body = ScrollText(bodyFrame, true)
	dialog.body:SetPoint("TOPLEFT", 4, -4)
	dialog.body:SetPoint("BOTTOMRIGHT", -4, 4)

	dialog.hint = Label(inset, nil, "GameFontHighlightSmall", COLOR.muted)
	dialog.hint:SetPoint("BOTTOMLEFT", 12, 14)
	dialog.hint:SetPoint("RIGHT", -12, 0)

	dialog.save = UI.Button(dialog, "Enregistrer", 120, function()
		local ok
		if dialog.mode == "announcements" then
			ok = Report(C.PostAnnouncement(dialog.title:GetText(), dialog.body:GPGetText(), dialog.event and dialog.event.id, dialog.item))
		elseif dialog.mode == "handbook" then
			ok = Report(C.SaveSection(dialog.key:GetText(), dialog.title:GetText(), dialog.body:GPGetText(), tonumber(dialog.order:GetText())))
		else
			local deadline
			local d, m, y = (dialog.deadline:GetText() or ""):match("^(%d+)/(%d+)/(%d+)$")
			if d then
				deadline = time({ year = tonumber(y), month = tonumber(m), day = tonumber(d), hour = 23, min = 59, sec = 0 })
			end
			ok = Report(C.SaveGoal({
				title = dialog.title:GetText(), target = dialog.target:GetText(), unit = dialog.unit:GetText(),
				deadline = deadline, description = dialog.body:GPGetText(), closed = dialog.item and dialog.item.closed,
			}, dialog.item))
		end
		if ok then
			dialog:Hide()
		end
	end)
	dialog.save:SetPoint("BOTTOMRIGHT", -6, 4)
	local cancel = UI.Button(dialog, CANCEL or "Annuler", 96, function() dialog:Hide() end)
	cancel:SetPoint("RIGHT", dialog.save, "LEFT", -4, 0)
	return dialog
end

local function OpenEditor(mode, item)
	editor = editor or CreateEditor()
	local dialog = editor
	dialog.mode, dialog.item, dialog.event = mode, item, nil
	local isAnnouncement, isHandbook, isGoal = mode == "announcements", mode == "handbook", mode == "goals"
	UI.SetWindowTitle(dialog, isAnnouncement and (item and "Modifier l'annonce" or "Nouvelle annonce")
		or isHandbook and (item and "Modifier la section" or "Nouvelle section de la charte")
		or (item and "Modifier l'objectif" or "Nouvel objectif"))
	for _, element in ipairs({ dialog.keyLabel, dialog.key, dialog.orderLabel, dialog.order }) do
		element:SetShown(isHandbook)
	end
	for _, element in ipairs({ dialog.targetLabel, dialog.target, dialog.unit, dialog.deadline, dialog.targetHint }) do
		element:SetShown(isGoal)
	end
	for _, element in ipairs({ dialog.templateLabel, dialog.template, dialog.applyTemplate, dialog.saveTemplate, dialog.eventButton }) do
		element:SetShown(isAnnouncement)
	end
	dialog.key:SetText(item and item.key or "")
	dialog.key:SetEnabled(item == nil)
	dialog.order:SetText(tostring(item and item.order or 10))
	dialog.title:SetText(item and item.title or "")
	dialog.target:SetText(item and tostring(item.target or "") or "")
	dialog.unit:SetText(item and item.unit or "")
	dialog.deadline:SetText(item and item.deadline and date("%d/%m/%Y", item.deadline) or "")
	dialog.template:SetText("")
	dialog.body:GPSetText(item and (item.body or item.description) or "")
	if isAnnouncement and item and item.eventId then
		for _, event in ipairs(Upcoming()) do
			if event.id == item.eventId then
				dialog.event = event
			end
		end
	end
	dialog.eventButton:SetText(dialog.event and ("Événement : %s"):format(dialog.event.title) or "Événement : aucun")
	dialog.hint:SetText(isAnnouncement and "Les modèles remplacent {event}, {date} et {guild}. Publiée sur Discord si la guilde a réglé un salon d'annonces."
		or isHandbook and "La clé identifie la section (ex. reglement, butin) ; l'ordre la range dans la charte."
		or "Chacun pourra y contribuer depuis l'onglet ou sur Discord.")
	dialog:Show()
	dialog.title:SetFocus()
end

-- Liste de gauche ---------------------------------------------------------------------------------

local function SetupRow(row)
	UI.AddRowBackground(row)
	row.title = Label(row, nil, "GameFontNormal")
	row.title:SetPoint("TOPLEFT", 10, -6)
	row.title:SetPoint("RIGHT", -8, 0)
	row.title:SetWordWrap(false)
	row.sub = Label(row, nil, "GameFontHighlightSmall", COLOR.muted)
	row.sub:SetPoint("TOPLEFT", row.title, "BOTTOMLEFT", 0, -4)
	row.sub:SetPoint("RIGHT", -8, 0)
	row.sub:SetWordWrap(false)
	row:SetScript("OnClick", function(self)
		view.selected[view.section] = self.item.key
		PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		GP.RefreshUI()
	end)
end

local function Progress(goal)
	local target = goal.target or 0
	local progress = goal.progress or 0
	return progress, target, target > 0 and math.min(1, progress / target) or 0
end

local function UpdateRow(row, data)
	local item = data.item
	row.item = item
	row.stripe:SetShown(data.index % 2 == 0)
	row.selectedTex:SetShown(item.key == view.selected[view.section])
	local pending = item.pendingSync and Paint(COLOR.muted, " · pas encore sur le serveur") or ""
	if view.section == "announcements" then
		local unread = not GuildkeepDB.announcementsSeen[item.key] and not (item.clientId and GuildkeepDB.announcementsSeen[item.clientId])
		row.title:SetText((unread and Paint(COLOR.positive, "● ") or "") .. item.title)
		row.sub:SetText(("%s · %s%s"):format(item.author or "?", date("%d/%m %H:%M", item.createdAt or 0), pending))
	elseif view.section == "handbook" then
		row.title:SetText(item.title)
		row.sub:SetText((item.updatedBy and ("par " .. item.updatedBy) or item.key) .. pending)
	else
		local progress, target, ratio = Progress(item)
		row.title:SetText(item.title .. (item.closed and Paint(COLOR.muted, " (clos)") or ""))
		row.sub:SetText(("%d/%d %s · %d %%%s"):format(progress, target, item.unit or "", math.floor(ratio * 100 + 0.5), pending))
	end
end

-- Détail : contributions d'un objectif ------------------------------------------------------------

local function SetupContributionRow(row)
	UI.AddRowBackground(row)
	row.text = Label(row, nil, "GameFontHighlight")
	row.text:SetPoint("LEFT", 8, 0)
	row.text:SetPoint("RIGHT", -8, 0)
	row.text:SetWordWrap(false)
end

local function UpdateContributionRow(row, data)
	row.stripe:SetShown(data.index % 2 == 0)
	local c = data.contribution
	row.text:SetText(("%s : %d%s%s%s"):format(c.character or "?", c.amount or 0, data.unit and (" " .. data.unit) or "",
		c.note and Paint(COLOR.muted, " — « " .. c.note .. " »") or "", c.pendingSync and Paint(COLOR.muted, " · en cours d'envoi") or ""))
end

-- Construction -------------------------------------------------------------------------------------

local function BuildGuildTab(_, parent)
	panel = parent
	local left = CreateFrame("Frame", nil, parent)
	left:SetPoint("TOPLEFT", 8, -6)
	left:SetPoint("BOTTOMLEFT", 8, 4)
	left:SetWidth(318)

	panel.sectionButtons = {}
	local previous
	for _, def in ipairs(SECTIONS) do
		local button = UI.Toggle(left, def.label, function(self)
			view.section = self.commSection
			GP.RefreshUI()
		end)
		button.commSection = def.key
		button:SetPoint("TOPLEFT", previous or left, previous and "TOPRIGHT" or "TOPLEFT", previous and 2 or 0, 0)
		panel.sectionButtons[#panel.sectionButtons + 1] = button
		previous = button
	end
	panel.new = UI.Button(left, "Nouveau", 80, function()
		OpenEditor(view.section, nil)
	end)
	panel.new:SetPoint("TOPRIGHT", 0, 0)

	panel.list = UI.CreateScrollList(left, 42, SetupRow, UpdateRow)
	panel.list:SetPoint("TOPLEFT", 0, -32)
	panel.list:SetPoint("BOTTOMRIGHT", -18, 0)
	panel.empty = Label(left, nil, "GameFontHighlight", COLOR.muted)
	panel.empty:SetPoint("CENTER", panel.list, "CENTER")
	panel.empty:SetJustifyH("CENTER")

	local detail = CreateFrame("Frame", nil, parent)
	detail:SetPoint("TOPLEFT", left, "TOPRIGHT", 24, 0)
	detail:SetPoint("BOTTOMRIGHT", -8, 4)
	panel.detail = detail

	panel.delete = UI.Button(detail, "Retirer", 80, function()
		local item = panel.current
		if not item then
			return
		end
		if view.section == "announcements" then
			Report(C.DeleteAnnouncement(item))
		elseif view.section == "handbook" then
			Report(C.SaveSection(item.key, item.title, item.body, item.order, true))
		else
			Report(C.SetGoalClosed(item, not item.closed))
		end
	end)
	panel.delete:SetPoint("TOPRIGHT", 0, 0)
	panel.edit = UI.Button(detail, "Modifier", 80, function()
		if panel.current then
			OpenEditor(view.section, panel.current)
		end
	end)
	panel.edit:SetPoint("RIGHT", panel.delete, "LEFT", -4, 0)

	panel.title = Label(detail, nil, "GameFontNormalLarge")
	panel.title:SetPoint("TOPLEFT", 0, -2)
	panel.title:SetPoint("RIGHT", panel.edit, "LEFT", -8, 0)
	panel.title:SetWordWrap(false)
	panel.sub = Label(detail, nil, "GameFontHighlightSmall", COLOR.muted)
	panel.sub:SetPoint("TOPLEFT", panel.title, "BOTTOMLEFT", 0, -6)
	panel.sub:SetPoint("RIGHT", 0, 0)
	panel.sub:SetWordWrap(false)

	-- Objectif : barre d'avancement, contributions, et la sienne.
	panel.bar = CreateFrame("StatusBar", nil, detail)
	panel.bar:SetHeight(16)
	panel.bar:SetPoint("TOPLEFT", panel.sub, "BOTTOMLEFT", 0, -10)
	panel.bar:SetPoint("RIGHT", 0, 0)
	panel.bar:SetStatusBarTexture("Interface\\TargetingFrame\\UI-StatusBar")
	panel.bar:SetStatusBarColor(unpack(COLOR.barMax))
	panel.bar:SetMinMaxValues(0, 1)
	local barBg = panel.bar:CreateTexture(nil, "BACKGROUND")
	barBg:SetAllPoints()
	barBg:SetColorTexture(unpack(COLOR.panel))
	panel.barText = Label(panel.bar, nil, "GameFontHighlightSmall")
	panel.barText:SetPoint("CENTER")

	panel.body = ScrollText(detail, false)
	panel.body:SetPoint("TOPLEFT", panel.sub, "BOTTOMLEFT", 0, -10)
	panel.body:SetPoint("BOTTOMRIGHT", -24, 36)

	panel.contributions = UI.CreateScrollList(detail, 22, SetupContributionRow, UpdateContributionRow)
	panel.contributions:SetPoint("TOPLEFT", panel.bar, "BOTTOMLEFT", 0, -110)
	panel.contributions:SetPoint("BOTTOMRIGHT", -18, 36)
	panel.amount = UI.InputBox(detail, 60, 7, true)
	panel.amount:SetPoint("BOTTOMLEFT", 6, 8)
	panel.note = UI.InputBox(detail, 200, C.NOTE_MAX)
	panel.note:SetPoint("LEFT", panel.amount, "RIGHT", 10, 0)
	panel.contribute = UI.Button(detail, "Contribuer", 100, function()
		local goal = panel.current
		if goal and Report(C.Contribute(goal, panel.amount:GetText(), panel.note:GetText())) then
			panel.amount:SetText("")
			panel.note:SetText("")
		end
	end)
	panel.contribute:SetPoint("LEFT", panel.note, "RIGHT", 8, 0)

	panel.noSelection = Label(detail, nil, "GameFontHighlight", COLOR.muted)
	panel.noSelection:SetPoint("CENTER")
	panel.noSelection:SetJustifyH("CENTER")

	local divider = parent:CreateTexture(nil, "ARTWORK")
	divider:SetColorTexture(1, 0.82, 0, 0.18)
	divider:SetWidth(1)
	divider:SetPoint("TOPLEFT", left, "TOPRIGHT", 12, 0)
	divider:SetPoint("BOTTOMLEFT", left, "BOTTOMRIGHT", 12, 0)
end

local EMPTY = {
	announcements = "Aucune annonce pour l'instant.",
	handbook = "La charte de la guilde est vide.",
	goals = "Aucun objectif en cours.",
}

local function RefreshGuild()
	local data = C.Get()
	local officer = GP.IsOfficer()
	for _, button in ipairs(panel.sectionButtons) do
		button:GPSetSelected(button.commSection == view.section)
	end
	panel.new:SetShown(officer)

	local list = data[view.section]
	local items = {}
	for i, item in ipairs(list) do
		items[i] = { item = item, index = i }
	end
	panel.list:GPSetItems(items)
	panel.empty:SetText(EMPTY[view.section] .. (officer and "\n« Nouveau » pour en créer." or ""))
	panel.empty:SetShown(#items == 0)

	-- Sélection par défaut : le premier élément.
	local current
	for _, item in ipairs(list) do
		if item.key == view.selected[view.section] or (item.clientId and item.clientId == view.selected[view.section]) then
			current = item
		end
	end
	current = current or list[1]
	panel.current = current
	view.selected[view.section] = current and current.key

	local isGoal = view.section == "goals"
	for _, element in ipairs({ panel.title, panel.sub }) do
		element:SetShown(current ~= nil)
	end
	panel.body:SetShown(current ~= nil and not isGoal)
	for _, element in ipairs({ panel.bar, panel.contributions, panel.amount, panel.note, panel.contribute }) do
		element:SetShown(current ~= nil and isGoal)
	end
	panel.edit:SetShown(current ~= nil and officer)
	panel.delete:SetShown(current ~= nil and officer)
	panel.noSelection:SetShown(current == nil)
	panel.noSelection:SetText(EMPTY[view.section])
	if not current then
		return
	end

	panel.title:SetText(current.title)
	if view.section == "announcements" then
		-- Lue dès qu'elle est affichée.
		GuildkeepDB.announcementsSeen[current.key] = GP.Now()
		local event = EventTitle(current.eventId)
		panel.sub:SetText(("par %s · %s%s"):format(current.author or "?", date("%d/%m/%Y %H:%M", current.createdAt or 0),
			event and (" · " .. event) or ""))
		panel.body:GPSetText(current.body)
		panel.delete:SetText("Retirer")
	elseif view.section == "handbook" then
		panel.sub:SetText(current.updatedBy and ("mise à jour par %s"):format(current.updatedBy) or "")
		panel.body:GPSetText(current.body)
		panel.delete:SetText("Retirer")
	else
		local progress, target, ratio = Progress(current)
		panel.sub:SetText(((current.deadline and ("échéance le %s · "):format(date("%d/%m/%Y", current.deadline))) or "")
			.. ("créé par %s"):format(current.createdBy or "?") .. (current.description and (" · " .. current.description) or ""))
		panel.bar:SetValue(ratio)
		panel.barText:SetText(("%d / %d %s (%d %%)"):format(progress, target, current.unit or "", math.floor(ratio * 100 + 0.5)))
		local rows = {}
		for i, c in ipairs(current.contributions or {}) do
			rows[i] = { contribution = c, unit = current.unit, index = i }
		end
		panel.contributions:GPSetItems(rows)
		panel.contribute:SetEnabled(not current.closed)
		panel.delete:SetText(current.closed and "Rouvrir" or "Clore")
	end
end

GP.Modules.Define("communication", "ui", function(ctx)
	local function Section(key)
		return function()
			view.section = key
			GP.RefreshUI()
		end
	end
	ctx:Tutorial("communication", {
		tab = "tabGuild",
		title = "Communication",
		steps = {
			{ prepare = Section("announcements"), target = function() return panel and panel.sectionButtons and panel.sectionButtons[1] end, title = "Annonces, Charte, Objectifs",
				text = "Trois sections : les annonces des officiers, la charte de la guilde et les objectifs communs." },
			{ prepare = Section("announcements"), target = function() return panel and panel.list end, title = "Les annonces",
				text = "Les plus récentes en haut ; un point vert marque une annonce non lue. Clique pour la lire en entier (elle passe alors en lue)." },
			{ prepare = Section("announcements"), target = function() return panel and panel.new end, title = "Nouveau",
				text = "Les officiers rédigent une annonce (titre, texte), partent d'un modèle (« Appliquer », ou « Enregistrer comme modèle ») et peuvent la lier à un événement. {event}, {date} et {guild} sont remplacés.",
				hint = "Réservé aux officiers." },
			{ prepare = Section("announcements"), target = function() return panel and panel.edit end, title = "Modifier et Retirer",
				text = "Les officiers corrigent ou retirent l'élément affiché ; sur un objectif, « Clore » ou « Rouvrir ».", hint = "Réservé aux officiers." },
			{ prepare = Section("handbook"), target = function() return panel and panel.body end, title = "La charte",
				text = "Le règlement, les règles de butin et les autres sections écrites par les officiers (une clé et un ordre par section).",
				hint = "La charte est vide pour l'instant." },
			{ prepare = Section("goals"), target = function() return panel and panel.bar end, title = "Les objectifs",
				text = "Un objectif chiffré (200 flacons, de l'or pour les réparations…) et son avancement, avec les contributions de chacun.",
				hint = "Aucun objectif en cours pour l'instant." },
			{ prepare = Section("goals"), target = function() return panel and panel.contribute end, title = "Contribuer",
				text = "Indique la quantité que tu apportes et une note, puis « Contribuer » : l'avancement se met à jour pour toute la guilde.",
				hint = "Choisis d'abord un objectif ouvert." },
		},
	})
	ctx:Slash({ "guilde", "guild", "annonces", "charte", "objectifs" }, function()
		GP.ShowTab("tabGuild")
	end)
	ctx:AddTab({
		label = "Guilde",
		key = "tabGuild",
		order = 80,
		build = BuildGuildTab,
		refresh = RefreshGuild,
		badge = C.UnreadCount,
		badgeHint = function(n)
			return ("%d annonce%s non lue%s"):format(n, n > 1 and "s" or "", n > 1 and "s" or "")
		end,
	})
end)
