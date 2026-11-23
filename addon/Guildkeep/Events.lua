local _, GP = ...

-- Module Événements, partie données : événements de guilde, droits et
-- synchronisation.
--
-- Trois sources se superposent, la modification la plus récente gagnant
-- toujours (horodatage GetServerTime(), commun à tous les clients) :
--   1. le serveur (Data.lua ou chaîne importée) : la référence ;
--   2. ce que les autres joueurs connectés ont annoncé sur le canal de guilde
--      (GuildkeepDB.eventPeer) : visible tout de suite, en attendant le serveur ;
--   3. les changements faits sur ce poste (eventEdits, eventSignups,
--      eventPlacements) : envoyés au serveur par le compagnon ou l'export.
-- Chaque changement local ou reçu est effacé dès que le serveur le reflète.
--
-- Trois sortes de changements, chacun avec son horodatage propre :
--   l'événement (organisateur ou officier), la réponse d'un joueur (lui seul),
--   la place dans le roster (organisateur ou officier).
--
-- Un module qui dépend des événements (la soirée de raid) y rattache ses
-- propres lignes par E.Extend : elles suivent la même fusion et la même purge.

GP.Events = {}
local E = GP.Events

E.KINDS = { "raid", "dungeon", "pvp", "other" }
E.KIND_LABELS = { raid = "Raid", dungeon = "Donjon", pvp = "JcJ", other = "Autre" }
E.ROLES = { "tank", "healer", "melee", "ranged" }
E.ROLE_LABELS = { tank = "Tank", healer = "Soigneur", melee = "DPS mêlée", ranged = "DPS distance" }
E.RESPONSE_LABELS = { yes = "Présent", tentative = "Peut-être", no = "Absent", none = "Sans réponse" }
E.PLACEMENT_LABELS = { pending = "En attente", confirmed = "Confirmé", standby = "Remplaçant", bench = "Banc" }

local KEEP_SECONDS = 30 * 24 * 3600 -- au-delà, un changement jamais repris est abandonné
local HISTORY_SECONDS = 14 * 24 * 3600 -- événements passés encore affichés

local Now = GP.Now

function E.InitDB()
	GuildkeepDB.eventEdits = GuildkeepDB.eventEdits or {}
	GuildkeepDB.eventSignups = GuildkeepDB.eventSignups or {}
	GuildkeepDB.eventPlacements = GuildkeepDB.eventPlacements or {}
	GuildkeepDB.eventPeer = GuildkeepDB.eventPeer or {}
end

-- Extensions --------------------------------------------------------------------------
-- Une extension (ex. la soirée de raid) rattache à chaque événement des tables
-- de lignes horodatées : `tables` = { db (table de GuildkeepDB), peer (nom du
-- seau dans l'événement fusionné et dans GuildkeepDB.eventPeer), rowKey (clé
-- d'une ligne) }, et `finish(event)` prépare ses listes après la fusion.
-- Sans extension, ces lignes ne sont ni lues ni purgées : elles restent.
--
-- Le code d'une extension appartient à un autre module (`owner`) : chacun de
-- ses appels est protégé. Une erreur met ce module en échec (il est retiré,
-- extension comprise) et la fusion est refaite sans lui : les événements
-- restent lisibles quoi qu'il arrive à la soirée de raid.

local extensions = {}

local function RemoveExtension(extension)
	for i, other in ipairs(extensions) do
		if other == extension then
			table.remove(extensions, i)
			return
		end
	end
end

function E.Extend(extension, owner)
	extension.owner = owner
	extensions[#extensions + 1] = extension
	return function()
		RemoveExtension(extension)
	end
end

-- Nombre de pannes d'extension : une fusion pendant laquelle il change est refaite.
local faults = 0

-- Appel protégé d'une fonction d'extension. En cas d'erreur, l'extension est
-- retirée et son module mis en échec ; renvoie false.
local function CallExtension(extension, fn, ...)
	if extension.failed then
		return false
	end
	local ok, result = pcall(fn, ...)
	if ok then
		return true, result
	end
	faults = faults + 1
	extension.failed = true
	RemoveExtension(extension)
	GP.Modules.Fault(extension.owner, result)
	return false
end

-- Exécute `run` (une fusion) jusqu'à ce qu'aucune extension n'y tombe en panne :
-- chaque panne retire une extension, le nombre d'essais est donc borné.
local function WithoutFaults(run)
	for _ = 1, #extensions + 1 do
		local before = faults
		local result = run()
		if faults == before then
			return result
		end
	end
	return run()
end

-- Tables des extensions actives ; `extension` désigne son propriétaire.
local function ExtensionTables()
	local list = {}
	for _, extension in ipairs(extensions) do
		for _, def in ipairs(extension.tables or {}) do
			list[#list + 1] = { db = def.db, peer = def.peer, rowKey = def.rowKey, extension = extension }
		end
	end
	return list
end

-- Clé d'une ligne d'extension, ou nil si la fonction de l'extension échoue.
local function RowKey(def, row)
	local ok, key = CallExtension(def.extension, def.rowKey, row)
	if ok and key ~= nil then
		return key
	end
end

-- Seaux des lignes d'extension d'un événement fusionné (`event.night`).
local function NewBuckets()
	local buckets = {}
	for _, def in ipairs(ExtensionTables()) do
		buckets[def.peer] = {}
	end
	return buckets
end

-- Ce que les joueurs connectés ont annoncé, pour la guilde affichée.
function E.PeerStore()
	local key = GP.GuildKey(GP.GetPlayerGuildName())
	if not key then
		return nil
	end
	GuildkeepDB.eventPeer = GuildkeepDB.eventPeer or {}
	local store = GuildkeepDB.eventPeer[key]
	if not store then
		store = {}
		GuildkeepDB.eventPeer[key] = store
	end
	for _, field in ipairs({ "events", "signups", "placements" }) do
		store[field] = store[field] or {}
	end
	for _, def in ipairs(ExtensionTables()) do
		store[def.peer] = store[def.peer] or {}
	end
	return store
end
local PeerStore = E.PeerStore

-- Droits ----------------------------------------------------------------------------

function E.CanManage(event)
	return event ~= nil and (event.organizer == GP.GetPlayerFullName() or GP.IsOfficer())
end

-- Fusion des sources ---------------------------------------------------------------------

local function NewSignup(character)
	return { character = character, response = "none", responseAt = 0, placement = "pending", placementAt = 0 }
end

local EVENT_FIELDS = { "title", "kind", "startsAt", "description", "tank", "healer", "melee", "ranged", "cancelled", "locked" }

-- Index des événements du serveur pour la guilde affichée : clé = id serveur
-- (texte) ET id client, pour qu'un changement fait avant la synchro retrouve
-- son événement une fois numéroté.
local function ServerIndex(section)
	local list, byKey = {}, {}
	for _, src in ipairs(section.events or {}) do
		local event = {
			key = tostring(src.id), id = src.id, clientId = src.clientId, organizer = src.organizer,
			updatedAt = src.updatedAt or 0, signups = {}, night = NewBuckets(),
		}
		for _, field in ipairs(EVENT_FIELDS) do
			event[field] = src[field]
		end
		event.cancelled = event.cancelled == true
		event.locked = event.locked == true
		for _, def in ipairs(ExtensionTables()) do
			for _, row in ipairs(src[def.peer] or {}) do
				local copy = {}
				for k, v in pairs(row) do
					copy[k] = v
				end
				copy.at = copy.at or 0
				local key = RowKey(def, copy)
				if key ~= nil then
					event.night[def.peer][key] = copy
				end
			end
		end
		for _, s in ipairs(src.signups or {}) do
			-- Une inscription sans personnage (donnée serveur inattendue) est ignorée.
			if type(s) == "table" and type(s.character) == "string" then
				event.signups[s.character] = {
					character = s.character, classFile = s.classFile, role = s.role, spec = s.spec,
					response = s.response or "none", note = s.note, responseAt = s.responseAt or 0,
					placement = s.placement or "pending", placementAt = s.placementAt or 0,
				}
			end
		end
		list[#list + 1] = event
		byKey[event.key] = event
		if event.clientId then
			byKey[event.clientId] = event
		end
	end
	return list, byKey
end

local function ApplyEdit(list, byKey, key, edit, pending)
	local event = byKey[key]
	if not event then
		if tonumber(key) then
			return -- événement serveur hors de l'export (trop ancien) : rien à montrer
		end
		event = { key = key, clientId = key, organizer = edit.by, updatedAt = 0, signups = {}, localOnly = true,
			night = NewBuckets() }
		list[#list + 1] = event
		byKey[key] = event
	end
	if (edit.at or 0) > (event.updatedAt or 0) then
		for _, field in ipairs(EVENT_FIELDS) do
			event[field] = edit[field]
		end
		event.cancelled = event.cancelled == true
		event.locked = event.locked == true
		event.updatedAt = edit.at
		event.pendingSync = event.pendingSync or pending
	end
end

local function ApplySignup(byKey, s, pending)
	local event = byKey[s.event]
	if not event then
		return
	end
	local row = event.signups[s.character] or NewSignup(s.character)
	event.signups[s.character] = row
	if (s.at or 0) > row.responseAt then
		row.classFile = s.classFile or row.classFile
		row.role, row.spec, row.response, row.note = s.role, s.spec, s.response, s.note
		row.responseAt = s.at
		row.pendingSync = row.pendingSync or pending
	end
end

-- Ligne d'extension (réservation, rôle de soutien…) : la plus récente gagne.
local function ApplyNight(byKey, def, entry, pending)
	local event = byKey[entry.event]
	if not event then
		return
	end
	local rows = event.night[def.peer]
	local key = rows and RowKey(def, entry)
	if key == nil then
		return
	end
	local current = rows[key]
	if not current or (entry.at or 0) > (current.at or 0) then
		local copy = {}
		for k, v in pairs(entry) do
			copy[k] = v
		end
		copy.pendingSync = pending
		rows[key] = copy
	end
end

local function ApplyPlacement(byKey, p, pending)
	local event = byKey[p.event]
	if not event then
		return
	end
	local row = event.signups[p.character] or NewSignup(p.character)
	event.signups[p.character] = row
	if (p.at or 0) > row.placementAt then
		row.placement = p.placement
		row.placementAt = p.at
		row.pendingSync = row.pendingSync or pending
	end
end

-- Liste des événements de la guilde affichée, triée par date. Chaque
-- événement porte `signups` en tableau (rangé par place puis par nom) et
-- `mine` : la ligne du personnage connecté, s'il a répondu.
local function MergeEvents()
	local list, byKey = ServerIndex(GP.GetServerData())
	local peer = PeerStore() or { events = {}, signups = {}, placements = {} }

	for key, edit in pairs(peer.events) do
		ApplyEdit(list, byKey, key, edit, false)
	end
	for key, edit in pairs(GuildkeepDB.eventEdits) do
		if GP.IsInCurrentGuild(edit.by) then
			ApplyEdit(list, byKey, key, edit, true)
		end
	end
	for _, s in pairs(peer.signups) do
		ApplySignup(byKey, s, false)
	end
	for _, s in pairs(GuildkeepDB.eventSignups) do
		if GP.IsInCurrentGuild(s.character) then
			ApplySignup(byKey, s, true)
		end
	end
	for _, p in pairs(peer.placements) do
		ApplyPlacement(byKey, p, false)
	end
	for _, p in pairs(GuildkeepDB.eventPlacements) do
		if GP.IsInCurrentGuild(p.by) then
			ApplyPlacement(byKey, p, true)
		end
	end
	for _, def in ipairs(ExtensionTables()) do
		for _, entry in pairs(peer[def.peer] or {}) do
			ApplyNight(byKey, def, entry, false)
		end
		for _, entry in pairs(GuildkeepDB[def.db] or {}) do
			if GP.IsInCurrentGuild(entry.by) then
				ApplyNight(byKey, def, entry, true)
			end
		end
	end

	local me, since = GP.GetPlayerFullName(), Now() - HISTORY_SECONDS
	local result = {}
	for _, event in ipairs(list) do
		if event.title and (event.startsAt or 0) >= since then
			local rows = {}
			for _, row in pairs(event.signups) do
				-- Un joueur qui se déclare absent perd sa place : l'organisateur le replacera s'il revient.
				if row.response == "no" then
					row.placement = "pending"
				end
				rows[#rows + 1] = row
			end
			table.sort(rows, function(a, b) return a.character < b.character end)
			event.signups = rows
			event.mine = nil
			for _, row in ipairs(rows) do
				if row.character == me then
					event.mine = row
				end
			end
			-- Copie : une extension en panne est retirée de la liste pendant le parcours.
			for _, extension in ipairs({ unpack(extensions) }) do
				if extension.finish then
					CallExtension(extension, extension.finish, event)
				end
			end
			result[#result + 1] = event
		end
	end
	table.sort(result, function(a, b)
		if (a.startsAt or 0) ~= (b.startsAt or 0) then
			return (a.startsAt or 0) < (b.startsAt or 0)
		end
		return a.key < b.key
	end)
	return result
end

function E.GetEvents()
	return WithoutFaults(MergeEvents)
end

function E.Find(key)
	for _, event in ipairs(E.GetEvents()) do
		if event.key == key or event.clientId == key then
			return event
		end
	end
end

-- Comptes par rôle : réponses « présent » et joueurs confirmés.
function E.Counts(event)
	local counts = {}
	for _, role in ipairs(E.ROLES) do
		counts[role] = { yes = 0, confirmed = 0, target = event[role] or 0 }
	end
	counts.tentative, counts.standby, counts.bench, counts.total = 0, 0, 0, 0
	for _, row in ipairs(event.signups) do
		local c = counts[row.role]
		if row.response == "yes" and c then
			c.yes = c.yes + 1
			counts.total = counts.total + 1
		end
		if row.placement == "confirmed" and c then
			c.confirmed = c.confirmed + 1
		end
		if row.response == "tentative" then
			counts.tentative = counts.tentative + 1
		end
		if row.placement == "standby" then
			counts.standby = counts.standby + 1
		elseif row.placement == "bench" then
			counts.bench = counts.bench + 1
		end
	end
	return counts
end

function E.IsUpcoming(event)
	-- Encore « en cours » quatre heures après le début.
	return not event.cancelled and (event.startsAt or 0) >= Now() - 4 * 3600
end

-- Événements à venir où le personnage connecté n'a pas encore répondu (pastille d'onglet).
function E.CountAwaitingAnswer()
	if not GuildkeepDB or not GuildkeepDB.eventEdits then
		return 0
	end
	local count = 0
	for _, event in ipairs(E.GetEvents()) do
		if E.IsUpcoming(event) and (not event.mine or event.mine.response == "none") then
			count = count + 1
		end
	end
	return count
end

-- Rôle et spécialisation du personnage connecté --------------------------------------------

local RANGED_CLASSES = { MAGE = true, WARLOCK = true, HUNTER = true }

function E.DetectRole()
	local classFile = select(2, UnitClass("player"))
	local specName, role
	if GetSpecialization and GetSpecializationInfo then
		local ok, index = pcall(GetSpecialization)
		if ok and index then
			local _, name, _, _, specRole = GetSpecializationInfo(index)
			specName = name
			if specRole == "TANK" then
				role = "tank"
			elseif specRole == "HEALER" then
				role = "healer"
			elseif specRole == "DAMAGER" then
				local rangedSpec = (classFile == "PRIEST") or (classFile == "DRUID" and index == 1) or (classFile == "SHAMAN" and index == 1)
				role = (RANGED_CLASSES[classFile] or rangedSpec) and "ranged" or "melee"
			end
		end
	end
	if not role then
		role = RANGED_CLASSES[classFile] and "ranged" or (classFile == "PRIEST" and "healer") or "melee"
	end
	return role, specName, classFile
end

-- Changements faits en jeu ---------------------------------------------------------------

local function EventKeyOf(event)
	return event.id and tostring(event.id) or event.key
end

local NextAt = GP.NextAt
E.EventKeyOf = EventKeyOf

local function Validate(fields)
	local title = strtrim(fields.title or "")
	if title == "" then
		return nil, "donne un titre à l'événement"
	end
	if not fields.startsAt or fields.startsAt <= 0 then
		return nil, "date ou heure invalide"
	end
	local clean = {
		title = title:sub(1, 80),
		kind = E.KIND_LABELS[fields.kind] and fields.kind or "other",
		startsAt = fields.startsAt,
		description = fields.description and strtrim(fields.description) ~= "" and strtrim(fields.description):sub(1, 500) or nil,
		cancelled = fields.cancelled and true or false,
		locked = fields.locked and true or false,
	}
	for _, role in ipairs(E.ROLES) do
		clean[role] = math.max(0, math.min(40, math.floor(tonumber(fields[role]) or 0)))
	end
	return clean
end

function E.Create(fields)
	local clean, err = Validate(fields)
	if not clean then
		return nil, err
	end
	local me = GP.GetPlayerFullName()
	local key = me .. "-" .. Now() .. "-" .. math.random(1000, 9999)
	clean.by, clean.at = me, Now()
	GuildkeepDB.eventEdits[key] = clean
	GP.Comm.Broadcast({ t = "ev", key = key, e = clean })
	GP.RefreshUI()
	return key
end

function E.Update(event, fields)
	if not E.CanManage(event) then
		return nil, "seuls l'organisateur et les officiers peuvent modifier cet événement"
	end
	local clean, err = Validate(fields)
	if not clean then
		return nil, err
	end
	clean.by, clean.at = GP.GetPlayerFullName(), NextAt(event.updatedAt)
	local key = EventKeyOf(event)
	GuildkeepDB.eventEdits[key] = clean
	GP.Comm.Broadcast({ t = "ev", key = key, e = clean })
	GP.RefreshUI()
	return key
end

local function UpdateField(event, field, value)
	local fields = {}
	for _, f in ipairs(EVENT_FIELDS) do
		fields[f] = event[f]
	end
	fields[field] = value
	return E.Update(event, fields)
end

function E.SetCancelled(event, cancelled)
	return UpdateField(event, "cancelled", cancelled)
end

function E.SetLocked(event, locked)
	return UpdateField(event, "locked", locked)
end

function E.Respond(event, response, role, spec, note)
	local me = GP.GetPlayerFullName()
	local previous = event.mine
	local key = EventKeyOf(event)
	local signup = {
		event = key,
		character = me,
		classFile = select(2, UnitClass("player")),
		role = role,
		spec = spec and strtrim(spec) ~= "" and strtrim(spec):sub(1, 40) or nil,
		response = response,
		note = note and strtrim(note) ~= "" and strtrim(note):sub(1, 120) or nil,
		at = NextAt(previous and previous.responseAt),
	}
	GuildkeepDB.eventSignups[key .. "|" .. me] = signup
	GP.Comm.Broadcast({ t = "su", s = signup })
	GP.RefreshUI()
end

function E.Place(event, character, placement)
	if not E.CanManage(event) then
		return
	end
	local key = EventKeyOf(event)
	local previous
	for _, row in ipairs(event.signups) do
		if row.character == character then
			previous = row
		end
	end
	local entry = {
		event = key, character = character, placement = placement,
		by = GP.GetPlayerFullName(), at = NextAt(previous and previous.placementAt),
	}
	GuildkeepDB.eventPlacements[key .. "|" .. character] = entry
	GP.Comm.Broadcast({ t = "pl", p = entry })
	GP.RefreshUI()
end

-- Invitations : les confirmés en ligne, raid formé au besoin ---------------------------------

local function InviteName(character)
	-- Sur Forever, le nom de famille occupe le slot royaume : « Prénom-Nom ».
	return (character:gsub(" ", "-", 1))
end

local MAX_RAID = 40
local pendingInvites = {}

local function InviteNext()
	local name = table.remove(pendingInvites, 1)
	if not name then
		return
	end
	if InCombatLockdown and InCombatLockdown() then
		table.insert(pendingInvites, 1, name)
		C_Timer.After(2, InviteNext)
		return
	end
	-- Plus de 5 : le groupe doit passer en raid, une fois la conversion effective.
	if IsInGroup() and not IsInRaid() and GetNumGroupMembers() >= 5 then
		if C_PartyInfo and C_PartyInfo.ConvertToRaid then
			C_PartyInfo.ConvertToRaid()
		end
		table.insert(pendingInvites, 1, name)
		C_Timer.After(1, InviteNext)
		return
	end
	local invite = C_PartyInfo and C_PartyInfo.InviteUnit or InviteUnit
	pcall(invite, name)
	C_Timer.After(0.5, InviteNext)
end

function E.InviteConfirmed(event)
	if InCombatLockdown and InCombatLockdown() then
		return 0, "impossible en combat"
	end
	if IsInGroup() and not (UnitIsGroupLeader("player") or UnitIsGroupAssistant("player")) then
		return 0, "il faut être chef ou assistant du groupe pour inviter"
	end
	if IsInGroup(LE_PARTY_CATEGORY_INSTANCE) then
		return 0, "impossible depuis un groupe de recherche"
	end
	local me = GP.GetPlayerFullName()
	local targets, offline = {}, 0
	for _, row in ipairs(event.signups) do
		local name = InviteName(row.character)
		if row.placement == "confirmed" and row.character ~= me and not UnitInParty(name) and not UnitInRaid(name) then
			if GP.UI.IsOnline(row.character) == true then
				targets[#targets + 1] = name
			else
				offline = offline + 1
			end
		end
	end
	local room = MAX_RAID - (IsInGroup() and GetNumGroupMembers() or 1)
	while #targets > room do
		table.remove(targets)
	end
	if #targets == 0 then
		return 0, offline > 0 and ("aucun confirmé en ligne à inviter (%d hors ligne)"):format(offline) or "aucun confirmé à inviter"
	end
	local alreadyRunning = #pendingInvites > 0
	for _, name in ipairs(targets) do
		pendingInvites[#pendingInvites + 1] = name
	end
	if not alreadyRunning then
		InviteNext()
	end
	if offline > 0 then
		return #targets, ("%d invitation(s) en cours ; %d confirmé(s) hors ligne"):format(#targets, offline)
	end
	return #targets
end

-- Liste copiable du roster (pour Discord ou un tableur).
function E.RosterText(event)
	local lines = { ("%s — %s"):format(event.title, date("%d/%m/%Y %H:%M", event.startsAt)) }
	for _, placement in ipairs({ "confirmed", "standby", "bench" }) do
		local names = {}
		for _, row in ipairs(event.signups) do
			if row.placement == placement then
				names[#names + 1] = ("%s (%s%s)"):format(row.character, E.ROLE_LABELS[row.role] or "?", row.spec and (", " .. row.spec) or "")
			end
		end
		if #names > 0 then
			lines[#lines + 1] = E.PLACEMENT_LABELS[placement] .. " : " .. table.concat(names, ", ")
		end
	end
	return table.concat(lines, "\n")
end

-- Purge ----------------------------------------------------------------------------------

-- Efface les changements locaux et reçus que le serveur reflète déjà (toutes
-- guildes du fichier : au chargement, la guilde du personnage est inconnue),
-- et ceux trop vieux pour être jamais repris.
local function PruneOnce()
	local byKey = {}
	for _, section in ipairs(GP.AllServerSections()) do
		local _, index = ServerIndex(section)
		for key, event in pairs(index) do
			byKey[key] = event
		end
	end
	local oldest = Now() - KEEP_SECONDS

	local function pruneEdits(edits)
		for key, edit in pairs(edits) do
			local server = byKey[key]
			if (server and server.updatedAt >= (edit.at or 0)) or (edit.at or 0) < oldest then
				edits[key] = nil
			end
		end
	end
	local function pruneRows(rows, field)
		for key, entry in pairs(rows) do
			local server = byKey[entry.event]
			local row = server and server.signups[entry.character]
			if (row and row[field] >= (entry.at or 0)) or (entry.at or 0) < oldest then
				rows[key] = nil
			end
		end
	end

	local function pruneNight(rows, def)
		for key, entry in pairs(rows or {}) do
			local server = byKey[entry.event]
			local bucket = server and server.night[def.peer]
			local rowKey = bucket and RowKey(def, entry)
			-- Clé illisible (extension en panne) : la ligne est gardée telle quelle.
			if not bucket or rowKey ~= nil then
				local row = bucket and bucket[rowKey]
				if (row and (row.at or 0) >= (entry.at or 0)) or (entry.at or 0) < oldest then
					rows[key] = nil
				end
			end
		end
	end

	pruneEdits(GuildkeepDB.eventEdits)
	pruneRows(GuildkeepDB.eventSignups, "responseAt")
	pruneRows(GuildkeepDB.eventPlacements, "placementAt")
	for _, def in ipairs(ExtensionTables()) do
		pruneNight(GuildkeepDB[def.db], def)
	end
	for _, store in pairs(GuildkeepDB.eventPeer) do
		pruneEdits(store.events or {})
		pruneRows(store.signups or {}, "responseAt")
		pruneRows(store.placements or {}, "placementAt")
		for _, def in ipairs(ExtensionTables()) do
			pruneNight(store[def.peer], def)
		end
	end
end

function E.Prune()
	WithoutFaults(PruneOnce)
end

-- Export manuel (même forme que l'upload du compagnon) -------------------------------------

-- Référence d'un événement : id serveur (nombre) ou id client.
function E.EventRef(key)
	local id = tonumber(key)
	return id, (not id) and key or nil
end

function E.BuildExport()
	local events, signups, placements
	for key, edit in pairs(GuildkeepDB.eventEdits) do
		local serverId, clientId = E.EventRef(key)
		events = events or {}
		events[#events + 1] = {
			clientId = clientId, serverId = serverId, title = edit.title, kind = edit.kind, startsAt = edit.startsAt,
			description = edit.description, by = edit.by, tank = edit.tank or 0, healer = edit.healer or 0,
			melee = edit.melee or 0, ranged = edit.ranged or 0, cancelled = edit.cancelled and true or false, at = edit.at,
			locked = edit.locked and true or false,
		}
	end
	for _, s in pairs(GuildkeepDB.eventSignups) do
		local eventId, eventClientId = E.EventRef(s.event)
		signups = signups or {}
		signups[#signups + 1] = {
			eventId = eventId, eventClientId = eventClientId, character = s.character, classFile = s.classFile,
			role = s.role, spec = s.spec, response = s.response, note = s.note, at = s.at,
		}
	end
	for _, p in pairs(GuildkeepDB.eventPlacements) do
		local eventId, eventClientId = E.EventRef(p.event)
		placements = placements or {}
		placements[#placements + 1] = {
			eventId = eventId, eventClientId = eventClientId, character = p.character,
			placement = p.placement, by = p.by, at = p.at,
		}
	end
	return events, signups, placements
end

-- Synchro directe entre joueurs connectés ---------------------------------------------------
-- Canal partagé du socle (GP.Comm) : « ev » (événement), « su » (réponse),
-- « pl » (place dans le roster).

local Notify = GP.Chat

-- Contenu reçu d'un autre joueur : types et valeurs vérifiés avant de l'afficher.
local function IsText(v, max)
	return type(v) == "string" and v ~= "" and #v <= max
end

local function OptionalText(v, max)
	return v == nil or (type(v) == "string" and #v <= max)
end

local function IsStamp(v)
	return type(v) == "number" and v > 0 and v < 4102444800
end
E.IsStamp = IsStamp

local VALID_RESPONSES = { yes = true, tentative = true, no = true }

local function CleanEdit(e)
	if type(e) ~= "table" or not IsText(e.title, 80) or not IsStamp(e.startsAt) or not IsStamp(e.at)
		or not IsText(e.by, 64) or not OptionalText(e.description, 500) or not E.KIND_LABELS[e.kind] then
		return nil
	end
	e.locked = e.locked == true
	for _, role in ipairs(E.ROLES) do
		if type(e[role]) ~= "number" or e[role] < 0 or e[role] > 40 then
			return nil
		end
	end
	e.cancelled = e.cancelled == true
	return e
end

local function CleanSignup(s)
	if type(s) ~= "table" or not IsText(s.event, 120) or not IsText(s.character, 64) or not IsStamp(s.at)
		or not VALID_RESPONSES[s.response] or not E.ROLE_LABELS[s.role]
		or not OptionalText(s.spec, 40) or not OptionalText(s.note, 120) or not OptionalText(s.classFile, 20) then
		return nil
	end
	return s
end

local function CleanPlacement(p)
	if type(p) ~= "table" or not IsText(p.event, 120) or not IsText(p.character, 64) or not IsText(p.by, 64)
		or not IsStamp(p.at) or not E.PLACEMENT_LABELS[p.placement] then
		return nil
	end
	return p
end

-- L'expéditeur peut-il gérer cet événement ? Organisateur connu, ou officier.
function E.SenderManages(eventKey, sender)
	local event = E.Find(eventKey)
	return (event and event.organizer == sender) or GP.IsOfficerName(sender)
end
local SenderManages = E.SenderManages

local function Receive(data, sender)
	local store = PeerStore()
	if not store then
		return
	end
	if data.t == "ev" and type(data.key) == "string" and #data.key <= 120 then
		local edit = CleanEdit(data.e)
		if not edit or edit.by ~= sender then
			return
		end
		local known = E.Find(data.key)
		-- Un événement existant ne se modifie que par son organisateur ou un officier ;
		-- un nouveau, par son créateur (qui en devient l'organisateur).
		if known and not SenderManages(data.key, sender) then
			return
		end
		if not known and tonumber(data.key) then
			return -- id serveur inconnu ici : on attend la synchro plutôt que de le créer
		end
		local previous = store.events[data.key]
		if not previous or edit.at > (previous.at or 0) then
			store.events[data.key] = edit
		end
		GuildkeepDB.eventSeen = GuildkeepDB.eventSeen or {}
		if not known and not edit.cancelled and edit.startsAt > Now() and not GuildkeepDB.eventSeen[data.key] then
			GuildkeepDB.eventSeen[data.key] = edit.at
			Notify(("nouvel événement de %s : |cffffd100%s|r le %s — /gkeep events"):format(
				sender, edit.title, date("%d/%m %H:%M", edit.startsAt)))
		end
	elseif data.t == "su" then
		data.s = CleanSignup(data.s)
		if not data.s or data.s.character ~= sender then
			return
		end
		local key = data.s.event .. "|" .. data.s.character
		local previous = store.signups[key]
		if not previous or (data.s.at or 0) > (previous.at or 0) then
			store.signups[key] = data.s
		end
	elseif data.t == "pl" then
		data.p = CleanPlacement(data.p)
		if not data.p or data.p.by ~= sender or not SenderManages(data.p.event, sender) then
			return
		end
		local key = data.p.event .. "|" .. data.p.character
		local previous = store.placements[key]
		if not previous or (data.p.at or 0) > (previous.at or 0) then
			store.placements[key] = data.p
		end
		if data.p.character == GP.GetPlayerFullName() then
			local event = E.Find(data.p.event)
			if event then
				Notify(("%s : tu es |cffffd100%s|r (par %s)."):format(event.title, (E.PLACEMENT_LABELS[data.p.placement] or "?"):lower(), sender))
			end
		end
	end
end

function E.AnnouncePending()
	for key, edit in pairs(GuildkeepDB.eventEdits) do
		if GP.IsInCurrentGuild(edit.by) and edit.by == GP.GetPlayerFullName() then
			GP.Comm.Broadcast({ t = "ev", key = key, e = edit })
		end
	end
	for _, s in pairs(GuildkeepDB.eventSignups) do
		if s.character == GP.GetPlayerFullName() then
			GP.Comm.Broadcast({ t = "su", s = s })
		end
	end
	for _, p in pairs(GuildkeepDB.eventPlacements) do
		if p.by == GP.GetPlayerFullName() then
			GP.Comm.Broadcast({ t = "pl", p = p })
		end
	end
end

-- Événements à venir sans réponse : rappel à la connexion.
local function NotifyWaiting()
	if not IsInGuild() then
		return
	end
	local waiting = E.CountAwaitingAnswer()
	if waiting > 0 then
		Notify(("%d événement%s attend%s ta réponse — /gkeep events"):format(
			waiting, waiting > 1 and "s" or "", waiting > 1 and "ent" or ""))
	end
end

local function Export(payload)
	payload.events, payload.signups, payload.placements = E.BuildExport()
end

GP.Modules.Define("events", "data", function(ctx)
	E.InitDB()
	E.Prune()
	for _, kind in ipairs({ "ev", "su", "pl" }) do
		ctx:OnPeer(kind, Receive)
	end
	ctx:On("ANNOUNCE_PENDING", E.AnnouncePending)
	ctx:On("EXPORT", Export)
	-- Laisse le temps à la guilde d'être connue, puis rappelle les événements en attente de réponse.
	ctx:Listen({ "PLAYER_LOGIN" }, function()
		ctx:After(12, NotifyWaiting)
	end)
end)
