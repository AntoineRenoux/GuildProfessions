local _, GP = ...

-- Module Soirée de raid, partie données : réservations de butin, réservations
-- fermes, verrouillage, rôles de soutien et liste de contrôle. Dépend du module
-- Événements (seule dépendance entre modules, déclarée dans Modules.lua) : ses
-- lignes s'y rattachent par E.Extend et suivent le même modèle — chaque
-- changement est horodaté, rangé dans GuildkeepDB (envoyé au serveur),
-- diffusé aux joueurs connectés, et effacé dès que le serveur le reflète.
--
-- Règles (identiques au serveur et à Discord) :
--   - un joueur réserve pour lui un objet par priorité (haute, normale, basse),
--     tant que les réservations ne sont pas verrouillées ;
--   - l'organisateur ou un officier réserve pour n'importe qui (joueur sans
--     addon), même verrouillé ; lui seul pose les réservations fermes et les
--     rôles de soutien ;
--   - verrouillé, seules comptent les réservations des joueurs confirmés.

local E = GP.Events
GP.RaidNight = {}
local R = GP.RaidNight

R.PRIORITIES = { "high", "normal", "low" }
R.PRIORITY_LABELS = { high = "Haute", normal = "Normale", low = "Basse" }
-- Rang d'affichage des priorités ; une priorité inconnue de ce client (ajoutée
-- côté serveur plus tard) est rangée en dernier au lieu de faire planter le tri.
local PRIORITY_RANK = { high = 1, normal = 2, low = 3 }
local UNKNOWN_RANK = 99

-- Libellé d'une priorité, toléré pour une valeur inconnue (affichée telle quelle).
function R.PriorityLabel(priority)
	return R.PRIORITY_LABELS[priority] or tostring(priority or "?")
end
R.DUTIES = { "lead", "summoner", "consumables", "repair" }
R.DUTY_LABELS = { lead = "Chef de raid", summoner = "Invocateur", consumables = "Consommables", repair = "Robot de réparation" }

-- Tables de changements : nom dans GuildkeepDB, seau dans l'événement fusionné
-- (event.night) et dans GuildkeepDB.eventPeer, clé d'une ligne.
local RESERVES = { db = "eventReserves", peer = "reserves", kind = "sr", rowKey = function(r) return r.character .. "|" .. r.priority end }
local HARD = { db = "eventHardReserves", peer = "hardReserves", kind = "hr", rowKey = function(r) return tostring(r.itemId) end }
local DUTIES = { db = "eventDuties", peer = "duties", kind = "du", rowKey = function(r) return r.duty end }
local TABLES = { RESERVES, HARD, DUTIES }
local BY_KIND = { sr = RESERVES, hr = HARD, du = DUTIES }

local function InitDB()
	for _, def in ipairs(TABLES) do
		GuildkeepDB[def.db] = GuildkeepDB[def.db] or {}
	end
end

-- Objets -----------------------------------------------------------------------------------

-- Numéro d'objet dans un lien du jeu (shift-clic), « item=19019 » ou un nombre.
function R.ParseItem(text)
	text = strtrim(text or "")
	local id = text:match("item[:=](%d+)") or text:match("^(%d+)$")
	id = tonumber(id)
	return id and id > 0 and id or nil
end


-- Le personnage peut-il porter l'objet ? Armures et armes d'après la classe
-- (compétences du jeu d'origine) ; nil quand on ne sait pas (bijou, objet pas
-- encore en cache…), pour n'avertir qu'à coup sûr.
local ARMOR = 4
local WEAPON = 2
local ARMOR_BY_CLASS = {
	WARRIOR = { 1, 2, 3, 4, 6 }, PALADIN = { 1, 2, 3, 4, 6, 7 }, HUNTER = { 1, 2, 3 }, ROGUE = { 1, 2 },
	PRIEST = { 1 }, SHAMAN = { 1, 2, 3, 6, 9 }, MAGE = { 1 }, WARLOCK = { 1 }, DRUID = { 1, 2, 8 },
}
local WEAPON_BY_CLASS = {
	WARRIOR = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 13, 15, 16, 18 },
	PALADIN = { 0, 1, 4, 5, 6, 7, 8 },
	HUNTER = { 0, 1, 2, 3, 6, 7, 8, 10, 13, 15, 16, 18 },
	ROGUE = { 2, 3, 4, 7, 13, 15, 16, 18 },
	PRIEST = { 4, 10, 15, 19 },
	SHAMAN = { 0, 1, 4, 5, 10, 13, 15 },
	MAGE = { 7, 10, 15, 19 },
	WARLOCK = { 7, 10, 15, 19 },
	DRUID = { 4, 5, 10, 13, 15 },
}
local ARMOR_CHECKED = { [1] = true, [2] = true, [3] = true, [4] = true, [6] = true, [7] = true, [8] = true, [9] = true }

local function Contains(list, value)
	for _, v in ipairs(list or {}) do
		if v == value then
			return true
		end
	end
	return false
end

function R.CanUseItem(classFile, itemId)
	local instant = (C_Item and C_Item.GetItemInfoInstant) or GetItemInfoInstant
	if not classFile or not instant then
		return nil
	end
	local ok, _, _, _, equipLoc, _, classId, subclassId = pcall(instant, itemId)
	if not ok or not classId then
		return nil
	end
	if classId == ARMOR and ARMOR_CHECKED[subclassId] and equipLoc ~= "INVTYPE_CLOAK" then
		return Contains(ARMOR_BY_CLASS[classFile], subclassId)
	end
	if classId == WEAPON and WEAPON_BY_CLASS[classFile] then
		return Contains(WEAPON_BY_CLASS[classFile], subclassId)
	end
	return nil
end

-- Événement fusionné : listes prêtes à afficher --------------------------------------------

function R.FinishNight(event)
	local night = event.night or { reserves = {}, hardReserves = {}, duties = {} }
	local confirmed = {}
	for _, row in ipairs(event.signups) do
		if row.placement == "confirmed" then
			confirmed[row.character] = true
		end
	end
	local hard, hardList = {}, {}
	for _, h in pairs(night.hardReserves) do
		-- Ligne venue du serveur non contrôlée : un objet sans numéro est ignoré.
		if h.active and type(h.itemId) == "number" then
			hard[h.itemId] = h
			hardList[#hardList + 1] = h
		end
	end
	table.sort(hardList, function(a, b) return a.itemId < b.itemId end)

	local all, effective, mine = {}, {}, {}
	local me = GP.GetPlayerFullName()
	for _, r in pairs(night.reserves) do
		-- Lignes venues du serveur ou d'autres joueurs : une ligne mal formée est
		-- ignorée plutôt que de faire échouer toute la soirée.
		if type(r.itemId) == "number" and r.itemId > 0 and type(r.character) == "string" and r.priority ~= nil then
			all[#all + 1] = r
			r.blockedByHard = hard[r.itemId] ~= nil
			r.droppedByLock = event.locked and not confirmed[r.character] or false
			if not r.blockedByHard and not r.droppedByLock then
				effective[#effective + 1] = r
			end
			if r.character == me and R.PRIORITY_LABELS[r.priority] then
				mine[r.priority] = r
			end
		end
	end
	-- Ordre total (objet, rang, priorité, personnage) : deux priorités inconnues
	-- différentes restent ordonnées entre elles, le tri reste cohérent.
	local function byItem(a, b)
		if a.itemId ~= b.itemId then
			return a.itemId < b.itemId
		end
		if a.priority ~= b.priority then
			local ra, rb = PRIORITY_RANK[a.priority] or UNKNOWN_RANK, PRIORITY_RANK[b.priority] or UNKNOWN_RANK
			if ra ~= rb then
				return ra < rb
			end
			return tostring(a.priority) < tostring(b.priority)
		end
		return a.character < b.character
	end
	table.sort(all, byItem)
	table.sort(effective, byItem)

	local duties = {}
	for duty, d in pairs(night.duties) do
		duties[duty] = d.character
	end
	event.reserves, event.effectiveReserves, event.myReserves = all, effective, mine
	event.hardReserves, event.duties = hardList, duties
end

-- Réservations effectives groupées par objet, avec le nombre de concurrents.
function R.ReserveGroups(event)
	local groups, index = {}, {}
	for _, r in ipairs(event.effectiveReserves or {}) do
		local group = index[r.itemId]
		if not group then
			group = { itemId = r.itemId, itemName = r.itemName, rows = {} }
			index[r.itemId] = group
			groups[#groups + 1] = group
		end
		group.itemName = group.itemName or r.itemName
		group.rows[#group.rows + 1] = r
	end
	return groups
end

-- Changements faits en jeu ---------------------------------------------------------------

local function Store(def, entry)
	GuildkeepDB[def.db][entry.event .. "|" .. def.rowKey(entry)] = entry
	GP.Comm.Broadcast({ t = def.kind, n = entry })
	GP.RefreshUI()
end

function R.SetReserve(event, priority, itemId, itemName, character)
	local me = GP.GetPlayerFullName()
	character = character or me
	local manager = E.CanManage(event)
	if character ~= me and not manager then
		return nil, "seuls l'organisateur et les officiers réservent pour un autre joueur"
	end
	if event.locked and not manager then
		return nil, "les réservations sont verrouillées"
	end
	if not R.PRIORITY_LABELS[priority] then
		return nil, "priorité inconnue"
	end
	itemId = itemId or 0
	if itemId > 0 then
		for _, h in ipairs(event.hardReserves or {}) do
			if h.itemId == itemId then
				return nil, "cet objet est réservé d'office"
			end
		end
		for _, r in ipairs(event.reserves or {}) do
			if r.character == character and r.itemId == itemId and r.priority ~= priority then
				return nil, ("déjà réservé en priorité %s"):format(R.PriorityLabel(r.priority):lower())
			end
		end
	end
	local previous
	for _, r in pairs(event.night.reserves) do
		if r.character == character and r.priority == priority then
			previous = r
		end
	end
	Store(RESERVES, {
		event = E.EventKeyOf(event), character = character, priority = priority, itemId = itemId,
		itemName = itemId > 0 and itemName or nil, by = me, at = GP.NextAt(previous and previous.at),
	})
	return true
end

function R.SetHardReserve(event, itemId, itemName, holder, active)
	if not E.CanManage(event) then
		return nil, "réservé à l'organisateur et aux officiers"
	end
	local previous = event.night.hardReserves[tostring(itemId)]
	Store(HARD, {
		event = E.EventKeyOf(event), itemId = itemId, itemName = itemName or (previous and previous.itemName),
		holder = holder and strtrim(holder) ~= "" and strtrim(holder) or nil, active = active and true or false,
		by = GP.GetPlayerFullName(), at = GP.NextAt(previous and previous.at),
	})
	return true
end

function R.SetDuty(event, duty, character)
	if not E.CanManage(event) then
		return nil, "réservé à l'organisateur et aux officiers"
	end
	local previous = event.night.duties[duty]
	Store(DUTIES, {
		event = E.EventKeyOf(event), duty = duty, character = character,
		by = GP.GetPlayerFullName(), at = GP.NextAt(previous and previous.at),
	})
	return true
end

-- Maître du butin, proposé au verrouillage (chef de raid seulement).
function R.SetMasterLooter()
	if not (IsInRaid() and UnitIsGroupLeader("player")) then
		return false
	end
	local me = UnitName("player")
	if C_PartyInfo and C_PartyInfo.SetLootMethod and Enum and Enum.LootMethod then
		return pcall(C_PartyInfo.SetLootMethod, Enum.LootMethod.Masterlooter, me)
	end
	if SetLootMethod then
		return pcall(SetLootMethod, "master", me)
	end
	return false
end

-- Contenu reçu des autres joueurs ---------------------------------------------------------

local function Text(v, max)
	return type(v) == "string" and v ~= "" and #v <= max
end

local function Optional(v, max)
	return v == nil or (type(v) == "string" and #v <= max)
end

function R.CleanNight(kind, n)
	if type(n) ~= "table" or not Text(n.event, 120) or not Text(n.by, 64) or not E.IsStamp(n.at) then
		return nil
	end
	if kind == "sr" then
		if Text(n.character, 64) and R.PRIORITY_LABELS[n.priority] and type(n.itemId) == "number" and n.itemId >= 0
			and Optional(n.itemName, 100) then
			return n
		end
	elseif kind == "hr" then
		if type(n.itemId) == "number" and n.itemId > 0 and Optional(n.itemName, 100) and Optional(n.holder, 64) then
			n.active = n.active == true
			return n
		end
	elseif kind == "du" then
		if R.DUTY_LABELS[n.duty] and Optional(n.character, 64) then
			return n
		end
	end
	return nil
end

-- Liste de contrôle et export ---------------------------------------------------------------

function R.Checklist(event)
	local items = {}
	local function add(blocking, text)
		items[#items + 1] = { blocking = blocking, text = text }
	end
	local counts = E.Counts(event)
	for _, role in ipairs(E.ROLES) do
		local c = counts[role]
		if c.target > 0 and c.confirmed < c.target then
			add(true, ("Il manque %d %s confirmé%s (%d/%d)."):format(c.target - c.confirmed, E.ROLE_LABELS[role]:lower(),
				c.target - c.confirmed > 1 and "s" or "", c.confirmed, c.target))
		end
	end
	local unplaced, tentative, confirmed, offline = 0, {}, {}, {}
	for _, row in ipairs(event.signups) do
		if row.placement == "pending" and row.response == "yes" then
			unplaced = unplaced + 1
		end
		if row.response == "tentative" then
			tentative[#tentative + 1] = row.character
		end
		if row.placement == "confirmed" then
			confirmed[row.character] = true
			if GP.UI.IsOnline(row.character) == false then
				offline[#offline + 1] = row.character
			end
		end
	end
	if unplaced > 0 then
		add(false, ("%d présent%s pas encore placé%s dans le roster."):format(unplaced, unplaced > 1 and "s" or "", unplaced > 1 and "s" or ""))
	end
	if #tentative > 0 then
		add(false, "Encore « peut-être » : " .. table.concat(tentative, ", ") .. ".")
	end
	if #offline > 0 and E.IsUpcoming(event) and event.startsAt - GP.Now() < 3600 then
		add(false, "Confirmés hors ligne : " .. table.concat(offline, ", ") .. ".")
	end
	for _, group in ipairs(R.ReserveGroups(event)) do
		if #group.rows > 1 then
			local names = {}
			for _, r in ipairs(group.rows) do
				names[#names + 1] = r.character
			end
			add(false, ("%s réservé par %d joueurs : %s."):format(GP.ItemInfo(group.itemId, group.itemName), #group.rows, table.concat(names, ", ")))
		end
	end
	for _, duty in ipairs(R.DUTIES) do
		local holder = event.duties and event.duties[duty]
		if not holder then
			add(duty == "lead", ("Personne n'est %s."):format(R.DUTY_LABELS[duty]:lower()))
		elseif not confirmed[holder] then
			add(true, ("%s est %s mais n'est plus confirmé."):format(holder, R.DUTY_LABELS[duty]:lower()))
		end
	end
	if not event.locked and #(event.reserves or {}) > 0 then
		add(false, "Réservations pas encore verrouillées.")
	end
	return items
end

-- Lignes ajoutées au texte de la soirée à coller sur Discord : soutien, réservations.
function R.NightExportLines(event)
	local lines = {}
	local duties = {}
	for _, duty in ipairs(R.DUTIES) do
		if event.duties and event.duties[duty] then
			duties[#duties + 1] = R.DUTY_LABELS[duty] .. " : " .. event.duties[duty]
		end
	end
	if #duties > 0 then
		lines[#lines + 1] = "Soutien : " .. table.concat(duties, " · ")
	end
	local groups = R.ReserveGroups(event)
	if #groups > 0 then
		lines[#lines + 1] = event.locked and "Réservations (verrouillées) :" or "Réservations :"
		for _, group in ipairs(groups) do
			local who = {}
			for _, r in ipairs(group.rows) do
				who[#who + 1] = ("%s (%s)"):format(r.character, R.PriorityLabel(r.priority):lower())
			end
			lines[#lines + 1] = ("- %s : %s"):format(GP.ItemInfo(group.itemId, group.itemName), table.concat(who, ", "))
		end
	end
	for _, h in ipairs(event.hardReserves or {}) do
		lines[#lines + 1] = ("- %s : réservé d'office%s"):format(GP.ItemInfo(h.itemId, h.itemName), h.holder and (" pour " .. h.holder) or "")
	end
	return lines
end

-- Synchro avec les joueurs connectés -----------------------------------------------------------

-- Ligne reçue d'un autre joueur (« sr », « hr », « du ») : rangée avec ce qu'il a annoncé.
local function Receive(data, sender)
	local store = E.PeerStore()
	local entry = R.CleanNight(data.t, data.n)
	if not store or not entry or entry.by ~= sender then
		return
	end
	-- Un joueur réserve pour lui ; le reste (et réserver pour un autre) : organisateur ou officier.
	local own = data.t == "sr" and entry.character == sender and not (E.Find(entry.event) or {}).locked
	if not own and not E.SenderManages(entry.event, sender) then
		return
	end
	local def = BY_KIND[data.t]
	local key = entry.event .. "|" .. def.rowKey(entry)
	local previous = store[def.peer][key]
	if not previous or entry.at > (previous.at or 0) then
		store[def.peer][key] = entry
	end
end

-- Un joueur vient de se connecter : on lui renvoie nos lignes pas encore reprises.
local function AnnouncePending()
	local me = GP.GetPlayerFullName()
	for _, def in ipairs(TABLES) do
		for _, entry in pairs(GuildkeepDB[def.db]) do
			if entry.by == me then
				GP.Comm.Broadcast({ t = def.kind, n = entry })
			end
		end
	end
end

-- Export manuel (même forme que l'upload du compagnon) -------------------------------------

local function Export(payload)
	local reserves, hardReserves, duties
	for _, r in pairs(GuildkeepDB.eventReserves) do
		local eventId, eventClientId = E.EventRef(r.event)
		reserves = reserves or {}
		reserves[#reserves + 1] = {
			eventId = eventId, eventClientId = eventClientId, character = r.character, priority = r.priority,
			itemId = r.itemId or 0, itemName = r.itemName, by = r.by, at = r.at,
		}
	end
	for _, h in pairs(GuildkeepDB.eventHardReserves) do
		local eventId, eventClientId = E.EventRef(h.event)
		hardReserves = hardReserves or {}
		hardReserves[#hardReserves + 1] = {
			eventId = eventId, eventClientId = eventClientId, itemId = h.itemId, itemName = h.itemName,
			holder = h.holder, active = h.active and true or false, by = h.by, at = h.at,
		}
	end
	for _, d in pairs(GuildkeepDB.eventDuties) do
		local eventId, eventClientId = E.EventRef(d.event)
		duties = duties or {}
		duties[#duties + 1] = { eventId = eventId, eventClientId = eventClientId, duty = d.duty, character = d.character, by = d.by, at = d.at }
	end
	payload.reserves, payload.hardReserves, payload.duties = reserves, hardReserves, duties
end

GP.Modules.Define("raid", "data", function(ctx)
	InitDB()
	ctx:Track(E.Extend({ tables = TABLES, finish = R.FinishNight }, ctx.id))
	-- Purge de nos lignes, maintenant qu'elles sont rattachées aux événements.
	E.Prune()
	for _, def in ipairs(TABLES) do
		ctx:OnPeer(def.kind, Receive)
	end
	ctx:On("ANNOUNCE_PENDING", AnnouncePending)
	ctx:On("EXPORT", Export)
end)
