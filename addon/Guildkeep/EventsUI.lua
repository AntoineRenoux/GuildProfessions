local _, GP = ...

-- Module Événements, partie interface : liste ou calendrier à gauche, détail
-- de l'événement à droite (réponse du joueur, composition, roster). Les données
-- et les droits vivent dans Events.lua ; ce fichier ne fait qu'afficher et
-- relayer. Un module qui dépend des événements (la soirée de raid) ajoute ses
-- propres sections au bas du détail, et ses lignes au texte à copier, par
-- GP.EventsUI.Extend.

local E = GP.Events
local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label
local Button, Toggle, InputBox = UI.Button, UI.Toggle, UI.InputBox

local ROLE_ICONS = {
	tank = "Interface\\Icons\\INV_Shield_06",
	healer = "Interface\\Icons\\Spell_Holy_FlashHeal",
	melee = "Interface\\Icons\\INV_Sword_27",
	ranged = "Interface\\Icons\\INV_Weapon_Bow_07",
}
local RESPONSE_ICONS = {
	yes = "Interface\\RaidFrame\\ReadyCheck-Ready",
	tentative = "Interface\\RaidFrame\\ReadyCheck-Waiting",
	no = "Interface\\RaidFrame\\ReadyCheck-NotReady",
}
local RESPONSE_COLORS = { yes = COLOR.positive, tentative = COLOR.gold, no = COLOR.negative, none = COLOR.muted }
local PLACEMENT_COLORS = { confirmed = COLOR.positive, standby = COLOR.info, bench = COLOR.muted, pending = COLOR.text }

local DAYS = { "dimanche", "lundi", "mardi", "mercredi", "jeudi", "vendredi", "samedi" }
local MONTHS = { "janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre" }

local view = {
	mode = "list", -- list | calendar
	showPast = false,
	selected = nil, -- clé de l'événement affiché
	month = nil, -- { year, month } du calendrier
	day = nil, -- jour choisi dans le calendrier (AAAA-MM-JJ)
	role = nil, -- rôle choisi avant de répondre
	section = "roster", -- roster, ou la clé d'une section ajoutée par une extension (bas du détail)
}

local panel -- cadre de l'onglet

-- Extensions du détail ----------------------------------------------------------------------
-- Une extension apporte `sections` (liste de { key, label, build(detail) → cadre,
-- refresh(cadre, événement, peutGérer), title(événement) facultatif }) et
-- `exportLines(événement)` (lignes ajoutées au texte à copier). Elle s'ajoute
-- avant la construction de l'onglet, au chargement de son module (`owner`).
-- Ses fonctions sont appelées sous protection : une erreur met son module en
-- échec (GP.Modules.Fault, qui retire l'extension) sans casser l'onglet.

GP.EventsUI = {}
local extensions = {}

local function RemoveExtension(extension)
	for i, other in ipairs(extensions) do
		if other == extension then
			table.remove(extensions, i)
			return
		end
	end
end

-- Affiche une section du détail (roster, ou celle d'une extension) ; sert aux tutoriels.
function GP.EventsUI.ShowSection(key)
	view.section = key
	GP.RefreshUI()
end

-- Bouton d'une section du détail (nil si absente).
function GP.EventsUI.SectionButton(key)
	local detail = panel and panel.detail
	for _, button in ipairs(detail and detail.sectionButtons or {}) do
		if button.section == key then
			return button
		end
	end
end

-- Détail de l'événement affiché (boutons partagés avec les extensions, ex. « Copier la soirée »).
function GP.EventsUI.Detail()
	return panel and panel.detail
end

function GP.EventsUI.Extend(extension, owner)
	extension.owner = owner
	extensions[#extensions + 1] = extension
	return function()
		RemoveExtension(extension)
	end
end

-- Appel protégé d'une fonction d'extension ; renvoie false si elle échoue.
local function CallExtension(extension, fn, ...)
	if extension.failed then
		return false
	end
	local ok, result = pcall(fn, ...)
	if ok then
		return true, result
	end
	extension.failed = true
	RemoveExtension(extension)
	GP.Modules.Fault(extension.owner, result)
	return false
end

-- Extension qui apporte la section `key`, si elle est toujours active.
local function SectionOwner(key)
	for _, extension in ipairs(extensions) do
		for _, section in ipairs(extension.sections or {}) do
			if section.key == key then
				return extension
			end
		end
	end
end

local function ExtensionSections()
	local list = {}
	for _, extension in ipairs(extensions) do
		for _, section in ipairs(extension.sections or {}) do
			list[#list + 1] = section
		end
	end
	return list
end

-- Texte à copier : le roster, puis ce qu'ajoutent les extensions.
local function ExportText(event)
	local lines = { E.RosterText(event) }
	for _, extension in ipairs({ unpack(extensions) }) do
		if extension.exportLines then
			local ok, extra = CallExtension(extension, extension.exportLines, event)
			if ok and type(extra) == "table" then
				for _, line in ipairs(extra) do
					lines[#lines + 1] = line
				end
			end
		end
	end
	return table.concat(lines, "\n")
end

local function RoleIcon(texture, role)
	texture:SetTexture(ROLE_ICONS[role] or "Interface\\Icons\\INV_Misc_QuestionMark")
	texture:SetTexCoord(0.08, 0.92, 0.08, 0.92)
end

local function ShortDate(t)
	local d = date("*t", t)
	return ("%s %02d/%02d · %02d:%02d"):format(DAYS[d.wday]:sub(1, 3) .. ".", d.day, d.month, d.hour, d.min)
end

local function LongDate(t)
	local d = date("*t", t)
	return ("%s %d %s %d à %02d:%02d"):format(DAYS[d.wday], d.day, MONTHS[d.month], d.year, d.hour, d.min)
end

local function DayKey(t)
	return date("%Y-%m-%d", t)
end

local function ClassName(row)
	local r, g, b = UI.ClassColor(row.classFile)
	return UI.Hex({ r, g, b }) .. row.character .. "|r"
end

local function RoleButton(parent, role, onClick)
	local button = CreateFrame("Button", nil, parent)
	button:SetSize(28, 28)
	button.icon = button:CreateTexture(nil, "ARTWORK")
	button.icon:SetAllPoints()
	RoleIcon(button.icon, role)
	button.border = button:CreateTexture(nil, "OVERLAY")
	button.border:SetTexture("Interface\\Buttons\\CheckButtonHilight")
	button.border:SetBlendMode("ADD")
	button.border:SetAllPoints()
	button.border:Hide()
	local highlight = button:CreateTexture(nil, "HIGHLIGHT")
	highlight:SetTexture("Interface\\Buttons\\ButtonHilight-Square")
	highlight:SetBlendMode("ADD")
	highlight:SetAllPoints()
	button:SetScript("OnClick", function()
		PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		onClick(role)
	end)
	button:SetScript("OnEnter", function(self)
		GameTooltip:SetOwner(self, "ANCHOR_TOP")
		GameTooltip:SetText(E.ROLE_LABELS[role])
		GameTooltip:Show()
	end)
	button:SetScript("OnLeave", GameTooltip_Hide)
	function button:GPSetSelected(selected)
		self.border:SetShown(selected)
		self.icon:SetDesaturated(not selected)
		self.icon:SetAlpha(selected and 1 or 0.55)
	end
	return button
end

-- Fenêtre de création / modification ------------------------------------------------------

local editor

local function ParseDate(dialog)
	local day, month, year = tonumber(dialog.day:GetText()), tonumber(dialog.month:GetText()), tonumber(dialog.year:GetText())
	local hour, minute = tonumber(dialog.hour:GetText()), tonumber(dialog.minute:GetText())
	if not (day and month and year and hour and minute) or month < 1 or month > 12 or day < 1 or day > 31
		or hour > 23 or minute > 59 or year < 2000 then
		return nil
	end
	local t = time({ year = year, month = month, day = day, hour = hour, min = minute, sec = 0 })
	-- time() normalise un 31 février en mars : on refuse plutôt que de surprendre.
	local check = date("*t", t)
	if check.day ~= day or check.month ~= month then
		return nil
	end
	return t
end

local function CreateEditor()
	local dialog = UI.CreateWindow("GuildkeepEventEditor", 420, 400, "DIALOG")
	if dialog.SetPortraitToAsset then
		dialog:SetPortraitToAsset("Interface\\Icons\\INV_Misc_Note_01")
	end
	local inset = dialog.Inset
	local y = -14

	local function Row(text)
		local label = Label(inset, text, "GameFontNormal")
		label:SetPoint("TOPLEFT", 12, y)
		label:SetWidth(96)
		y = y - 34
		return label
	end

	dialog.title = InputBox(inset, 260, 80)
	dialog.title:SetPoint("LEFT", Row("Titre"), "RIGHT", 8, 0)

	local kindLabel = Row("Type")
	dialog.kinds = {}
	local previous
	for _, kind in ipairs(E.KINDS) do
		local button = Toggle(inset, E.KIND_LABELS[kind], function(self)
			dialog.kind = self.kind
			for _, other in ipairs(dialog.kinds) do
				other:GPSetSelected(other.kind == dialog.kind)
			end
		end)
		button.kind = kind
		button:SetPoint("LEFT", previous or kindLabel, "RIGHT", previous and 2 or 4, 0)
		dialog.kinds[#dialog.kinds + 1] = button
		previous = button
	end

	local dateLabel = Row("Date")
	dialog.day = InputBox(inset, 26, 2, true)
	dialog.day:SetPoint("LEFT", dateLabel, "RIGHT", 12, 0)
	dialog.month = InputBox(inset, 26, 2, true)
	dialog.month:SetPoint("LEFT", dialog.day, "RIGHT", 12, 0)
	dialog.year = InputBox(inset, 44, 4, true)
	dialog.year:SetPoint("LEFT", dialog.month, "RIGHT", 12, 0)
	local at = Label(inset, "à", "GameFontHighlight")
	at:SetPoint("LEFT", dialog.year, "RIGHT", 10, 0)
	dialog.hour = InputBox(inset, 26, 2, true)
	dialog.hour:SetPoint("LEFT", at, "RIGHT", 12, 0)
	dialog.minute = InputBox(inset, 26, 2, true)
	dialog.minute:SetPoint("LEFT", dialog.hour, "RIGHT", 12, 0)
	local dateHint = Label(inset, "jour / mois / année, heure : minutes (ton heure locale)", "GameFontHighlightSmall", COLOR.muted)
	dateHint:SetPoint("TOPLEFT", dialog.day, "BOTTOMLEFT", -4, -2)
	y = y - 10

	local targetLabel = Row("Composition")
	dialog.targets = {}
	previous = targetLabel
	for _, role in ipairs(E.ROLES) do
		local icon = inset:CreateTexture(nil, "ARTWORK")
		icon:SetSize(18, 18)
		RoleIcon(icon, role)
		icon:SetPoint("LEFT", previous, "RIGHT", previous == targetLabel and 8 or 10, 0)
		local box = InputBox(inset, 26, 2, true)
		box:SetPoint("LEFT", icon, "RIGHT", 8, 0)
		dialog.targets[role] = box
		previous = box
	end
	local targetHint = Label(inset, "tanks, soigneurs, DPS mêlée, DPS distance — 0 : pas d'objectif", "GameFontHighlightSmall", COLOR.muted)
	targetHint:SetPoint("TOPLEFT", targetLabel, "BOTTOMLEFT", 104, -6)
	y = y - 10

	dialog.description = InputBox(inset, 260, 250)
	dialog.description:SetPoint("LEFT", Row("Détails"), "RIGHT", 8, 0)

	dialog.error = Label(inset, nil, "GameFontHighlightSmall", COLOR.negative)
	dialog.error:SetPoint("BOTTOMLEFT", 12, 10)
	dialog.error:SetPoint("RIGHT", -12, 0)

	dialog.save = Button(dialog, "Enregistrer", 110, function()
		local fields = {
			title = dialog.title:GetText(),
			kind = dialog.kind,
			startsAt = ParseDate(dialog),
			description = dialog.description:GetText(),
			cancelled = dialog.event and dialog.event.cancelled,
		}
		for role, box in pairs(dialog.targets) do
			fields[role] = tonumber(box:GetText()) or 0
		end
		local key, err
		if dialog.event then
			key, err = E.Update(dialog.event, fields)
		else
			key, err = E.Create(fields)
		end
		if not key then
			dialog.error:SetText(err or "enregistrement impossible")
			return
		end
		view.selected = key
		view.day = DayKey(fields.startsAt)
		dialog:Hide()
		GP.RefreshUI()
	end)
	dialog.save:SetPoint("BOTTOMRIGHT", -6, 4)
	local cancel = Button(dialog, CANCEL or "Annuler", 96, function() dialog:Hide() end)
	cancel:SetPoint("RIGHT", dialog.save, "LEFT", -4, 0)
	return dialog
end

local function OpenEditor(event)
	editor = editor or CreateEditor()
	local dialog = editor
	dialog.event = event
	UI.SetWindowTitle(dialog, event and "Modifier l'événement" or "Nouvel événement")
	local startsAt = event and event.startsAt
	if not startsAt then
		-- Par défaut : le lendemain à 20 h 45, ou le jour choisi au calendrier.
		local base = date("*t", time() + 24 * 3600)
		if view.mode == "calendar" and view.day then
			local y, m, d = view.day:match("(%d+)-(%d+)-(%d+)")
			base = { year = tonumber(y), month = tonumber(m), day = tonumber(d) }
		end
		startsAt = time({ year = base.year, month = base.month, day = base.day, hour = 20, min = 45, sec = 0 })
	end
	local d = date("*t", startsAt)
	dialog.title:SetText(event and event.title or "")
	dialog.kind = event and event.kind or "raid"
	for _, button in ipairs(dialog.kinds) do
		button:GPSetSelected(button.kind == dialog.kind)
	end
	dialog.day:SetText(d.day)
	dialog.month:SetText(d.month)
	dialog.year:SetText(d.year)
	dialog.hour:SetText(("%02d"):format(d.hour))
	dialog.minute:SetText(("%02d"):format(d.min))
	for role, box in pairs(dialog.targets) do
		box:SetText(tostring(event and event[role] or 0))
	end
	dialog.description:SetText(event and event.description or "")
	dialog.error:SetText("")
	dialog:Show()
	dialog.title:SetFocus()
end

-- Fenêtre de confirmation, déclarée au chargement du module.
local function DefinePopups()
	StaticPopupDialogs["GUILDKEEP_CANCEL_EVENT"] = {
		text = "Annuler l'événement « %s » ? Les inscrits le verront annulé.",
		button1 = YES or "Oui",
		button2 = NO or "Non",
		OnAccept = function(_, event)
			E.SetCancelled(event, true)
		end,
		timeout = 0,
		whileDead = true,
		hideOnEscape = true,
		showAlert = true,
	}
end

-- Liste des événements ---------------------------------------------------------------------

local function EventSummary(event)
	local counts = E.Counts(event)
	local parts = { ShortDate(event.startsAt) }
	if event.cancelled then
		parts[#parts + 1] = Paint(COLOR.negative, "annulé")
	else
		parts[#parts + 1] = ("%d présent%s"):format(counts.total, counts.total > 1 and "s" or "")
	end
	return table.concat(parts, " · ")
end

local function SetupEventRow(row)
	UI.AddRowBackground(row)
	row.response = row:CreateTexture(nil, "ARTWORK")
	row.response:SetSize(16, 16)
	row.response:SetPoint("RIGHT", -8, 0)
	row.kind = Label(row, nil, "GameFontHighlightSmall", COLOR.muted)
	row.kind:SetPoint("TOPRIGHT", row.response, "TOPLEFT", -6, 4)
	row.title = Label(row, nil, "GameFontNormal")
	row.title:SetPoint("TOPLEFT", 10, -6)
	row.title:SetPoint("RIGHT", row.kind, "LEFT", -6, 0)
	row.title:SetWordWrap(false)
	row.sub = Label(row, nil, "GameFontHighlightSmall", COLOR.muted)
	row.sub:SetPoint("TOPLEFT", row.title, "BOTTOMLEFT", 0, -4)
	row.sub:SetPoint("RIGHT", row.response, "LEFT", -6, 0)
	row.sub:SetWordWrap(false)
	row:SetScript("OnClick", function(self)
		view.selected = self.event.key
		view.role = nil
		PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		GP.RefreshUI()
	end)
end

local function UpdateEventRow(row, data)
	local event = data.event
	row.event = event
	row.stripe:SetShown(data.index % 2 == 0)
	row.selectedTex:SetShown(event.key == view.selected)
	local past = not E.IsUpcoming(event)
	row.title:SetText(event.title)
	row.title:SetTextColor(unpack((event.cancelled or past) and COLOR.muted or COLOR.gold))
	row.kind:SetText(E.KIND_LABELS[event.kind] or "")
	row.sub:SetText(EventSummary(event))
	local mine = event.mine and event.mine.response
	row.response:SetTexture(RESPONSE_ICONS[mine] or "Interface\\GossipFrame\\AvailableQuestIcon")
	row.response:SetShown(not event.cancelled and (mine ~= nil or not past))
	row.response:SetDesaturated(RESPONSE_ICONS[mine] == nil)
end

-- Calendrier --------------------------------------------------------------------------------

local function SetupDayCell(cell)
	UI.AddRowBackground(cell)
	cell.number = Label(cell, nil, "GameFontHighlightSmall")
	cell.number:SetPoint("TOPLEFT", 4, -3)
	cell.count = Label(cell, nil, "GameFontNormalSmall")
	cell.count:SetPoint("BOTTOMRIGHT", -4, 3)
	cell.today = cell:CreateTexture(nil, "BORDER")
	cell.today:SetPoint("TOPLEFT", 1, -1)
	cell.today:SetPoint("BOTTOMRIGHT", -1, 1)
	cell.today:SetColorTexture(0.4, 0.65, 1, 0.18)
	cell:SetScript("OnClick", function(self)
		view.day = self.dayKey
		local first = self.events and self.events[1]
		if first then
			view.selected = first.key
		end
		PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		GP.RefreshUI()
	end)
	cell:SetScript("OnEnter", function(self)
		if not self.events or #self.events == 0 then
			return
		end
		GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
		for _, event in ipairs(self.events) do
			GameTooltip:AddDoubleLine(event.title, date("%H:%M", event.startsAt), 1, 0.82, 0, 1, 1, 1)
		end
		GameTooltip:Show()
	end)
	cell:SetScript("OnLeave", GameTooltip_Hide)
end

local function RefreshCalendar(events)
	local now = date("*t")
	view.month = view.month or { year = now.year, month = now.month }
	local year, month = view.month.year, view.month.month
	panel.monthText:SetText(("%s %d"):format(MONTHS[month], year))

	local byDay = {}
	for _, event in ipairs(events) do
		local key = DayKey(event.startsAt)
		byDay[key] = byDay[key] or {}
		table.insert(byDay[key], event)
	end

	-- Semaine commençant le lundi.
	local first = time({ year = year, month = month, day = 1, hour = 12 })
	local offset = (date("*t", first).wday + 5) % 7
	local today = DayKey(time())
	for i, cell in ipairs(panel.cells) do
		local t = first + (i - 1 - offset) * 24 * 3600
		local d = date("*t", t)
		local key = DayKey(t)
		cell.dayKey = key
		cell.events = byDay[key]
		cell.number:SetText(d.day)
		cell.number:SetTextColor(unpack(d.month == month and COLOR.text or COLOR.muted))
		cell.today:SetShown(key == today)
		cell.selectedTex:SetShown(key == view.day)
		local count = cell.events and #cell.events or 0
		cell.count:SetText(count > 0 and (count == 1 and "•" or ("•" .. count)) or "")
		cell.stripe:SetShown(count > 0)
	end
end

-- Détail : roster ---------------------------------------------------------------------------

local SECTIONS = {
	{ key = "confirmed", label = "Confirmés" },
	{ key = "standby", label = "Remplaçants" },
	{ key = "bench", label = "Banc" },
	{ key = "yes", label = "Présents, à placer" },
	{ key = "tentative", label = "Peut-être" },
	{ key = "no", label = "Absents" },
}

local function SectionOf(row)
	if row.placement ~= "pending" then
		return row.placement
	end
	if row.response == "yes" or row.response == "tentative" or row.response == "no" then
		return row.response
	end
	return "yes" -- placé puis remis en attente sans réponse : à placer
end

local function SetupRosterRow(row)
	UI.AddRowBackground(row)
	row.header = Label(row, nil, "GameFontNormal")
	row.header:SetPoint("LEFT", 6, 0)
	row.role = row:CreateTexture(nil, "ARTWORK")
	row.role:SetSize(18, 18)
	row.role:SetPoint("LEFT", 6, 0)
	row.online = row:CreateTexture(nil, "ARTWORK")
	row.online:SetSize(14, 14)
	row.online:SetPoint("LEFT", row.role, "RIGHT", 4, 0)
	row.name = Label(row, nil, "GameFontHighlight")
	row.name:SetPoint("LEFT", row.online, "RIGHT", 4, 0)
	row.name:SetWidth(150)
	row.name:SetWordWrap(false)
	row.detail = Label(row, nil, "GameFontHighlightSmall", COLOR.muted)
	row.detail:SetPoint("LEFT", row.name, "RIGHT", 6, 0)
	row.detail:SetWordWrap(false)

	row.actions = {}
	local previous
	for i = 3, 1, -1 do
		local button = CreateFrame("Button", nil, row, "UIPanelButtonTemplate")
		button:SetSize(78, 20)
		if previous then
			button:SetPoint("RIGHT", previous, "LEFT", -2, 0)
		else
			button:SetPoint("RIGHT", -4, 0)
		end
		row.actions[i] = button
		previous = button
	end
	row.detail:SetPoint("RIGHT", row.actions[1], "LEFT", -6, 0)

	row:SetScript("OnEnter", function(self)
		local data = self.signup
		if not data then
			return
		end
		GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
		GameTooltip:SetText(data.character)
		GameTooltip:AddLine(("%s%s"):format(E.ROLE_LABELS[data.role] or "Rôle inconnu", data.spec and (" · " .. data.spec) or ""), 1, 1, 1)
		GameTooltip:AddLine(E.RESPONSE_LABELS[data.response] or "?", unpack(RESPONSE_COLORS[data.response] or COLOR.muted))
		GameTooltip:AddLine("Place : " .. (E.PLACEMENT_LABELS[data.placement] or "?"), unpack(PLACEMENT_COLORS[data.placement] or COLOR.text))
		if data.note then
			GameTooltip:AddLine("« " .. data.note .. " »", 0.9, 0.9, 0.9, true)
		end
		UI.AddOnlineLine(data.character)
		if data.pendingSync then
			GameTooltip:AddLine("Pas encore sur le serveur", unpack(COLOR.muted))
		end
		GameTooltip:Show()
	end)
	row:SetScript("OnLeave", GameTooltip_Hide)
end

local PLACE_BUTTONS = {
	{ key = "confirmed", label = "Confirmer" },
	{ key = "standby", label = "Remplaçant" },
	{ key = "bench", label = "Banc" },
	{ key = "pending", label = "Retirer" },
}

local function UpdateRosterRow(row, data)
	row.stripe:SetShown(false)
	if data.header then
		row.signup = nil
		row.header:SetText(data.header)
		row.header:Show()
		for _, element in ipairs({ row.role, row.online, row.name, row.detail }) do
			element:Hide()
		end
		for _, button in ipairs(row.actions) do
			button:Hide()
		end
		return
	end
	local signup = data.signup
	row.signup = signup
	row.header:Hide()
	row.stripe:SetShown(data.index % 2 == 0)
	RoleIcon(row.role, signup.role)
	row.role:Show()
	UI.SetOnlineIcon(row.online, signup.character)
	row.name:SetText(ClassName(signup))
	row.name:Show()
	local detail = {}
	if signup.spec then
		detail[#detail + 1] = signup.spec
	end
	if signup.placement ~= "pending" and signup.response ~= "yes" then
		detail[#detail + 1] = Paint(RESPONSE_COLORS[signup.response] or COLOR.muted, (E.RESPONSE_LABELS[signup.response] or "?"):lower())
	end
	if signup.note then
		detail[#detail + 1] = "« " .. signup.note .. " »"
	end
	row.detail:SetText(table.concat(detail, " · "))
	row.detail:Show()

	-- Boutons de placement : les trois places autres que l'actuelle.
	local shown = 0
	for _, def in ipairs(PLACE_BUTTONS) do
		if data.canManage and def.key ~= signup.placement and shown < 3 then
			shown = shown + 1
			local button = row.actions[shown]
			button:SetText(def.label)
			button:SetScript("OnClick", function()
				E.Place(data.event, signup.character, def.key)
			end)
			button:Show()
		end
	end
	for i = shown + 1, 3 do
		row.actions[i]:Hide()
	end
end

local function RefreshDetail(event)
	local detail = panel.detail
	detail:SetShown(event ~= nil)
	panel.noSelection:SetShown(event == nil)
	if not event then
		return
	end
	local canManage = E.CanManage(event)

	detail.title:SetText(event.title)
	detail.title:SetTextColor(unpack(event.cancelled and COLOR.muted or COLOR.gold))
	local sub = ("%s · %s · organisé par %s"):format(E.KIND_LABELS[event.kind] or "?", LongDate(event.startsAt), event.organizer or "?")
	if event.cancelled then
		sub = Paint(COLOR.negative, "ANNULÉ") .. " · " .. sub
	end
	if event.pendingSync or event.localOnly then
		sub = sub .. Paint(COLOR.muted, " · pas encore sur le serveur")
	end
	detail.sub:SetText(sub)
	detail.description:SetText(event.description or "")

	detail.edit:SetShown(canManage)
	detail.cancel:SetShown(canManage)
	detail.cancel:SetText(event.cancelled and "Rétablir" or "Annuler l'év.")

	-- Ta réponse
	local mine = event.mine
	local role = view.role or (mine and mine.role) or (E.DetectRole())
	view.role = role
	for _, button in ipairs(detail.roleButtons) do
		button:GPSetSelected(button.role == role)
	end
	if not detail.spec:HasFocus() then
		local _, detected = E.DetectRole()
		detail.spec:SetText((mine and mine.spec) or detected or "")
	end
	if not detail.note:HasFocus() then
		detail.note:SetText(mine and mine.note or "")
	end
	local open = not event.cancelled
	for _, button in ipairs(detail.responseButtons) do
		button:GPSetSelected(mine ~= nil and mine.response == button.response, RESPONSE_COLORS[button.response])
		button:SetEnabled(open)
	end
	local status
	if not open then
		status = Paint(COLOR.muted, "Événement annulé : plus de réponse possible.")
	elseif mine and mine.response ~= "none" then
		status = ("Ta place : %s%s"):format(
			Paint(PLACEMENT_COLORS[mine.placement] or COLOR.text, E.PLACEMENT_LABELS[mine.placement] or "?"),
			mine.pendingSync and Paint(COLOR.muted, " · réponse en cours d'envoi") or "")
	else
		status = Paint(COLOR.gold, "Choisis ton rôle puis ta réponse.")
	end
	detail.status:SetText(status)

	-- Composition
	local counts = E.Counts(event)
	for _, cell in ipairs(detail.compCells) do
		local c = counts[cell.role]
		local confirmedText = c.target > 0 and ("%d/%d"):format(c.confirmed, c.target) or tostring(c.confirmed)
		local color = COLOR.text
		if c.target > 0 then
			color = c.confirmed >= c.target and COLOR.positive or COLOR.gold
		end
		cell.text:SetText(("%s %s"):format(Paint(color, confirmedText), Paint(COLOR.muted, ("(%d prés.)"):format(c.yes))))
	end
	detail.compExtra:SetText(Paint(COLOR.muted, ("%d peut-être · %d remplaçant%s · %d au banc"):format(
		counts.tentative, counts.standby, counts.standby > 1 and "s" or "", counts.bench)))

	-- Roster groupé par section
	local items, bySection = {}, {}
	for _, row in ipairs(event.signups) do
		local section = SectionOf(row)
		bySection[section] = bySection[section] or {}
		table.insert(bySection[section], row)
	end
	for _, section in ipairs(SECTIONS) do
		local rows = bySection[section.key]
		if rows then
			table.sort(rows, function(a, b)
				if a.role ~= b.role then
					return (a.role or "") > (b.role or "") -- tank, ranged, melee, healer : groupés
				end
				return a.character < b.character
			end)
			items[#items + 1] = { header = ("%s (%d)"):format(section.label, #rows) }
			for _, row in ipairs(rows) do
				items[#items + 1] = { signup = row, event = event, canManage = canManage, index = #items + 1 }
			end
		end
	end
	detail.roster:GPSetItems(items)
	-- Titres des sections : une extension en panne est retirée sur-le-champ.
	for _, button in ipairs(detail.sectionButtons) do
		local section = button.def
		local owner = section and SectionOwner(section.key)
		if owner and section.title then
			local ok, title = CallExtension(owner, section.title, event)
			if ok and title ~= nil then
				button.text:SetText(title)
				button:SetWidth(button.text:GetStringWidth() + 20)
			end
		end
	end
	-- Sections encore actives (une extension retirée en cours de partie emporte les siennes).
	local alive = { roster = true }
	for _, section in ipairs(detail.sections) do
		if SectionOwner(section.key) then
			alive[section.key] = true
		end
	end
	if not alive[view.section] then
		view.section = "roster"
	end
	local live = 0
	for _, section in ipairs(detail.sections) do
		if alive[section.key] then
			live = live + 1
		end
	end
	local onRoster = view.section == "roster"
	detail.roster:SetShown(onRoster)
	detail.rosterEmpty:SetShown(onRoster and #items == 0)
	detail.invite:SetShown(canManage and not event.cancelled)
	detail.copy:SetShown(#items > 0)
	detail.remind:SetShown(canManage and not event.cancelled and E.IsUpcoming(event))
	local remindOn, remindMinutes = E.Reminders.Get()
	detail.reminder:SetChecked(remindOn)
	if not detail.reminderMinutes:HasFocus() then
		detail.reminderMinutes:SetText(tostring(remindMinutes))
	end
	for _, button in ipairs(detail.sectionButtons) do
		button:GPSetSelected(button.section == view.section)
		button:SetShown(live > 0 and alive[button.section] == true)
	end
	for _, section in ipairs(detail.sections) do
		local shown = view.section == section.key and alive[section.key] == true
		section.frame:SetShown(shown)
		if shown then
			-- Une section en erreur ne doit pas empêcher d'afficher l'événement.
			local owner = SectionOwner(section.key)
			CallExtension(owner, section.refresh, section.frame, event, canManage)
		end
	end
end

-- Construction de l'onglet --------------------------------------------------------------------

local function BuildLeft(parent)
	local left = CreateFrame("Frame", nil, parent)
	left:SetPoint("TOPLEFT", 8, -6)
	left:SetPoint("BOTTOMLEFT", 8, 4)
	left:SetWidth(318)

	panel.modeButtons = {}
	local previous
	for _, def in ipairs({ { "list", "Liste" }, { "calendar", "Calendrier" } }) do
		local button = Toggle(left, def[2], function(self)
			view.mode = self.mode
			GP.RefreshUI()
		end)
		button.mode = def[1]
		button:SetPoint("TOPLEFT", previous or left, previous and "TOPRIGHT" or "TOPLEFT", previous and 4 or 0, 0)
		panel.modeButtons[#panel.modeButtons + 1] = button
		previous = button
	end

	local newButton = Button(left, "Nouveau", 86, function() OpenEditor(nil) end)
	panel.newButton = newButton
	newButton:SetPoint("TOPRIGHT", 0, 0)

	panel.showPast = CreateFrame("CheckButton", nil, left, "UICheckButtonTemplate")
	panel.showPast:SetSize(22, 22)
	panel.showPast:SetPoint("RIGHT", newButton, "LEFT", -60, 0)
	UI.SetCheckLabel(panel.showPast, "Passés")
	panel.showPast:SetScript("OnClick", function(self)
		view.showPast = self:GetChecked() and true or false
		GP.RefreshUI()
	end)

	-- Mode liste
	panel.list = UI.CreateScrollList(left, 42, SetupEventRow, UpdateEventRow)
	panel.list:SetPoint("TOPLEFT", 0, -32)
	panel.list:SetPoint("BOTTOMRIGHT", -18, 0)
	panel.listEmpty = Label(left, "Aucun événement à venir.\n« Nouveau » pour en créer un.", "GameFontHighlight", COLOR.muted)
	panel.listEmpty:SetPoint("CENTER", panel.list, "CENTER")
	panel.listEmpty:SetJustifyH("CENTER")

	-- Mode calendrier
	local calendar = CreateFrame("Frame", nil, left)
	calendar:SetPoint("TOPLEFT", 0, -32)
	calendar:SetPoint("BOTTOMRIGHT", 0, 0)
	panel.calendar = calendar

	local prev = CreateFrame("Button", nil, calendar)
	prev:SetSize(24, 24)
	prev:SetPoint("TOPLEFT", 0, 0)
	prev:SetNormalTexture("Interface\\Buttons\\UI-SpellbookIcon-PrevPage-Up")
	prev:SetPushedTexture("Interface\\Buttons\\UI-SpellbookIcon-PrevPage-Down")
	local nextButton = CreateFrame("Button", nil, calendar)
	nextButton:SetSize(24, 24)
	nextButton:SetPoint("TOPRIGHT", 0, 0)
	nextButton:SetNormalTexture("Interface\\Buttons\\UI-SpellbookIcon-NextPage-Up")
	nextButton:SetPushedTexture("Interface\\Buttons\\UI-SpellbookIcon-NextPage-Down")
	local function Shift(delta)
		local m = view.month
		m.month = m.month + delta
		if m.month < 1 then
			m.month, m.year = 12, m.year - 1
		elseif m.month > 12 then
			m.month, m.year = 1, m.year + 1
		end
		PlaySound(SOUNDKIT.IG_ABILITY_PAGE_TURN)
		GP.RefreshUI()
	end
	prev:SetScript("OnClick", function() Shift(-1) end)
	nextButton:SetScript("OnClick", function() Shift(1) end)
	panel.monthText = Label(calendar, nil, "GameFontNormal")
	panel.monthText:SetPoint("TOP", 0, -5)

	local cellWidth, cellHeight = 44, 34
	for i, name in ipairs({ "lun", "mar", "mer", "jeu", "ven", "sam", "dim" }) do
		local header = Label(calendar, name, "GameFontHighlightSmall", COLOR.muted)
		header:SetPoint("TOPLEFT", (i - 1) * (cellWidth + 1) + 12, -30)
	end
	panel.cells = {}
	for i = 1, 42 do
		local cell = CreateFrame("Button", nil, calendar)
		cell:SetSize(cellWidth, cellHeight)
		local col, rowIndex = (i - 1) % 7, math.floor((i - 1) / 7)
		cell:SetPoint("TOPLEFT", col * (cellWidth + 1), -46 - rowIndex * (cellHeight + 1))
		SetupDayCell(cell)
		panel.cells[i] = cell
	end

	panel.dayList = UI.CreateScrollList(calendar, 42, SetupEventRow, UpdateEventRow)
	panel.dayList:SetPoint("TOPLEFT", 0, -46 - 6 * (cellHeight + 1) - 8)
	panel.dayList:SetPoint("BOTTOMRIGHT", -18, 0)
	panel.dayEmpty = Label(calendar, "Rien ce jour-là.", "GameFontHighlightSmall", COLOR.muted)
	panel.dayEmpty:SetPoint("CENTER", panel.dayList, "CENTER")

	return left
end

local function BuildDetail(parent, left)
	local detail = CreateFrame("Frame", nil, parent)
	detail:SetPoint("TOPLEFT", left, "TOPRIGHT", 24, 0)
	detail:SetPoint("BOTTOMRIGHT", -8, 4)
	panel.detail = detail

	panel.noSelection = Label(parent, "Choisis un événement dans la liste,\nou crées-en un avec « Nouveau ».", "GameFontHighlight", COLOR.muted)
	panel.noSelection:SetPoint("CENTER", detail, "CENTER")
	panel.noSelection:SetJustifyH("CENTER")

	detail.cancel = Button(detail, "Annuler l'év.", 104, function()
		local event = E.Find(view.selected)
		if not event then
			return
		end
		if event.cancelled then
			E.SetCancelled(event, false)
		else
			StaticPopup_Show("GUILDKEEP_CANCEL_EVENT", event.title, nil, event)
		end
	end)
	detail.cancel:SetPoint("TOPRIGHT", 0, 0)
	detail.edit = Button(detail, "Modifier", 86, function()
		OpenEditor(E.Find(view.selected))
	end)
	detail.edit:SetPoint("RIGHT", detail.cancel, "LEFT", -4, 0)

	detail.title = Label(detail, nil, "GameFontNormalLarge")
	detail.title:SetPoint("TOPLEFT", 0, -2)
	detail.title:SetPoint("RIGHT", detail.edit, "LEFT", -8, 0)
	detail.title:SetWordWrap(false)
	detail.sub = Label(detail, nil, "GameFontHighlightSmall", COLOR.muted)
	detail.sub:SetPoint("TOPLEFT", detail.title, "BOTTOMLEFT", 0, -6)
	detail.sub:SetPoint("RIGHT", 0, 0)
	detail.sub:SetWordWrap(false)
	detail.description = Label(detail, nil, "GameFontHighlightSmall")
	detail.description:SetPoint("TOPLEFT", detail.sub, "BOTTOMLEFT", 0, -4)
	detail.description:SetPoint("RIGHT", 0, 0)
	detail.description:SetHeight(26)
	detail.description:SetJustifyV("TOP")

	-- Ta réponse
	local answer = CreateFrame("Frame", nil, detail)
	answer:SetPoint("TOPLEFT", 0, -68)
	answer:SetPoint("RIGHT", 0, 0)
	answer:SetHeight(64)
	local bg = answer:CreateTexture(nil, "BACKGROUND")
	bg:SetAllPoints()
	bg:SetColorTexture(unpack(COLOR.panel))

	local answerLabel = Label(answer, "Ta réponse", "GameFontNormal")
	answerLabel:SetPoint("TOPLEFT", 8, -8)
	detail.roleButtons = {}
	local previous
	for _, role in ipairs(E.ROLES) do
		local button = RoleButton(answer, role, function(chosen)
			view.role = chosen
			GP.RefreshUI()
		end)
		button.role = role
		button:SetPoint("TOPLEFT", previous or answerLabel, previous and "TOPRIGHT" or "BOTTOMLEFT", previous and 4 or 0, previous and 0 or -6)
		detail.roleButtons[#detail.roleButtons + 1] = button
		previous = button
	end
	local specLabel = Label(answer, "Spé.", "GameFontHighlightSmall", COLOR.muted)
	specLabel:SetPoint("LEFT", previous, "RIGHT", 12, 0)
	detail.spec = InputBox(answer, 96, 40)
	detail.spec:SetPoint("LEFT", specLabel, "RIGHT", 8, 0)
	local noteLabel = Label(answer, "Note", "GameFontHighlightSmall", COLOR.muted)
	noteLabel:SetPoint("LEFT", detail.spec, "RIGHT", 10, 0)
	detail.note = InputBox(answer, 150, 120)
	detail.note:SetPoint("LEFT", noteLabel, "RIGHT", 8, 0)

	detail.responseButtons = {}
	local nextAnchor
	for i = 3, 1, -1 do
		local response = ({ "yes", "tentative", "no" })[i]
		local button = Toggle(answer, E.RESPONSE_LABELS[response], function(self)
			local event = E.Find(view.selected)
			if event and not event.cancelled then
				detail.spec:ClearFocus()
				detail.note:ClearFocus()
				E.Respond(event, self.response, view.role, detail.spec:GetText(), detail.note:GetText())
			end
		end)
		button.response = response
		button:SetPoint("TOPRIGHT", nextAnchor or answer, nextAnchor and "TOPLEFT" or "TOPRIGHT", nextAnchor and -2 or -6, nextAnchor and 0 or -4)
		detail.responseButtons[i] = button
		nextAnchor = button
	end
	detail.status = Label(answer, nil, "GameFontHighlightSmall")
	detail.status:SetPoint("LEFT", answerLabel, "RIGHT", 12, 0)
	detail.status:SetPoint("RIGHT", nextAnchor, "LEFT", -8, 0)
	detail.status:SetWordWrap(false)

	-- Composition
	local comp = CreateFrame("Frame", nil, detail)
	comp:SetPoint("TOPLEFT", answer, "BOTTOMLEFT", 0, -8)
	comp:SetPoint("RIGHT", 0, 0)
	comp:SetHeight(22)
	detail.compCells = {}
	previous = nil
	for _, role in ipairs(E.ROLES) do
		local cell = CreateFrame("Frame", nil, comp)
		cell:SetSize(118, 22)
		cell:SetPoint("LEFT", previous or comp, previous and "RIGHT" or "LEFT", previous and 4 or 0, 0)
		cell.role = role
		cell.icon = cell:CreateTexture(nil, "ARTWORK")
		cell.icon:SetSize(18, 18)
		cell.icon:SetPoint("LEFT")
		RoleIcon(cell.icon, role)
		cell.text = Label(cell, nil, "GameFontHighlight")
		cell.text:SetPoint("LEFT", cell.icon, "RIGHT", 4, 0)
		cell:EnableMouse(true)
		cell:SetScript("OnEnter", function(self)
			GameTooltip:SetOwner(self, "ANCHOR_TOP")
			GameTooltip:SetText(E.ROLE_LABELS[role])
			GameTooltip:AddLine("Confirmés / objectif, et nombre de présents inscrits.", 1, 1, 1, true)
			GameTooltip:Show()
		end)
		cell:SetScript("OnLeave", GameTooltip_Hide)
		detail.compCells[#detail.compCells + 1] = cell
		previous = cell
	end
	detail.compExtra = Label(comp, nil, "GameFontHighlightSmall")
	detail.compExtra:SetPoint("TOPLEFT", comp, "BOTTOMLEFT", 0, -2)

	-- Roster
	detail.invite = Button(detail, "Inviter les confirmés", 150, function()
		local event = E.Find(view.selected)
		if not event then
			return
		end
		local count, message = E.InviteConfirmed(event)
		print("|cff33ff99Guildkeep|r — " .. (message or ("%d invitation%s envoyée%s."):format(count, count > 1 and "s" or "", count > 1 and "s" or "")))
	end)
	detail.invite:SetPoint("BOTTOMRIGHT", 0, 0)
	detail.copy = Button(detail, "Copier le roster", 120, function()
		local event = E.Find(view.selected)
		if event then
			UI.OpenCopyDialog("Soirée — " .. event.title, "Ctrl+C pour copier, puis colle-le sur Discord.", ExportText(event))
		end
	end)
	detail.copy:SetPoint("RIGHT", detail.invite, "LEFT", -4, 0)

	-- Rappel aux inscrits dans le canal de guilde (organisateur ou officier).
	detail.remind = Button(detail, "Rappeler", 90, function()
		local event = E.Find(view.selected)
		if event then
			local ok, err = E.Reminders.RemindGuild(event)
			if not ok then
				GP.Chat(err)
			end
		end
	end)
	detail.remind:SetPoint("RIGHT", detail.copy, "LEFT", -4, 0)

	-- Rappel personnel : case et nombre de minutes, réglage par compte.
	detail.reminder = CreateFrame("CheckButton", nil, detail, "UICheckButtonTemplate")
	detail.reminder:SetSize(22, 22)
	detail.reminder:SetPoint("BOTTOMLEFT", 0, 0)
	UI.SetCheckLabel(detail.reminder, "Me rappeler")
	detail.reminderMinutes = UI.InputBox(detail, 30, 4, true)
	detail.reminderMinutes:SetPoint("LEFT", detail.reminder, "RIGHT", 82, 0)
	detail.reminderUnit = Label(detail, "min avant", "GameFontHighlightSmall", COLOR.muted)
	detail.reminderUnit:SetPoint("LEFT", detail.reminderMinutes, "RIGHT", 6, 0)
	local function SaveReminder()
		E.Reminders.Set(detail.reminder:GetChecked(), tonumber(detail.reminderMinutes:GetText()))
	end
	detail.reminder:SetScript("OnClick", SaveReminder)
	detail.reminderMinutes:SetScript("OnEditFocusLost", SaveReminder)

	-- Bas du détail : le roster, puis les sections des extensions (réservations de
	-- butin, préparation de la soirée…). Sans extension, pas de barre de sections.
	local sections = ExtensionSections()
	detail.sectionButtons = {}
	local previousSection
	table.insert(sections, 1, { key = "roster", label = "Roster" })
	for _, def in ipairs(sections) do
		local button = Toggle(detail, def.label, function(self)
			view.section = self.section
			GP.RefreshUI()
		end)
		button.section = def.key
		button.def = def
		button:SetShown(#sections > 1)
		button:SetPoint("TOPLEFT", previousSection or detail.compExtra, previousSection and "TOPRIGHT" or "BOTTOMLEFT", previousSection and 4 or 0, previousSection and 0 or -4)
		detail.sectionButtons[#detail.sectionButtons + 1] = button
		previousSection = button
	end
	detail.sectionTop = detail.sectionButtons[1]

	detail.roster = UI.CreateScrollList(detail, 24, SetupRosterRow, UpdateRosterRow)
	detail.roster:SetPoint("TOPLEFT", detail.sectionTop, "BOTTOMLEFT", 0, -6)
	detail.roster:SetPoint("BOTTOMRIGHT", -18, 28)
	detail.rosterEmpty = Label(detail, "Personne n'a encore répondu.", "GameFontHighlight", COLOR.muted)
	detail.rosterEmpty:SetPoint("CENTER", detail.roster, "CENTER")
	if #extensions > 0 then
		detail.copy:SetText("Copier la soirée")
	end
	detail.sections = {}
	for i = 2, #sections do
		local def = sections[i]
		local ok, frame = GP.Modules.Protect(def.build, detail)
		if ok and frame then
			frame:Hide()
			detail.sections[#detail.sections + 1] = { key = def.key, refresh = def.refresh, frame = frame }
		end
	end
	-- Section impossible à construire : son bouton disparaît (et la barre, s'il ne reste que le roster).
	local built = { roster = true }
	for _, section in ipairs(detail.sections) do
		built[section.key] = true
	end
	for _, button in ipairs(detail.sectionButtons) do
		button:SetShown(#detail.sections > 0 and built[button.section] == true)
	end
end

local function BuildEventsTab(_, parent)
	panel = parent
	local left = BuildLeft(parent)
	BuildDetail(parent, left)

	local divider = parent:CreateTexture(nil, "ARTWORK")
	divider:SetColorTexture(1, 0.82, 0, 0.18)
	divider:SetWidth(1)
	divider:SetPoint("TOPLEFT", left, "TOPRIGHT", 12, 0)
	divider:SetPoint("BOTTOMLEFT", left, "BOTTOMRIGHT", 12, 0)
end

local function RefreshEvents()
	local all = E.GetEvents()
	local visible = {}
	for _, event in ipairs(all) do
		if view.showPast or E.IsUpcoming(event) or event.key == view.selected then
			visible[#visible + 1] = event
		end
	end

	for _, button in ipairs(panel.modeButtons) do
		button:GPSetSelected(button.mode == view.mode)
	end
	panel.showPast:SetChecked(view.showPast)

	local calendarMode = view.mode == "calendar"
	panel.list:SetShown(not calendarMode)
	panel.calendar:SetShown(calendarMode)
	panel.showPast:SetShown(not calendarMode)

	-- Sélection par défaut : le prochain événement.
	local selected
	for _, event in ipairs(all) do
		if event.key == view.selected or event.clientId == view.selected then
			selected = event
		end
	end
	if not selected then
		for _, event in ipairs(all) do
			if E.IsUpcoming(event) then
				selected = event
				break
			end
		end
	end
	view.selected = selected and selected.key

	if calendarMode then
		view.day = view.day or (selected and DayKey(selected.startsAt)) or DayKey(time())
		RefreshCalendar(all)
		local items = {}
		for _, event in ipairs(all) do
			if DayKey(event.startsAt) == view.day then
				items[#items + 1] = { event = event, index = #items + 1 }
			end
		end
		panel.dayList:GPSetItems(items)
		panel.dayEmpty:SetShown(#items == 0)
	else
		local items = {}
		for _, event in ipairs(visible) do
			items[#items + 1] = { event = event, index = #items + 1 }
		end
		panel.list:GPSetItems(items)
		panel.listEmpty:SetShown(#items == 0)
	end

	RefreshDetail(selected)
end

-- Carte du tableau de bord : les trois prochains événements et ta réponse.
local function HomeCard(cards)
	local lines = {}
	for _, event in ipairs(E.GetEvents()) do
		if #lines < 3 and E.IsUpcoming(event) then
			local response = event.mine and event.mine.response or "none"
			local answer = response == "none" and Paint(COLOR.gold, "à répondre") or E.RESPONSE_LABELS[response]
			lines[#lines + 1] = ("%s · %s · %s"):format(date("%d/%m %H:%M", event.startsAt or 0), event.title or "?", answer)
		end
	end
	if #lines == 0 then
		lines[1] = "Aucun événement à venir."
	end
	cards[#cards + 1] = { id = "events", order = 10, title = "Prochains événements", lines = lines, tab = "tabEvents" }
end

GP.Modules.Define("events", "ui", function(ctx)
	ctx:On("HOME_CARDS", HomeCard)
	DefinePopups()
	ctx:Slash({ "events", "event", "evenements" }, function()
		GP.ShowTab("tabEvents")
	end)
	local function Detail(name, index)
		return function()
			local detail = panel and panel.detail
			local element = detail and detail[name]
			return index and element and element[index] or element
		end
	end
	ctx:Tutorial("events", {
		tab = "tabEvents",
		order = 10,
		title = "Événements",
		steps = {
			{ target = function() return panel and panel.modeButtons and panel.modeButtons[1] end, title = "Liste ou calendrier",
				text = "Les événements à venir en liste, ou sur un calendrier du mois : un point marque les jours avec un événement, clique sur un jour pour voir les siens." },
			{ target = function() return panel and panel.showPast end, title = "Passés", text = "Coche pour revoir aussi les événements terminés." },
			{ target = function() return panel and panel.newButton end, title = "Nouveau",
				text = "Crée un événement : titre, type (raid, donjon, JcJ, autre), date et heure (ta propre heure), composition visée par rôle et détails. Tu en deviens l'organisateur." },
			{ target = Detail("roleButtons", 1), title = "Ton rôle",
				text = "Tank, soigneur, DPS mêlée ou distance : choisis le rôle que tu joueras. Ta spécialisation et une note sont facultatives.",
				hint = "Visible quand un événement est sélectionné." },
			{ target = Detail("responseButtons", 1), title = "Ta réponse",
				text = "Présent, Peut-être ou Absent : un clic enregistre ta réponse avec ton rôle. Répondre Absent te retire du roster." },
			{ target = Detail("compCells", 1), title = "Composition",
				text = "Pour chaque rôle : confirmés / objectif, et le nombre de présents inscrits. Vert quand l'objectif est atteint." },
			{ target = Detail("roster"), title = "Le roster",
				text = "Les inscrits rangés par place. L'organisateur et les officiers les placent avec Confirmer, Remplaçant, Banc ou Retirer." },
			{ target = Detail("invite"), title = "Inviter les confirmés",
				text = "Invite d'un clic les confirmés en ligne et passe le groupe en raid au besoin (chef ou assistant du groupe, hors combat).",
				hint = "Réservé à l'organisateur et aux officiers." },
			{ target = Detail("copy"), title = "Copier", text = "Le roster (et la soirée de raid) en texte, à coller sur Discord." },
			{ target = Detail("remind"), title = "Rappeler",
				text = "Poste un rappel avec l'heure et les inscrits dans le canal de guilde.", hint = "Réservé à l'organisateur et aux officiers, avant l'événement." },
			{ target = Detail("reminder"), title = "Me rappeler",
				text = "Une alerte quelques minutes avant chaque événement où tu as répondu Présent ou Peut-être. Règle le nombre de minutes à côté, ou /gkeep rappel 30 (off pour couper)." },
			{ target = Detail("edit"), title = "Modifier et annuler",
				text = "L'organisateur et les officiers modifient l'événement ou l'annulent (Rétablir le remet).", hint = "Réservé à l'organisateur et aux officiers." },
		},
	})
	ctx:AddTab({
		label = "Événements",
		key = "tabEvents",
		order = 40,
		build = BuildEventsTab,
		refresh = RefreshEvents,
		badge = E.CountAwaitingAnswer,
		badgeHint = function(count)
			return ("%d événement%s sans ta réponse"):format(count, count > 1 and "s" or "")
		end,
	})
end)
