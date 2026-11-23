local _, GP = ...

-- Module Communication (données) : annonces des officiers (avec modèles),
-- charte de la guilde et objectifs communs auxquels chacun contribue.
--
-- Trois sources, comme les autres modules : le serveur (section
-- « communication » de Data.lua), ce que les joueurs connectés ont annoncé
-- (GuildkeepDB.commPeer, jamais renvoyé au serveur) et les changements faits
-- ici (announcementEdits, templateEdits, handbookEdits, goalEdits,
-- goalContributions). Le plus récent gagne ; ce que le serveur reflète est purgé.
-- Droits : annonces, modèles, charte et objectifs pour les officiers ;
-- contributions pour ses propres personnages, sur un objectif ouvert.

GP.Communication = {}
local C = GP.Communication

C.TITLE_MAX, C.BODY_MAX, C.HANDBOOK_MAX, C.KEY_MAX, C.NOTE_MAX = 100, 2000, 4000, 40, 120
local KEEP_SECONDS = 60 * 24 * 3600

local TABLES = { "announcementEdits", "templateEdits", "handbookEdits", "goalEdits", "goalContributions" }
local PEER_KINDS = { an = "announcements", tp = "templates", hb = "handbook", go = "goals", gc = "contributions" }

function C.InitDB()
	for _, name in ipairs(TABLES) do
		GuildkeepDB[name] = GuildkeepDB[name] or {}
	end
	GuildkeepDB.commPeer = GuildkeepDB.commPeer or {}
	GuildkeepDB.announcementsSeen = GuildkeepDB.announcementsSeen or {}
end

local function Now()
	return GP.Now()
end

local function GuildKey()
	local guild = GP.GetPlayerGuildName()
	return guild and strtrim(guild):lower() or nil
end

local function PeerStore()
	local key = GuildKey()
	if not key then
		return { announcements = {}, templates = {}, handbook = {}, goals = {}, contributions = {} }
	end
	local store = GuildkeepDB.commPeer[key] or {}
	GuildkeepDB.commPeer[key] = store
	for _, field in pairs(PEER_KINDS) do
		store[field] = store[field] or {}
	end
	return store
end

local function Clip(text, max)
	if type(text) ~= "string" then
		return nil
	end
	text = strtrim(text)
	if text == "" then
		return nil
	end
	return #text > max and text:sub(1, max) or text
end

function C.NormalizeKey(key)
	local clipped = Clip(key, C.KEY_MAX)
	return clipped and clipped:lower() or nil
end

local function Copy(source)
	local copy = {}
	for k, v in pairs(source) do
		copy[k] = v
	end
	return copy
end

-- Fusion ---------------------------------------------------------------------------------

-- Changement d'ici ou reçu, appliqué s'il est plus récent que l'état connu.
local function Apply(byKey, list, key, edit, pending, make)
	local current = byKey[key]
	if not current then
		-- Élément serveur hors de l'export (trop ancien, supprimé) : make renvoie nil, rien à montrer.
		current = make(key, edit)
		if not current then
			return
		end
		byKey[key] = current
		list[#list + 1] = current
	end
	if (edit.at or 0) > (current.updatedAt or 0) then
		for k, v in pairs(edit) do
			if k ~= "at" and k ~= "key" then
				current[k] = v
			end
		end
		current.deleted = edit.deleted == true
		current.updatedAt = edit.at
		current.pendingSync = current.pendingSync or pending
	end
end

local function Sources(peerField, dbName, accept)
	local peer = PeerStore()[peerField]
	local sources = {}
	for key, edit in pairs(peer) do
		sources[#sources + 1] = { key = key, edit = edit, pending = false }
	end
	for key, edit in pairs(GuildkeepDB[dbName]) do
		if accept(edit) then
			sources[#sources + 1] = { key = key, edit = edit, pending = true }
		end
	end
	return sources
end

local function ByOfficerHere(edit)
	return GP.IsInCurrentGuild(edit.by)
end

-- Annonces, modèles, charte et objectifs de la guilde affichée, prêts à afficher.
function C.Get()
	local server = GP.GetServerData().communication or {}

	local announcements, announcementByKey = {}, {}
	for _, a in ipairs(server.announcements or {}) do
		local entry = Copy(a)
		entry.key = tostring(a.id)
		announcements[#announcements + 1] = entry
		announcementByKey[entry.key] = entry
		if a.clientId then
			announcementByKey[a.clientId] = entry
		end
	end
	for _, s in ipairs(Sources("announcements", "announcementEdits", ByOfficerHere)) do
		Apply(announcementByKey, announcements, s.key, s.edit, s.pending, function(key, edit)
			if tonumber(key) then
				return nil
			end
			return { key = key, clientId = key, author = edit.by, createdAt = edit.at, updatedAt = 0, localOnly = true }
		end)
	end

	local templates, templateByKey = {}, {}
	for _, t in ipairs(server.templates or {}) do
		local entry = Copy(t)
		templates[#templates + 1] = entry
		templateByKey[t.key] = entry
	end
	for _, s in ipairs(Sources("templates", "templateEdits", ByOfficerHere)) do
		Apply(templateByKey, templates, s.key, s.edit, s.pending, function(key)
			return { key = key, updatedAt = 0 }
		end)
	end

	local handbook, sectionByKey = {}, {}
	for _, h in ipairs(server.handbook or {}) do
		local entry = Copy(h)
		handbook[#handbook + 1] = entry
		sectionByKey[h.key] = entry
	end
	for _, s in ipairs(Sources("handbook", "handbookEdits", ByOfficerHere)) do
		Apply(sectionByKey, handbook, s.key, s.edit, s.pending, function(key)
			return { key = key, order = 50, updatedAt = 0 }
		end)
	end

	local goals, goalByKey = {}, {}
	for _, g in ipairs(server.goals or {}) do
		local entry = Copy(g)
		entry.key = tostring(g.id)
		entry.contributions = {}
		entry.seen = {}
		for _, c in ipairs(g.contributions or {}) do
			entry.contributions[#entry.contributions + 1] = Copy(c)
			entry.seen[c.clientId] = true
		end
		goals[#goals + 1] = entry
		goalByKey[entry.key] = entry
		if g.clientId then
			goalByKey[g.clientId] = entry
		end
	end
	for _, s in ipairs(Sources("goals", "goalEdits", ByOfficerHere)) do
		Apply(goalByKey, goals, s.key, s.edit, s.pending, function(key, edit)
			if tonumber(key) then
				return nil
			end
			return { key = key, clientId = key, createdBy = edit.by, progress = 0, contributions = {}, seen = {}, updatedAt = 0, localOnly = true }
		end)
	end
	-- Contributions pas encore reprises par le serveur : ajoutées à l'avancement.
	for _, s in ipairs(Sources("contributions", "goalContributions", function(c) return GP.IsInCurrentGuild(c.character) end)) do
		local c = s.edit
		local goal = goalByKey[c.goal]
		if goal and not goal.seen[c.clientId] then
			goal.seen[c.clientId] = true
			goal.progress = (goal.progress or 0) + (c.amount or 0)
			local copy = Copy(c)
			copy.pendingSync = s.pending
			table.insert(goal.contributions, 1, copy)
		end
	end

	local function alive(list)
		local result = {}
		for _, item in ipairs(list) do
			if not item.deleted and item.title then
				result[#result + 1] = item
			end
		end
		return result
	end
	announcements = alive(announcements)
	table.sort(announcements, function(a, b) return (a.createdAt or 0) > (b.createdAt or 0) end)
	templates = alive(templates)
	table.sort(templates, function(a, b) return a.key < b.key end)
	handbook = alive(handbook)
	table.sort(handbook, function(a, b)
		if (a.order or 0) ~= (b.order or 0) then
			return (a.order or 0) < (b.order or 0)
		end
		return a.key < b.key
	end)
	goals = alive(goals)
	table.sort(goals, function(a, b)
		if (a.closed and 1 or 0) ~= (b.closed and 1 or 0) then
			return not a.closed
		end
		return (a.deadline or math.huge) < (b.deadline or math.huge)
	end)
	return { announcements = announcements, templates = templates, handbook = handbook, goals = goals }
end

function C.FindGoal(key)
	for _, goal in ipairs(C.Get().goals) do
		if goal.key == key or goal.clientId == key then
			return goal
		end
	end
end

-- Annonces non lues (pastille d'onglet, message à la connexion).
function C.UnreadCount()
	if not GuildkeepDB or not GuildkeepDB.announcementsSeen then
		return 0
	end
	local count, since = 0, Now() - 14 * 24 * 3600
	for _, a in ipairs(C.Get().announcements) do
		if (a.createdAt or 0) >= since and not GuildkeepDB.announcementsSeen[a.clientId or a.key] and not GuildkeepDB.announcementsSeen[a.key] then
			count = count + 1
		end
	end
	return count
end

function C.MarkAllRead()
	for _, a in ipairs(C.Get().announcements) do
		GuildkeepDB.announcementsSeen[a.key] = Now()
		if a.clientId then
			GuildkeepDB.announcementsSeen[a.clientId] = Now()
		end
	end
end

-- Modèles : {event}, {date} et {guild} remplacés à la rédaction.
function C.Render(text, event)
	local guild = GP.GetPlayerGuildName() or ""
	text = (text or ""):gsub("{[Gg][Uu][Ii][Ll][Dd]}", (guild:gsub("%%", "%%%%")))
	if event then
		text = text:gsub("{[Ee][Vv][Ee][Nn][Tt]}", ((event.title or ""):gsub("%%", "%%%%")))
		text = text:gsub("{[Dd][Aa][Tt][Ee]}", date("%d/%m à %H:%M", event.startsAt))
	end
	return text
end

-- Changements faits en jeu --------------------------------------------------------------------

local function NextAt(previous)
	return math.max(Now(), (previous or 0) + 1)
end

local function Officer()
	if not GP.IsOfficer() then
		return nil, "réservé aux officiers"
	end
	return true
end

local function Store(dbName, kind, key, entry)
	GuildkeepDB[dbName][key] = entry
	GP.Comm.Broadcast({ t = kind, k = key, e = entry })
	GP.RefreshUI()
	return key
end

function C.PostAnnouncement(title, body, eventId, existing)
	local ok, err = Officer()
	if not ok then
		return nil, err
	end
	title, body = Clip(title, C.TITLE_MAX), Clip(body, C.BODY_MAX)
	if not title or not body then
		return nil, "il faut un titre et un texte"
	end
	local me = GP.GetPlayerFullName()
	local key = existing and existing.key or (me .. "-" .. Now() .. "-" .. math.random(1000, 9999))
	return Store("announcementEdits", "an", key, {
		title = title, body = body, eventId = eventId, by = me, deleted = false,
		at = NextAt(existing and existing.updatedAt),
	})
end

function C.DeleteAnnouncement(announcement)
	local ok, err = Officer()
	if not ok then
		return nil, err
	end
	return Store("announcementEdits", "an", announcement.key, {
		title = announcement.title, body = announcement.body or "", eventId = announcement.eventId,
		by = GP.GetPlayerFullName(), deleted = true, at = NextAt(announcement.updatedAt),
	})
end

function C.SaveTemplate(key, title, body, deleted)
	local ok, err = Officer()
	if not ok then
		return nil, err
	end
	key = C.NormalizeKey(key)
	title, body = Clip(title, C.TITLE_MAX), Clip(body, C.BODY_MAX)
	if not key or not title or not body then
		return nil, "il faut un nom, un titre et un texte"
	end
	local previous
	for _, t in ipairs(C.Get().templates) do
		if t.key == key then
			previous = t
		end
	end
	return Store("templateEdits", "tp", key, {
		title = title, body = body, by = GP.GetPlayerFullName(), deleted = deleted and true or false,
		at = NextAt(previous and previous.updatedAt),
	})
end

function C.SaveSection(key, title, body, order, deleted)
	local ok, err = Officer()
	if not ok then
		return nil, err
	end
	key = C.NormalizeKey(key)
	title, body = Clip(title, C.TITLE_MAX), Clip(body, C.HANDBOOK_MAX)
	if not key or not title or (not body and not deleted) then
		return nil, "il faut une clé, un titre et un texte"
	end
	local previous
	for _, h in ipairs(C.Get().handbook) do
		if h.key == key then
			previous = h
		end
	end
	return Store("handbookEdits", "hb", key, {
		title = title, body = body or (previous and previous.body) or "", order = math.max(0, math.min(100, math.floor(tonumber(order) or 50))),
		by = GP.GetPlayerFullName(), deleted = deleted and true or false, at = NextAt(previous and previous.updatedAt),
	})
end

function C.SaveGoal(fields, existing)
	local ok, err = Officer()
	if not ok then
		return nil, err
	end
	local title = Clip(fields.title, C.TITLE_MAX)
	local target = math.floor(tonumber(fields.target) or 0)
	if not title or target <= 0 then
		return nil, "il faut un titre et une cible positive"
	end
	local me = GP.GetPlayerFullName()
	local key = existing and existing.key or (me .. "-" .. Now() .. "-" .. math.random(1000, 9999))
	return Store("goalEdits", "go", key, {
		title = title, description = Clip(fields.description, C.BODY_MAX), target = math.min(target, 1000000),
		unit = Clip(fields.unit, 20), deadline = fields.deadline, closed = fields.closed and true or false,
		by = me, at = NextAt(existing and existing.updatedAt),
	})
end

function C.SetGoalClosed(goal, closed)
	return C.SaveGoal({
		title = goal.title, description = goal.description, target = goal.target, unit = goal.unit,
		deadline = goal.deadline, closed = closed,
	}, goal)
end

function C.Contribute(goal, amount, note)
	amount = math.floor(tonumber(amount) or 0)
	if goal.closed then
		return nil, "cet objectif est clos"
	end
	if amount <= 0 then
		return nil, "indique une quantité positive"
	end
	local me = GP.GetPlayerFullName()
	local clientId = me .. "-" .. Now() .. "-" .. math.random(1000, 9999)
	return Store("goalContributions", "gc", clientId, {
		clientId = clientId, goal = goal.id and tostring(goal.id) or goal.key, character = me,
		amount = math.min(amount, 1000000), note = Clip(note, C.NOTE_MAX), at = Now(),
	})
end

-- Contenu reçu d'un autre joueur ------------------------------------------------------------------

local function Text(v, max)
	return type(v) == "string" and v ~= "" and #v <= max
end

local function Optional(v, max)
	return v == nil or (type(v) == "string" and #v <= max)
end

local function Stamp(v)
	return type(v) == "number" and v > 0 and v < 4102444800
end

local VALIDATE = {
	an = function(e) return Text(e.title, C.TITLE_MAX) and Optional(e.body, C.BODY_MAX) and (e.eventId == nil or type(e.eventId) == "number") end,
	tp = function(e) return Text(e.title, C.TITLE_MAX) and Optional(e.body, C.BODY_MAX) end,
	hb = function(e) return Text(e.title, C.TITLE_MAX) and Optional(e.body, C.HANDBOOK_MAX) and (e.order == nil or type(e.order) == "number") end,
	go = function(e) return Text(e.title, C.TITLE_MAX) and type(e.target) == "number" and e.target > 0 and Optional(e.unit, 20) and Optional(e.description, C.BODY_MAX) end,
	gc = function(e) return Text(e.clientId, 120) and Text(e.goal, 120) and Text(e.character, 64) and type(e.amount) == "number" and e.amount > 0 and Optional(e.note, C.NOTE_MAX) end,
}

-- Un officier pour les annonces, modèles, charte et objectifs ; soi-même pour une contribution.
local function Receive(data, sender)
	local kind, entry, key = data.t, data.e, data.k
	local check = VALIDATE[kind]
	if type(entry) ~= "table" or type(key) ~= "string" or #key > 120 or not check or not Stamp(entry.at) or not check(entry) then
		return
	end
	if kind == "gc" then
		if entry.character ~= sender then
			return
		end
	elseif entry.by ~= sender or not GP.IsOfficerName(sender) then
		return
	end
	local store = PeerStore()[PEER_KINDS[kind]]
	local previous = store[key]
	if not previous or entry.at > (previous.at or 0) then
		store[key] = entry
	end
	if kind == "an" and not entry.deleted and not previous then
		GP.Chat(("annonce de %s : |cffffd100%s|r — /gkeep guilde"):format(sender, entry.title))
	end
end

function C.AnnouncePending()
	local me = GP.GetPlayerFullName()
	for i, dbName in ipairs(TABLES) do
		local kind = ({ "an", "tp", "hb", "go", "gc" })[i]
		for key, entry in pairs(GuildkeepDB[dbName]) do
			if entry.by == me or entry.character == me then
				GP.Comm.Broadcast({ t = kind, k = key, e = entry })
			end
		end
	end
end

-- Purge et export -----------------------------------------------------------------------------------

-- Efface ce que le serveur reflète déjà (toutes guildes du fichier) et ce qui est trop vieux.
function C.Prune()
	local stamps, contributions = {}, {}
	for _, section in ipairs(GP.AllServerSections()) do
		local c = section.communication or {}
		for _, a in ipairs(c.announcements or {}) do
			stamps["an|" .. tostring(a.id)] = a.updatedAt or 0
			if a.clientId then
				stamps["an|" .. a.clientId] = a.updatedAt or 0
			end
		end
		for _, t in ipairs(c.templates or {}) do
			stamps["tp|" .. t.key] = t.updatedAt or 0
		end
		for _, h in ipairs(c.handbook or {}) do
			stamps["hb|" .. h.key] = h.updatedAt or 0
		end
		for _, g in ipairs(c.goals or {}) do
			stamps["go|" .. tostring(g.id)] = g.updatedAt or 0
			if g.clientId then
				stamps["go|" .. g.clientId] = g.updatedAt or 0
			end
			for _, x in ipairs(g.contributions or {}) do
				contributions[x.clientId] = true
			end
		end
	end
	local oldest = Now() - KEEP_SECONDS
	local function prune(rows, kind)
		for key, entry in pairs(rows or {}) do
			local known = kind == "gc" and (contributions[entry.clientId] and math.huge or nil) or stamps[kind .. "|" .. key]
			if (known and known >= (entry.at or 0)) or (entry.at or 0) < oldest then
				rows[key] = nil
			end
		end
	end
	for i, dbName in ipairs(TABLES) do
		prune(GuildkeepDB[dbName], ({ "an", "tp", "hb", "go", "gc" })[i])
	end
	for _, store in pairs(GuildkeepDB.commPeer or {}) do
		for kind, field in pairs(PEER_KINDS) do
			prune(store[field], kind)
		end
	end
end

local function List(rows, convert)
	local list
	for key, entry in pairs(rows) do
		list = list or {}
		list[#list + 1] = convert(key, entry)
	end
	return list
end

-- Même forme que l'upload du compagnon (UploadCommunication) ; nil s'il n'y a rien.
function C.BuildExport()
	local export = {
		announcements = List(GuildkeepDB.announcementEdits, function(key, e)
			return { clientId = key, serverId = tonumber(key), title = e.title, body = e.body or "", eventId = e.eventId,
				by = e.by, deleted = e.deleted and true or false, at = e.at }
		end),
		templates = List(GuildkeepDB.templateEdits, function(key, e)
			return { key = key, title = e.title, body = e.body or "", by = e.by, deleted = e.deleted and true or false, at = e.at }
		end),
		handbook = List(GuildkeepDB.handbookEdits, function(key, e)
			return { key = key, title = e.title, body = e.body or "", order = e.order or 0, by = e.by,
				deleted = e.deleted and true or false, at = e.at }
		end),
		goals = List(GuildkeepDB.goalEdits, function(key, e)
			return { clientId = key, serverId = tonumber(key), title = e.title, description = e.description, target = e.target,
				unit = e.unit, deadline = e.deadline, closed = e.closed and true or false, by = e.by, at = e.at }
		end),
		contributions = List(GuildkeepDB.goalContributions, function(_, e)
			local goalId = tonumber(e.goal)
			return { clientId = e.clientId, goalId = goalId, goalClientId = (not goalId) and e.goal or nil, character = e.character,
				amount = e.amount, note = e.note, at = e.at }
		end),
	}
	if not (export.announcements or export.templates or export.handbook or export.goals or export.contributions) then
		return nil
	end
	return export
end

-- Chargement du module (données) ---------------------------------------------------------------

local function NotifyUnread()
	local unread = C.UnreadCount()
	if unread > 0 then
		GP.Chat(("%d annonce%s de la guilde non lue%s — /gkeep guilde"):format(unread, unread > 1 and "s" or "", unread > 1 and "s" or ""))
	end
end

GP.Modules.Define("communication", "data", function(ctx)
	C.InitDB()
	C.Prune()
	for kind in pairs(PEER_KINDS) do
		ctx:OnPeer(kind, Receive)
	end
	ctx:On("ANNOUNCE_PENDING", C.AnnouncePending)
	ctx:On("EXPORT", function(payload)
		payload.communication = C.BuildExport()
	end)
	ctx:Listen({ "PLAYER_LOGIN" }, function()
		ctx:After(14, NotifyUnread)
	end)
end)
