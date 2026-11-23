local _, GP = ...

-- Module Quêtes, partie données. Quêtes partagées : les quêtes élites, de donjon et de raid du journal de
-- chaque membre, pour trouver qui les fait aussi. Relevées à chaque
-- changement du journal ; une quête rendue disparaît au relevé suivant.
-- Diffusées aussitôt aux joueurs connectés (canal partagé du socle, type
-- « qs »), puis envoyées au serveur avec le reste (characters[nom].quests).

GP.Quests = {}
local Q = GP.Quests

Q.KIND_LABELS = { elite = "Élites", dungeon = "Donjons", raid = "Raids" }
local MAX_QUESTS = 25
local PEER_FRESH_SECONDS = 12 * 3600 -- un relevé reçu d'un joueur connecté reste plus frais que le serveur pendant ce temps

-- Types de quête du jeu (Enum.QuestTag) : donjon, héroïque, raids, groupe (élite).
local DUNGEON_TAGS = { [81] = true, [85] = true }
local RAID_TAGS = { [62] = true, [88] = true, [89] = true }
local GROUP_TAG = 1

local function Kind(questId, suggestedGroup)
	local tagId, tagName
	if C_QuestLog and C_QuestLog.GetQuestTagInfo then
		local ok, info = pcall(C_QuestLog.GetQuestTagInfo, questId)
		if ok and type(info) == "table" then
			tagId, tagName = info.tagID, info.tagName
		end
	elseif GetQuestTagInfo then
		local ok, id, name = pcall(GetQuestTagInfo, questId)
		if ok then
			tagId, tagName = id, name
		end
	end
	if tagId and DUNGEON_TAGS[tagId] then
		return "dungeon"
	elseif tagId and RAID_TAGS[tagId] then
		return "raid"
	elseif tagId == GROUP_TAG or (suggestedGroup or 0) > 1 or (tagName and (tagName:find("lite") or tagName:find("Élite"))) then
		return "elite"
	end
end

local function IsComplete(questId, fallback)
	if C_QuestLog and C_QuestLog.IsComplete then
		local ok, complete = pcall(C_QuestLog.IsComplete, questId)
		if ok then
			return complete and true or false
		end
	end
	return fallback and true or false
end

-- Lecture du journal : liste des quêtes partageables, rangées sous l'en-tête courant (zone ou donjon).
function Q.ReadLog()
	local quests, location = {}, nil
	if C_QuestLog and C_QuestLog.GetNumQuestLogEntries and C_QuestLog.GetInfo then
		for i = 1, C_QuestLog.GetNumQuestLogEntries() do
			local info = C_QuestLog.GetInfo(i)
			if info then
				if info.isHeader then
					location = info.title
				elseif info.questID and not info.isHidden then
					local kind = Kind(info.questID, info.suggestedGroup)
					if kind then
						quests[#quests + 1] = {
							id = info.questID, title = info.title, kind = kind, location = location,
							level = info.level, complete = IsComplete(info.questID),
						}
					end
				end
			end
		end
	elseif GetNumQuestLogEntries and GetQuestLogTitle then
		for i = 1, GetNumQuestLogEntries() do
			local title, level, suggestedGroup, isHeader, _, isComplete, _, questId = GetQuestLogTitle(i)
			if isHeader then
				location = title
			elseif questId then
				local kind = Kind(questId, suggestedGroup)
				if kind then
					quests[#quests + 1] = {
						id = questId, title = title, kind = kind, location = location, level = level,
						complete = IsComplete(questId, isComplete == 1 or isComplete == true),
					}
				end
			end
		end
	end
	table.sort(quests, function(a, b) return a.id < b.id end)
	while #quests > MAX_QUESTS do
		table.remove(quests)
	end
	return quests
end

local function Signature(quests)
	local parts = {}
	for _, q in ipairs(quests) do
		parts[#parts + 1] = q.id .. (q.complete and "+" or "")
	end
	return table.concat(parts, ",")
end

local broadcastPending = false

function Q.Announce()
	local char = GuildkeepDB and GuildkeepDB.characters[GP.GetPlayerFullName()]
	if not char or not char.quests then
		return
	end
	local list = {}
	for _, q in ipairs(char.quests) do
		list[#list + 1] = { i = q.id, t = q.title, k = q.kind, l = q.location, v = q.level, d = q.complete }
	end
	GP.Comm.Broadcast({ t = "qs", c = GP.GetPlayerFullName(), at = GP.Now(), q = list })
end

function Q.Scan()
	if not GuildkeepDB or not GP.IsEligibleCharacter() then
		return
	end
	local quests = Q.ReadLog()
	local char = GP.GetLocalCharacter()
	local changed = Signature(quests) ~= Signature(char.quests or {})
	char.quests = quests
	if changed then
		GP.RefreshUI()
		-- Plusieurs changements d'affilée (objectifs) : une seule annonce, un peu plus tard.
		if not broadcastPending then
			broadcastPending = true
			C_Timer.After(15, function()
				broadcastPending = false
				Q.Announce()
			end)
		end
	end
end

-- Relevé reçu d'un autre joueur connecté.
function Q.ReceivePeer(data, sender)
	if data.c ~= sender or type(data.q) ~= "table" or type(data.at) ~= "number" or #data.q > MAX_QUESTS then
		return
	end
	local quests = {}
	for _, q in ipairs(data.q) do
		if type(q) == "table" and type(q.i) == "number" and type(q.t) == "string" and #q.t <= 100
			and (q.k == "elite" or q.k == "dungeon" or q.k == "raid") then
			quests[#quests + 1] = {
				id = q.i, title = q.t, kind = q.k, level = tonumber(q.v), complete = q.d == true,
				location = type(q.l) == "string" and q.l:sub(1, 60) or nil,
			}
		end
	end
	local guild = GP.GetPlayerGuildName()
	if not guild then
		return
	end
	GuildkeepDB.questPeer = GuildkeepDB.questPeer or {}
	local key = strtrim(guild):lower()
	GuildkeepDB.questPeer[key] = GuildkeepDB.questPeer[key] or {}
	local previous = GuildkeepDB.questPeer[key][sender]
	if not previous or data.at > previous.at then
		GuildkeepDB.questPeer[key][sender] = { at = data.at, quests = quests }
	end
	GP.RefreshUI()
end

-- Vue fusionnée --------------------------------------------------------------------------

-- Quêtes de la guilde affichée : id → { id, title, kind, location, level, holders = { nom → { complete, classFile } } }.
function Q.GetAll()
	local byId = {}
	local holdersOf = {} -- nom → liste de quêtes retenue (la source la plus fraîche)
	local classes = {}
	for _, member in ipairs(GP.GetServerData().characters or {}) do
		if member.quests then
			holdersOf[member.name] = member.quests
		end
		classes[member.name] = member.classFile
	end
	local guild = GP.GetPlayerGuildName()
	local peer = guild and GuildkeepDB.questPeer and GuildkeepDB.questPeer[strtrim(guild):lower()] or {}
	local fresh = GP.Now() - PEER_FRESH_SECONDS
	for name, entry in pairs(peer) do
		if entry.at >= fresh then
			holdersOf[name] = entry.quests
		end
	end
	for name, char in pairs(GuildkeepDB.characters) do
		if GP.IsInCurrentGuild(name) and char.quests then
			holdersOf[name] = char.quests
			classes[name] = char.classFile or classes[name]
		end
	end
	for name, quests in pairs(holdersOf) do
		for _, q in ipairs(quests) do
			-- Relevé du serveur non contrôlé : une quête mal formée ou d'un type
			-- inconnu de ce client est ignorée.
			local valid = type(q) == "table" and type(q.id) == "number" and type(q.title) == "string" and Q.KIND_LABELS[q.kind]
			local quest = valid and byId[q.id]
			if valid and not quest then
				quest = { id = q.id, title = q.title, kind = q.kind, location = type(q.location) == "string" and q.location or nil,
					level = tonumber(q.level), holders = {} }
				byId[q.id] = quest
			end
			if quest then
				quest.holders[name] = { complete = q.complete, classFile = classes[name] }
			end
		end
	end
	return byId
end

-- Le personnage connecté a-t-il déjà rendu cette quête ? (le jeu le sait)
function Q.TurnedIn(questId)
	if C_QuestLog and C_QuestLog.IsQuestFlaggedCompleted then
		local ok, done = pcall(C_QuestLog.IsQuestFlaggedCompleted, questId)
		return ok and done or false
	end
	return false
end

-- Partage au groupe : la quête doit être dans le journal du joueur, et partageable.
function Q.Share(questId)
	if not IsInGroup() then
		return false, "il faut être en groupe pour partager une quête"
	end
	if C_QuestLog and C_QuestLog.GetLogIndexForQuestID and not C_QuestLog.GetLogIndexForQuestID(questId) then
		return false, "cette quête n'est pas dans ton journal"
	end
	if C_QuestLog and C_QuestLog.IsPushableQuest and not C_QuestLog.IsPushableQuest(questId) then
		return false, "cette quête ne se partage pas"
	end
	if C_QuestLog and C_QuestLog.SetSelectedQuest then
		C_QuestLog.SetSelectedQuest(questId)
	end
	if QuestLogPushQuest then
		QuestLogPushQuest()
		return true
	end
	return false, "partage indisponible sur ce client"
end

-- Chargement du module -----------------------------------------------------------------------

GP.Modules.Define("quests", "data", function(ctx)
	ctx:OnPeer("qs", Q.ReceivePeer)
	ctx:On("ANNOUNCE_PENDING", Q.Announce)
	-- Le journal envoie des rafales d'événements : un seul relevé deux secondes après.
	local scanPending = false
	ctx:Listen({ "PLAYER_LOGIN", "QUEST_LOG_UPDATE", "QUEST_TURNED_IN", "QUEST_ACCEPTED", "QUEST_REMOVED" }, function()
		if scanPending then
			return
		end
		scanPending = true
		ctx:After(2, function()
			scanPending = false
			Q.Scan()
		end)
	end)
end)
