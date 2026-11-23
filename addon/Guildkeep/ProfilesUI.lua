local _, GP = ...

-- Module Profils et accueil, partie interface : onglet « Profils ». Trois
-- sections, chacune en liste à gauche et détail à droite :
--   - Profils : le profil de chaque joueur (rerolls, rôles, disponibilités,
--     talents) ; « Mon profil » pour remplir le sien ;
--   - Accueil : sa liste d'accueil (et, pour les officiers, celle de chaque
--     nouveau, avec un rappel à copier) ;
--   - Journal : arrivées et départs relevés dans le roster ; versions de
--     l'addon pour les officiers.

local P = GP.Profiles
local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label

local DONE_ICON = "|TInterface\\RaidFrame\\ReadyCheck-Ready:14|t "
local TODO_ICON = "|TInterface\\RaidFrame\\ReadyCheck-Waiting:14|t "

local view = {
	section = "profiles", -- profiles | welcome | journal
	selected = { profiles = nil, welcome = nil, journal = nil },
}
local panel

local SECTIONS = {
	{ key = "profiles", label = "Profils" },
	{ key = "welcome", label = "Accueil" },
	{ key = "journal", label = "Journal" },
}

local function Report(ok, err)
	if not ok and err then
		GP.Chat(err)
	end
	return ok
end

local function Roles(roles)
	local labels = {}
	for _, role in ipairs(roles or {}) do
		labels[#labels + 1] = P.ROLE_LABELS[role] or role
	end
	return #labels > 0 and table.concat(labels, ", ") or nil
end

-- Éditeur de son profil ----------------------------------------------------------------------------

local editor

local function Contains(list, value)
	for _, v in ipairs(list or {}) do
		if v == value then
			return true
		end
	end
	return false
end

local function RefreshEditor()
	local dialog = editor
	for _, button in ipairs(dialog.mainButtons) do
		button:GPSetSelected(button.character == dialog.main)
	end
	for _, button in ipairs(dialog.altButtons) do
		local isMain = button.character == dialog.main
		button:SetEnabled(not isMain)
		button:GPSetSelected(not isMain and Contains(dialog.alts, button.character))
	end
	for _, button in ipairs(dialog.roleButtons) do
		button:GPSetSelected(Contains(dialog.roles, button.role))
	end
end

local function Toggled(list, value)
	local copy = {}
	local found = false
	for _, v in ipairs(list) do
		if v == value then
			found = true
		else
			copy[#copy + 1] = v
		end
	end
	if not found then
		copy[#copy + 1] = value
	end
	return copy
end

local function ButtonRow(parent, label, y)
	local text = Label(parent, label, "GameFontNormal")
	text:SetPoint("TOPLEFT", 12, y)
	text:SetWidth(100)
	local row = CreateFrame("Frame", nil, parent)
	row:SetPoint("TOPLEFT", text, "TOPRIGHT", 8, 4)
	row:SetSize(380, 22)
	return row
end

local function CreateEditor()
	local dialog = UI.CreateWindow("GuildkeepProfileEditor", 520, 360, "DIALOG")
	UI.SetWindowPortrait(dialog, GP.ICON)
	UI.SetWindowTitle(dialog, "Mon profil")
	local inset = dialog.Inset
	dialog.mainRow = ButtonRow(inset, "Principal", -16)
	dialog.altRow = ButtonRow(inset, "Rerolls", -50)
	dialog.roleRow = ButtonRow(inset, "Rôles", -84)
	dialog.mainButtons, dialog.altButtons, dialog.roleButtons = {}, {}, {}

	local previous
	for _, role in ipairs(P.ROLES) do
		local button = UI.Toggle(dialog.roleRow, P.ROLE_LABELS[role], function(self)
			dialog.roles = Toggled(dialog.roles, self.role)
			RefreshEditor()
		end)
		button.role = role
		button:SetPoint("LEFT", previous or dialog.roleRow, previous and "RIGHT" or "LEFT", previous and 4 or 0, 0)
		dialog.roleButtons[#dialog.roleButtons + 1] = button
		previous = button
	end

	local availability = Label(inset, "Disponibilités", "GameFontNormal")
	availability:SetPoint("TOPLEFT", 12, -122)
	availability:SetWidth(100)
	dialog.availability = UI.InputBox(inset, 360, P.AVAILABILITY_MAX)
	dialog.availability:SetPoint("LEFT", availability, "RIGHT", 8, 0)
	local note = Label(inset, "Note", "GameFontNormal")
	note:SetPoint("TOPLEFT", 12, -156)
	note:SetWidth(100)
	dialog.note = UI.InputBox(inset, 360, P.NOTE_MAX)
	dialog.note:SetPoint("LEFT", note, "RIGHT", 8, 0)

	dialog.hint = Label(inset, "Ton principal et tes rerolls sont tes personnages de cette guilde qui ont lancé l'addon. "
		.. "Le profil part vers les joueurs connectés et, par l'app compagnon, sur Discord (/gkeep profil voir).",
		"GameFontHighlightSmall", COLOR.muted)
	dialog.hint:SetPoint("TOPLEFT", 12, -192)
	dialog.hint:SetPoint("RIGHT", -12, 0)
	dialog.hint:SetWordWrap(true)

	dialog.save = UI.Button(dialog, "Enregistrer", 120, function()
		if Report(P.SaveProfile({
			main = dialog.main, alts = dialog.alts, roles = dialog.roles,
			availability = dialog.availability:GetText(), note = dialog.note:GetText(),
		})) then
			dialog:Hide()
		end
	end)
	dialog.save:SetPoint("BOTTOMRIGHT", -6, 4)
	local cancel = UI.Button(dialog, CANCEL or "Annuler", 96, function() dialog:Hide() end)
	cancel:SetPoint("RIGHT", dialog.save, "LEFT", -4, 0)
	return dialog
end

local function CharacterButtons(row, buttons, characters, onClick)
	for _, button in ipairs(buttons) do
		button:Hide()
	end
	local previous
	for i, name in ipairs(characters) do
		if i > 6 then
			break
		end
		local button = buttons[i]
		if not button then
			button = UI.Toggle(row, name, function(self) onClick(self.character) end)
			buttons[i] = button
		end
		button.text:SetText(name)
		button:SetWidth(button.text:GetStringWidth() + 20)
		button.character = name
		button:ClearAllPoints()
		button:SetPoint("LEFT", previous or row, previous and "RIGHT" or "LEFT", previous and 4 or 0, 0)
		button:Show()
		previous = button
	end
end

local function OpenEditor()
	editor = editor or CreateEditor()
	local dialog = editor
	local mine = P.MyCharacters()
	local current = P.ProfileOf(GP.GetPlayerFullName())
	dialog.main = current and Contains(mine, current.main) and current.main or mine[1]
	-- Rerolls : ceux du profil connu, sinon tous les autres personnages de ce compte dans la guilde.
	dialog.alts = {}
	for _, name in ipairs(mine) do
		if name ~= dialog.main and (not current or Contains(current.alts, name) or current.main == name) then
			dialog.alts[#dialog.alts + 1] = name
		end
	end
	dialog.roles = current and { unpack(current.roles) } or {}
	dialog.availability:SetText(current and current.availability or "")
	dialog.note:SetText(current and current.note or "")
	CharacterButtons(dialog.mainRow, dialog.mainButtons, mine, function(name)
		-- L'ancien principal devient reroll ; le nouveau n'en est plus un.
		if dialog.main and not Contains(dialog.alts, dialog.main) then
			dialog.alts[#dialog.alts + 1] = dialog.main
		end
		dialog.main = name
		if Contains(dialog.alts, name) then
			dialog.alts = Toggled(dialog.alts, name)
		end
		RefreshEditor()
	end)
	CharacterButtons(dialog.altRow, dialog.altButtons, mine, function(name)
		dialog.alts = Toggled(dialog.alts, name)
		RefreshEditor()
	end)
	RefreshEditor()
	dialog:Show()
end

GP.ProfilesUI = { OpenEditor = OpenEditor }

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
		view.selected[view.section] = self.key
		PlaySound(SOUNDKIT.IG_MAINMENU_OPTION_CHECKBOX_ON)
		GP.RefreshUI()
	end)
end

local function UpdateRow(row, data)
	row.key = data.key
	row.stripe:SetShown(data.index % 2 == 0)
	row.selectedTex:SetShown(data.key == view.selected[view.section])
	row.title:SetText(data.title)
	row.sub:SetText(data.sub or "")
end

-- Construction -------------------------------------------------------------------------------------

local function BuildProfilesTab(_, parent)
	panel = parent
	local left = CreateFrame("Frame", nil, parent)
	left:SetPoint("TOPLEFT", 8, -6)
	left:SetPoint("BOTTOMLEFT", 8, 4)
	left:SetWidth(318)

	panel.sectionButtons = {}
	local previous
	for _, def in ipairs(SECTIONS) do
		local button = UI.Toggle(left, def.label, function(self)
			view.section = self.profilesSection
			GP.RefreshUI()
		end)
		button.profilesSection = def.key
		button:SetPoint("TOPLEFT", previous or left, previous and "TOPRIGHT" or "TOPLEFT", previous and 2 or 0, 0)
		panel.sectionButtons[#panel.sectionButtons + 1] = button
		previous = button
	end
	panel.myProfile = UI.Button(left, "Mon profil", 96, OpenEditor)
	panel.myProfile:SetPoint("TOPRIGHT", 0, 0)

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

	panel.title = Label(detail, nil, "GameFontNormalLarge")
	panel.title:SetPoint("TOPLEFT", 0, -2)
	panel.title:SetPoint("RIGHT", 0, 0)
	panel.title:SetWordWrap(false)
	panel.sub = Label(detail, nil, "GameFontHighlightSmall", COLOR.muted)
	panel.sub:SetPoint("TOPLEFT", panel.title, "BOTTOMLEFT", 0, -6)
	panel.sub:SetPoint("RIGHT", 0, 0)
	panel.sub:SetWordWrap(false)

	panel.body = Label(detail, nil, "GameFontHighlight")
	panel.body:SetPoint("TOPLEFT", panel.sub, "BOTTOMLEFT", 0, -12)
	panel.body:SetPoint("RIGHT", 0, 0)
	panel.body:SetJustifyH("LEFT")
	panel.body:SetJustifyV("TOP")
	panel.body:SetWordWrap(true)

	-- Accueil : une bascule par étape.
	panel.steps = {}
	for i, step in ipairs(P.STEPS) do
		local button = UI.Toggle(detail, P.STEP_LABELS[step], function(self)
			local state = panel.currentSteps
			if state then
				Report(P.SetStep(state.character, self.step, not state.done[self.step]))
			end
		end)
		button.step = step
		button:SetPoint("TOPLEFT", panel.sub, "BOTTOMLEFT", 0, -12 - (i - 1) * 28)
		button:SetPoint("RIGHT", 0, 0)
		panel.steps[i] = button
	end
	panel.stepsHint = Label(detail, nil, "GameFontHighlightSmall", COLOR.muted)
	panel.stepsHint:SetPoint("TOPLEFT", panel.steps[#panel.steps], "BOTTOMLEFT", 0, -12)
	panel.stepsHint:SetPoint("RIGHT", 0, 0)
	panel.stepsHint:SetWordWrap(true)

	panel.reminder = UI.Button(detail, "Copier un rappel", 140, function()
		local text = P.ReminderText()
		if not text then
			GP.Chat("tous les nouveaux ont fini leur accueil")
			return
		end
		UI.OpenCopyDialog("Rappel d'accueil", "Ctrl+C, puis colle-le dans le canal de guilde ou sur Discord.", text)
	end)
	panel.reminder:SetPoint("BOTTOMRIGHT", 0, 8)

	panel.noSelection = Label(detail, nil, "GameFontHighlight", COLOR.muted)
	panel.noSelection:SetPoint("CENTER")
	panel.noSelection:SetJustifyH("CENTER")

	panel.tutorialTargets = {
		sections = panel.sectionButtons[1], myProfile = panel.myProfile, list = panel.list,
		steps = panel.steps[1], reminder = panel.reminder,
	}

	local divider = parent:CreateTexture(nil, "ARTWORK")
	divider:SetColorTexture(1, 0.82, 0, 0.18)
	divider:SetWidth(1)
	divider:SetPoint("TOPLEFT", left, "TOPRIGHT", 12, 0)
	divider:SetPoint("BOTTOMLEFT", left, "BOTTOMRIGHT", 12, 0)
end

-- Lignes de chaque section.
local function Items(data, states, officer)
	local items = {}
	if view.section == "profiles" then
		for i, p in ipairs(data.profiles) do
			local alts = #p.alts > 0 and (#p.alts == 1 and "1 reroll" or (#p.alts .. " rerolls")) or "pas de reroll"
			items[i] = { key = p.main, index = i, title = p.main .. (Roles(p.roles) and Paint(COLOR.muted, " · " .. Roles(p.roles)) or ""),
				sub = alts .. (p.availability and (" · " .. p.availability) or "") .. (p.pendingSync and " · pas encore sur le serveur" or ""),
				profile = p }
		end
	elseif view.section == "welcome" then
		local list = {}
		local me = GP.GetPlayerFullName()
		list[1] = P.StepsOf(me, states)
		if officer then
			for _, state in ipairs(P.Incomplete(states)) do
				if state.character ~= me then
					list[#list + 1] = state
				end
			end
		end
		for i, state in ipairs(list) do
			items[i] = { key = state.character, index = i, title = state.character .. (state.character == me and Paint(COLOR.muted, " (toi)") or ""),
				sub = ("%d/%d étapes"):format(state.count or 0, #P.STEPS), state = state }
		end
	else
		for i, r in ipairs(data.journal) do
			items[i] = { key = r.character .. "|" .. r.at, index = i,
				title = (r.kind == "join" and Paint(COLOR.positive, "+ ") or Paint(COLOR.muted, "- ")) .. r.character,
				sub = (r.kind == "join" and "a rejoint la guilde" or "a quitté la guilde") .. " · " .. date("%d/%m %H:%M", r.at)
					.. (r.pendingSync and " · pas encore sur le serveur" or ""), change = r }
		end
	end
	return items
end

local EMPTY = {
	profiles = "Aucun profil pour l'instant.\n« Mon profil » pour remplir le tien.",
	welcome = "Personne à accueillir.",
	journal = "Aucune arrivée ni départ relevé.\nLe journal se remplit quand le roster change.",
}

local function ProfileText(profile, data, officer)
	local lines = {}
	lines[#lines + 1] = Paint(COLOR.gold, "Rerolls : ") .. (#profile.alts > 0 and table.concat(profile.alts, ", ") or "aucun")
	lines[#lines + 1] = Paint(COLOR.gold, "Disponibilités : ") .. (profile.availability or "non précisées")
	if profile.note then
		lines[#lines + 1] = Paint(COLOR.gold, "Note : ") .. profile.note
	end
	local talents = {}
	for _, name in ipairs({ profile.main, unpack(profile.alts) }) do
		local t = data.talents[name]
		if t then
			talents[#talents + 1] = ("  %s : %s(%s)"):format(name, t.spec and (t.spec .. " ") or "", t.summary)
		end
	end
	if #talents > 0 then
		lines[#lines + 1] = Paint(COLOR.gold, "Talents :")
		for _, line in ipairs(talents) do
			lines[#lines + 1] = line
		end
	end
	if officer then
		local versions = {}
		for _, name in ipairs({ profile.main, unpack(profile.alts) }) do
			if data.versions[name] then
				versions[#versions + 1] = ("%s %s"):format(name, data.versions[name].version)
			end
		end
		if #versions > 0 then
			lines[#lines + 1] = Paint(COLOR.gold, "Version de l'addon : ") .. table.concat(versions, ", ")
		end
	end
	return table.concat(lines, "\n")
end

local function VersionsText(data)
	local byVersion, order = {}, {}
	for name, v in pairs(data.versions) do
		if not byVersion[v.version] then
			byVersion[v.version] = {}
			order[#order + 1] = v.version
		end
		table.insert(byVersion[v.version], name)
	end
	table.sort(order, function(a, b) return a > b end)
	local lines = { Paint(COLOR.gold, "Versions de l'addon") .. Paint(COLOR.muted, (" (la tienne : %s)"):format(GP.VERSION)) }
	for _, version in ipairs(order) do
		table.sort(byVersion[version])
		lines[#lines + 1] = ("%s : %s"):format(version, table.concat(byVersion[version], ", "))
	end
	if #order == 0 then
		lines[#lines + 1] = "Aucune version relevée."
	end
	return table.concat(lines, "\n")
end

local function RefreshProfiles()
	local data = P.Get()
	local states = P.Onboarding(data)
	local officer = GP.IsOfficer()
	for _, button in ipairs(panel.sectionButtons) do
		button:GPSetSelected(button.profilesSection == view.section)
	end

	local items = Items(data, states, officer)
	panel.list:GPSetItems(items)
	panel.empty:SetText(EMPTY[view.section])
	panel.empty:SetShown(#items == 0)

	local current
	for _, item in ipairs(items) do
		if item.key == view.selected[view.section] then
			current = item
		end
	end
	current = current or items[1]
	view.selected[view.section] = current and current.key

	local welcome = view.section == "welcome"
	local journal = view.section == "journal"
	panel.title:SetShown(current ~= nil or journal)
	panel.sub:SetShown(current ~= nil or journal)
	panel.body:SetShown(not welcome and (current ~= nil or journal))
	for _, button in ipairs(panel.steps) do
		button:SetShown(welcome and current ~= nil)
	end
	panel.stepsHint:SetShown(welcome and current ~= nil)
	panel.reminder:SetShown(welcome and officer)
	panel.noSelection:SetShown(current == nil and not journal)
	panel.noSelection:SetText(EMPTY[view.section])
	panel.currentSteps = nil

	if view.section == "profiles" and current then
		local p = current.profile
		panel.title:SetText(p.main)
		panel.sub:SetText((Roles(p.roles) or "rôles non précisés") .. " · mis à jour le " .. date("%d/%m/%Y", p.updatedAt))
		panel.body:SetText(ProfileText(p, data, officer))
	elseif welcome and current then
		local state = current.state
		panel.currentSteps = state
		panel.title:SetText("Accueil de " .. state.character)
		panel.sub:SetText(("%d étape%s sur %d"):format(state.count or 0, (state.count or 0) > 1 and "s" or "", #P.STEPS))
		local mine = state.character == GP.GetPlayerFullName()
		for _, button in ipairs(panel.steps) do
			local done = state.done[button.step]
			button.text:SetText((done and DONE_ICON or TODO_ICON) .. P.STEP_LABELS[button.step])
			button:GPSetSelected(done and true or false, COLOR.positive)
			button:SetEnabled(mine or officer)
		end
		panel.stepsHint:SetText("Clique une étape pour la cocher ou la décocher. « Discord », « principal » et « profil » se cochent "
			.. "aussi d'eux-mêmes (compte lié sur Discord, profil rempli).")
	elseif journal then
		panel.title:SetText("Arrivées et départs")
		panel.sub:SetText("relevés dans le roster par les membres qui ont l'addon")
		panel.body:SetText(officer and VersionsText(data)
			or "Chaque arrivée est suivie dans « Accueil » par les officiers pendant 60 jours.")
	end
end

-- Carte du tableau de bord : ton profil et ton accueil ; les nouveaux à accueillir pour les officiers.
local function HomeCard(cards)
	local data = P.Get()
	local states = P.Onboarding(data)
	local me = GP.GetPlayerFullName()
	local lines = {}
	if not P.ProfileOf(me, data) then
		lines[#lines + 1] = Paint(COLOR.gold, "Ton profil est vide : « Mon profil » pour le remplir.")
	end
	local mine = states[me]
	if mine and mine.count < #P.STEPS then
		lines[#lines + 1] = ("Ton accueil : %d/%d étapes"):format(mine.count, #P.STEPS)
	end
	local week = GP.Now() - 7 * 86400
	local joins = 0
	for _, change in ipairs(data.journal) do
		if change.kind == "join" and change.at >= week then
			joins = joins + 1
		end
	end
	if joins > 0 then
		lines[#lines + 1] = ("Arrivées cette semaine : %d"):format(joins)
	end
	if GP.IsOfficer() then
		local waiting = #P.Incomplete(states)
		if waiting > 0 then
			lines[#lines + 1] = ("Nouveaux à accueillir : %s"):format(Paint(COLOR.gold, tostring(waiting)))
		end
	end
	if #lines > 0 then
		cards[#cards + 1] = { id = "profiles", order = 50, title = "Profils et accueil", lines = lines, tab = "tabProfiles" }
	end
end

GP.Modules.Define("profiles", "ui", function(ctx)
	ctx:On("HOME_CARDS", HomeCard)
	local function Section(key)
		return function()
			view.section = key
			GP.RefreshUI()
		end
	end
	ctx:Tutorial("profiles", {
		tab = "tabProfiles",
		title = "Profils et accueil",
		steps = {
			{ prepare = Section("profiles"), target = function() return panel and panel.sectionButtons and panel.sectionButtons[1] end,
				title = "Profils, Accueil, Journal",
				text = "Trois sections : le profil de chaque joueur, la liste d'accueil des nouveaux et le journal des arrivées et départs." },
			{ prepare = Section("profiles"), target = function() return panel and panel.list end, title = "Les profils",
				text = "Un profil par joueur : son principal, ses rerolls, ses rôles et ses disponibilités. Clique pour voir le détail et les talents de chaque personnage." },
			{ prepare = Section("profiles"), target = function() return panel and panel.myProfile end, title = "Mon profil",
				text = "Choisis ton principal, coche tes rerolls (tes personnages de cette guilde), tes rôles, et indique quand tu joues. Tes talents sont relevés tout seuls." },
			{ prepare = Section("welcome"), target = function() return panel and panel.steps and panel.steps[1] end, title = "L'accueil",
				text = "Cinq étapes pour bien démarrer : Discord, principal, profil, charte et présentation. Clique une étape pour la cocher ; les officiers cochent celles des nouveaux." },
			{ prepare = Section("welcome"), target = function() return panel and panel.reminder end, title = "Copier un rappel",
				text = "Les officiers copient un message qui liste, pour chaque nouveau, les étapes qui restent, à coller dans le canal de guilde ou sur Discord.",
				hint = "Réservé aux officiers." },
			{ prepare = Section("journal"), target = function() return panel and panel.list end, title = "Le journal",
				text = "Les arrivées et départs relevés dans le roster par les membres qui ont l'addon. Les officiers y voient aussi la version de l'addon de chacun." },
		},
	})
	ctx:Slash({ "profils", "profil", "profiles", "profile", "accueil", "welcome" }, function(rest)
		if rest and rest:lower():match("^moi") then
			OpenEditor()
			return
		end
		GP.ShowTab("tabProfiles")
	end)
	ctx:AddTab({
		label = "Profils",
		key = "tabProfiles",
		order = 90,
		build = BuildProfilesTab,
		refresh = RefreshProfiles,
		badge = function()
			if not GP.IsOfficer() then
				return 0
			end
			return #P.Incomplete()
		end,
		badgeHint = function(n)
			return ("%d nouveau%s à accueillir"):format(n, n > 1 and "x" or "")
		end,
	})
end)
