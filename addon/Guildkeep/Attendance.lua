local _, GP = ...

-- Module Présence et butin, partie données. L'addon relève lui-même, pendant le raid :
--   - qui de la guilde est dans le raid et connecté, minute par minute ;
--   - les objets épiques ou mieux reçus par ces membres (message de butin du jeu).
-- Une inscription ne vaut jamais présence. Chaque poste présent relève le
-- même raid sous la même clé « jour|instance|chef de raid » : le serveur
-- fusionne les relevés. Les corrections d'officiers restent à part.

GP.Attendance = {}
local A = GP.Attendance

A.MIN_MINUTES = 15
A.MIN_GUILD_MEMBERS = 5
A.EPIC = 4
A.STATUS_LABELS = { present = "Présent", absent = "Absent", excused = "Excusé" }
local KEEP_SECONDS = 100 * 24 * 3600
local HISTORY_SECONDS = 90 * 24 * 3600

local Now = GP.Now
local Normalize = GP.NormalizeName

function A.InitDB()
	GuildkeepDB.raidSessions = GuildkeepDB.raidSessions or {}
	GuildkeepDB.raidLoot = GuildkeepDB.raidLoot or {}
	GuildkeepDB.attendanceCorrections = GuildkeepDB.attendanceCorrections or {}
	GuildkeepDB.lootCorrections = GuildkeepDB.lootCorrections or {}
end

-- Relevé en direct ------------------------------------------------------------------------

local current -- clé du raid en cours de relevé

-- Jour du royaume (calendrier du jeu), commun à tous les postes du raid.
local function RealmDay()
	if C_DateAndTime and C_DateAndTime.GetCurrentCalendarTime then
		local ok, t = pcall(C_DateAndTime.GetCurrentCalendarTime)
		if ok and t then
			return ("%04d-%02d-%02d"):format(t.year, t.month, t.monthDay)
		end
	end
	return date("%Y-%m-%d", Now())
end

local function GuildMembers()
	local members = {}
	if GetNumGuildMembers and GetGuildRosterInfo then
		for i = 1, GetNumGuildMembers() do
			local name = GetGuildRosterInfo(i)
			if name then
				members[Normalize(name)] = true
			end
		end
	end
	members[GP.GetPlayerFullName()] = true
	return members
end

-- Membres de la guilde présents dans le raid : nom → { classFile, online }, plus le chef.
local function RaidGuildMembers()
	local guild = GuildMembers()
	local present, leader, count = {}, nil, 0
	for i = 1, GetNumGroupMembers() do
		local name, rank, _, _, _, classFile, _, online = GetRaidRosterInfo(i)
		name = Normalize(name)
		if name then
			-- Le joueur lui-même peut apparaître sous son seul prénom.
			if name == UnitName("player") then
				name = GP.GetPlayerFullName()
			end
			if rank == 2 then
				leader = name
			end
			if guild[name] then
				present[name] = { classFile = classFile, online = online }
				count = count + 1
			end
		end
	end
	return present, leader, count
end

local function InRaidInstance()
	if not IsInRaid() then
		return nil
	end
	local inInstance, kind = IsInInstance()
	if not inInstance or kind ~= "raid" then
		return nil
	end
	return (GetInstanceInfo())
end

function A.Tick()
	if not GuildkeepDB or not GuildkeepDB.raidSessions or not GP.GetPlayerGuildName() then
		return
	end
	local instance = InRaidInstance()
	if not instance then
		current = nil
		return
	end
	local present, leader, count = RaidGuildMembers()
	local session = current and GuildkeepDB.raidSessions[current]
	if not session or session.instance ~= instance then
		if count < A.MIN_GUILD_MEMBERS then
			return -- pas un raid de guilde : rien n'est relevé
		end
		local key = table.concat({ RealmDay(), instance, leader or "?" }, "|")
		session = GuildkeepDB.raidSessions[key]
		if not session then
			session = {
				key = key, instance = instance, day = RealmDay(), leader = leader, startedAt = Now(), endedAt = Now(),
				observer = GP.GetPlayerFullName(), guild = GP.GetPlayerGuildName(), participants = {}, classes = {},
			}
			GuildkeepDB.raidSessions[key] = session
		end
		current = key
	end
	for name, info in pairs(present) do
		if info.online then
			session.participants[name] = (session.participants[name] or 0) + 1
		else
			session.participants[name] = session.participants[name] or 0
		end
		session.classes[name] = info.classFile or session.classes[name]
	end
	session.endedAt = Now()
end

function A.CurrentRaid()
	return current and GuildkeepDB.raidSessions[current]
end

-- Messages de butin du jeu, traduits en motifs (« %s reçoit le butin : %s. »).
local lootPatterns

local function ToPattern(format)
	local pattern = format:gsub("([%%%(%)%.%+%-%*%?%[%]%^%$])", "%%%1")
	pattern = pattern:gsub("%%%%s", "(.+)"):gsub("%%%%d", "(%%d+)")
	return "^" .. pattern .. "$"
end

local function BuildPatterns()
	lootPatterns = {}
	local function add(global, self)
		if type(global) == "string" then
			lootPatterns[#lootPatterns + 1] = { pattern = ToPattern(global), self = self }
		end
	end
	-- Les messages avec quantité d'abord : sinon « x2 » finirait dans le lien.
	add(LOOT_ITEM_MULTIPLE, false)
	add(LOOT_ITEM_SELF_MULTIPLE, true)
	add(LOOT_ITEM, false)
	add(LOOT_ITEM_SELF, true)
end

function A.ParseLoot(message)
	if not lootPatterns then
		BuildPatterns()
	end
	for _, p in ipairs(lootPatterns) do
		local a, b = message:match(p.pattern)
		if a then
			if p.self then
				return GP.GetPlayerFullName(), a
			end
			return Normalize(a), b
		end
	end
end

local function Quality(itemId, link)
	if C_Item and C_Item.GetItemQualityByID then
		local ok, quality = pcall(C_Item.GetItemQualityByID, itemId)
		if ok and quality then
			return quality
		end
	end
	local getter = (C_Item and C_Item.GetItemInfo) or GetItemInfo
	local ok, _, _, quality = pcall(getter, link or itemId)
	if ok and quality then
		return quality
	end
	-- Couleur du lien : violet (épique) ou orange (légendaire).
	local color = link and link:match("|c(%x%x%x%x%x%x%x%x)")
	return (color == "ffa335ee" and 4) or (color == "ffff8000" and 5) or 0
end

function A.OnLoot(message)
	local session = A.CurrentRaid()
	if not session then
		return
	end
	local receiver, link = A.ParseLoot(message or "")
	local itemId = link and tonumber(link:match("item:(%d+)"))
	if not receiver or not itemId or not session.participants[receiver] then
		return
	end
	local quality = Quality(itemId, link)
	if quality < A.EPIC then
		return
	end
	local ordinal = 1
	while GuildkeepDB.raidLoot[table.concat({ session.key, receiver, itemId, ordinal }, "|")] do
		ordinal = ordinal + 1
	end
	GuildkeepDB.raidLoot[table.concat({ session.key, receiver, itemId, ordinal }, "|")] = {
		raid = session.key, character = receiver, itemId = itemId, itemName = link:match("%[(.-)%]"),
		quality = quality, ordinal = ordinal, at = Now(), observer = GP.GetPlayerFullName(),
	}
	GP.RefreshUI()
end

-- Corrections d'officiers --------------------------------------------------------------------

local NextAt = GP.NextAt

function A.Correct(raid, character, status, note)
	if not GP.IsOfficer() then
		return nil, "réservé aux officiers"
	end
	if not A.STATUS_LABELS[status] then
		return nil, "statut inconnu"
	end
	local previous = raid.corrections[character]
	GuildkeepDB.attendanceCorrections[raid.key .. "|" .. character] = {
		raid = raid.key, character = character, status = status,
		note = note and strtrim(note) ~= "" and strtrim(note):sub(1, 120) or nil,
		by = GP.GetPlayerFullName(), at = NextAt(previous and previous.at),
	}
	GP.RefreshUI()
	return true
end

function A.SetVoid(raid, loot, void, note)
	if not GP.IsOfficer() then
		return nil, "réservé aux officiers"
	end
	GuildkeepDB.lootCorrections[table.concat({ raid.key, loot.character, loot.itemId, loot.ordinal }, "|")] = {
		raid = raid.key, character = loot.character, itemId = loot.itemId, ordinal = loot.ordinal,
		void = void and true or false, note = note, by = GP.GetPlayerFullName(), at = NextAt(loot.voidAt),
	}
	GP.RefreshUI()
	return true
end

-- Fusion serveur + relevés locaux -------------------------------------------------------------

local function LootKey(l)
	return table.concat({ l.character, l.itemId, l.ordinal or 1 }, "|")
end

-- Raids de la guilde affichée, du plus récent au plus ancien. Chaque raid :
-- participants (nom → { minutes, classFile }), loot (liste), corrections (nom → {...}).
function A.GetRaids()
	local byKey, list = {}, {}
	local function ensure(key, src)
		local raid = byKey[key]
		if not raid then
			raid = {
				key = key, id = src.id, instance = src.instance, day = src.day, leader = src.leader,
				startedAt = src.startedAt or 0, endedAt = src.endedAt or 0, eventId = src.eventId,
				participants = {}, lootByKey = {}, corrections = {},
			}
			byKey[key] = raid
			list[#list + 1] = raid
		end
		return raid
	end

	for _, src in ipairs(GP.GetServerData().raids or {}) do
		local raid = ensure(src.key, src)
		for _, p in ipairs(src.participants or {}) do
			raid.participants[p.character] = { minutes = p.minutes or 0, classFile = p.classFile }
		end
		for _, l in ipairs(src.loot or {}) do
			local copy = {}
			for k, v in pairs(l) do
				copy[k] = v
			end
			copy.voidAt = copy.voidAt or 0
			raid.lootByKey[LootKey(copy)] = copy
		end
		for _, c in ipairs(src.corrections or {}) do
			raid.corrections[c.character] = c
		end
	end

	-- Relevés de ce poste (ceux de la guilde affichée).
	local guildName = GP.GetPlayerGuildName()
	for key, session in pairs(GuildkeepDB.raidSessions) do
		if session.guild == guildName then
			local raid = ensure(key, session)
			raid.startedAt = math.min(raid.startedAt > 0 and raid.startedAt or session.startedAt, session.startedAt)
			raid.endedAt = math.max(raid.endedAt, session.endedAt)
			for name, minutes in pairs(session.participants) do
				local p = raid.participants[name] or { minutes = 0 }
				p.minutes = math.max(p.minutes, minutes)
				p.classFile = p.classFile or session.classes[name]
				raid.participants[name] = p
			end
			raid.pendingSync = raid.id == nil
		end
	end
	for _, l in pairs(GuildkeepDB.raidLoot) do
		local raid = byKey[l.raid]
		if raid and not raid.lootByKey[LootKey(l)] then
			local copy = { voidAt = 0, voided = false, pendingSync = true }
			for k, v in pairs(l) do
				copy[k] = v
			end
			raid.lootByKey[LootKey(copy)] = copy
		end
	end
	for _, c in pairs(GuildkeepDB.attendanceCorrections) do
		local raid = byKey[c.raid]
		local known = raid and raid.corrections[c.character]
		if raid and (not known or (c.at or 0) > (known.at or 0)) then
			raid.corrections[c.character] = c
		end
	end
	for _, c in pairs(GuildkeepDB.lootCorrections) do
		local raid = byKey[c.raid]
		local loot = raid and raid.lootByKey[LootKey(c)]
		if loot and (c.at or 0) > (loot.voidAt or 0) then
			loot.voided, loot.voidNote, loot.voidBy, loot.voidAt = c.void, c.note, c.by, c.at
		end
	end

	local since = Now() - HISTORY_SECONDS
	local result = {}
	for _, raid in ipairs(list) do
		if raid.startedAt >= since then
			raid.loot = {}
			for _, l in pairs(raid.lootByKey) do
				raid.loot[#raid.loot + 1] = l
			end
			table.sort(raid.loot, function(a, b) return (a.at or 0) < (b.at or 0) end)
			raid.count = 0
			for _ in pairs(raid.participants) do
				raid.count = raid.count + 1
			end
			result[#result + 1] = raid
		end
	end
	table.sort(result, function(a, b) return a.startedAt > b.startedAt end)
	return result
end

-- Taux de présence (mêmes règles que le serveur) ---------------------------------------------

function A.IsGuildRaid(raid)
	return raid.count >= A.MIN_GUILD_MEMBERS
end

-- true (présent), false (absent) ou nil (excusé : hors calcul).
function A.Attended(raid, character)
	local correction = raid.corrections[character]
	local status = correction and correction.status
	if status == "present" then
		return true
	elseif status == "absent" then
		return false
	elseif status == "excused" then
		return nil
	end
	local p = raid.participants[character]
	return p ~= nil and p.minutes >= A.MIN_MINUTES
end

function A.Rate(raids, character, from, to)
	local attended, eligible = 0, 0
	for _, raid in ipairs(raids) do
		if A.IsGuildRaid(raid) and raid.startedAt >= from and raid.startedAt < to then
			local state = A.Attended(raid, character)
			if state ~= nil then
				eligible = eligible + 1
				attended = attended + (state and 1 or 0)
			end
		end
	end
	return { attended = attended, eligible = eligible, percent = eligible > 0 and (100 * attended / eligible) or nil }
end

function A.Stats(raids, character)
	local now = Now()
	local function ago(days)
		return now - days * 86400
	end
	local loot = 0
	for _, raid in ipairs(raids) do
		for _, l in ipairs(raid.loot) do
			if l.character == character and not l.voided then
				loot = loot + 1
			end
		end
	end
	return {
		character = character,
		d30 = A.Rate(raids, character, ago(30), now + 1),
		d60 = A.Rate(raids, character, ago(60), now + 1),
		d90 = A.Rate(raids, character, ago(90), now + 1),
		previous30 = A.Rate(raids, character, ago(60), ago(30)),
		loot = loot,
	}
end

-- Tous les joueurs vus en raid (participants et corrections), avec leurs taux.
function A.Summary(raids)
	local names, classes = {}, {}
	for _, raid in ipairs(raids) do
		for name, p in pairs(raid.participants) do
			names[name] = true
			classes[name] = classes[name] or p.classFile
		end
		for name in pairs(raid.corrections) do
			names[name] = true
		end
	end
	local result = {}
	for name in pairs(names) do
		local stats = A.Stats(raids, name)
		stats.classFile = classes[name]
		result[#result + 1] = stats
	end
	return result
end

-- Purge et export ---------------------------------------------------------------------------

function A.Prune()
	local serverRaids, serverLoot, serverCorrections, serverVoids = {}, {}, {}, {}
	for _, section in ipairs(GP.AllServerSections()) do
		for _, raid in ipairs(section.raids or {}) do
			serverRaids[raid.key] = raid
			for _, l in ipairs(raid.loot or {}) do
				serverLoot[raid.key .. "|" .. LootKey(l)] = true
				serverVoids[raid.key .. "|" .. LootKey(l)] = l.voidAt or 0
			end
			for _, c in ipairs(raid.corrections or {}) do
				serverCorrections[raid.key .. "|" .. c.character] = c.at or 0
			end
		end
	end
	local oldest = Now() - KEEP_SECONDS
	for key, session in pairs(GuildkeepDB.raidSessions) do
		local server = serverRaids[key]
		-- Raid terminé depuis une heure et repris par le serveur : son relevé est complet là-bas.
		local done = server and key ~= current and session.endedAt < Now() - 3600 and (server.endedAt or 0) >= session.endedAt
		if done or session.endedAt < oldest then
			GuildkeepDB.raidSessions[key] = nil
		end
	end
	for key, l in pairs(GuildkeepDB.raidLoot) do
		if serverLoot[l.raid .. "|" .. LootKey(l)] or (l.at or 0) < oldest then
			GuildkeepDB.raidLoot[key] = nil
		end
	end
	for key, c in pairs(GuildkeepDB.attendanceCorrections) do
		if (serverCorrections[key] or -1) >= (c.at or 0) or (c.at or 0) < oldest then
			GuildkeepDB.attendanceCorrections[key] = nil
		end
	end
	for key, c in pairs(GuildkeepDB.lootCorrections) do
		if (serverVoids[key] or -1) >= (c.at or 0) or (c.at or 0) < oldest then
			GuildkeepDB.lootCorrections[key] = nil
		end
	end
end

-- Même forme que l'upload du compagnon (UploadRaid, UploadRaidLoot…).
function A.BuildExport()
	local raids, loot, corrections, voids
	for _, s in pairs(GuildkeepDB.raidSessions) do
		raids = raids or {}
		raids[#raids + 1] = {
			key = s.key, instance = s.instance, day = s.day, leader = s.leader, startedAt = s.startedAt,
			endedAt = s.endedAt, observer = s.observer, participants = s.participants, classes = s.classes,
		}
	end
	for _, l in pairs(GuildkeepDB.raidLoot) do
		loot = loot or {}
		loot[#loot + 1] = l
	end
	for _, c in pairs(GuildkeepDB.attendanceCorrections) do
		corrections = corrections or {}
		corrections[#corrections + 1] = c
	end
	for _, c in pairs(GuildkeepDB.lootCorrections) do
		voids = voids or {}
		voids[#voids + 1] = c
	end
	return raids, loot, corrections, voids
end

-- Chargement du module -----------------------------------------------------------------------

local function Export(payload)
	payload.raids, payload.loot, payload.attendanceCorrections, payload.lootCorrections = A.BuildExport()
end

GP.Modules.Define("attendance", "data", function(ctx)
	A.InitDB()
	A.Prune()
	ctx:On("EXPORT", Export)
	ctx:Listen({ "CHAT_MSG_LOOT" }, function(_, message)
		A.OnLoot(message)
	end)
	-- Une minute de présence par tic.
	ctx:Every(60, A.Tick)
	-- Entrée ou changement de raid : on ouvre le relevé sans attendre le tic.
	ctx:Listen({ "PLAYER_ENTERING_WORLD", "GROUP_ROSTER_UPDATE", "ZONE_CHANGED_NEW_AREA" }, function()
		if not A.CurrentRaid() then
			A.Tick()
		end
	end)
end)
