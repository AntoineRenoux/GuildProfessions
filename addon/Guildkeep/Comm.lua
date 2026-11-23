local _, GP = ...

-- Communication entre joueurs connectés de la guilde (socle).
--
-- Deux canaux, tous deux en messages d'addon sur le canal de guilde :
--   - un préfixe propre à un module (ex. GPROF pour les commandes de craft),
--     au format libre : le module reçoit le texte brut ;
--   - le canal partagé GKEEPEV : des tables JSON découpées en morceaux de 200
--     octets (limite de 255) et envoyées au fil de l'eau pour respecter la
--     limite de débit du client. Chaque table porte un type `t` ; le module
--     abonné à ce type la reçoit. Sans C_EncodingUtil, pas de canal partagé :
--     les données passent quand même par le serveur.
-- Un message n'est remis qu'aux modules actifs.

local M = GP.Modules
GP.Comm = {}
local C = GP.Comm

local function RegisterPrefix(prefix)
	if C_ChatInfo and C_ChatInfo.RegisterAddonMessagePrefix then
		pcall(C_ChatInfo.RegisterAddonMessagePrefix, prefix)
	end
end

local function Remover(list, key, entry)
	return function()
		if list[key] == entry then
			list[key] = nil
		end
	end
end

-- Préfixes propres aux modules ---------------------------------------------------------

local prefixHandlers = {}

function C.OnPrefix(prefix, handler, owner)
	local entry = { fn = handler, owner = owner }
	prefixHandlers[prefix] = entry
	RegisterPrefix(prefix)
	return Remover(prefixHandlers, prefix, entry)
end

-- Envoi d'un texte brut sur le canal de guilde (rien hors guilde).
function C.Send(prefix, message)
	if not IsInGuild() or not (C_ChatInfo and C_ChatInfo.SendAddonMessage) then
		return
	end
	pcall(C_ChatInfo.SendAddonMessage, prefix, message, "GUILD")
end

-- Canal partagé (JSON) ------------------------------------------------------------------

local PREFIX = "GKEEPEV"
local CHUNK = 200 -- avec l'en-tête du morceau, on reste loin des 255 octets permis
local MAX_QUEUE = 400 -- morceaux en attente au plus : au-delà, les plus anciens messages tombent
local MAX_TRIES = 5

local peerHandlers = {}
local queue, sending = {}, false
local inbox = {}
local messageCounter = 0

function C.OnPeer(kind, handler, owner)
	local entry = { fn = handler, owner = owner }
	peerHandlers[kind] = entry
	RegisterPrefix(PREFIX)
	return Remover(peerHandlers, kind, entry)
end

-- Au moins un module actif écoute-t-il le canal partagé ?
local function HasPeerListeners()
	for _, entry in pairs(peerHandlers) do
		if M.IsActive(entry.owner) then
			return true
		end
	end
	return false
end

local function Sent(ok, result)
	if not ok then
		return false
	end
	-- Anciens clients : true/false ; clients récents : un Enum.SendAddonMessageResult (0 = succès).
	if result == nil or result == true then
		return true
	end
	local success = Enum and Enum.SendAddonMessageResult and Enum.SendAddonMessageResult.Success or 0
	return result == success
end

local function SendNext()
	local item = queue[1]
	if not item then
		sending = false
		return
	end
	local ok, result = pcall(C_ChatInfo.SendAddonMessage, PREFIX, item.text, "GUILD")
	if Sent(ok, result) then
		table.remove(queue, 1)
		C_Timer.After(0.3, SendNext)
		return
	end
	-- Débit dépassé ou échec passager : on réessaie un peu plus tard, puis on abandonne le morceau.
	item.tries = item.tries + 1
	if item.tries >= MAX_TRIES then
		table.remove(queue, 1)
	end
	C_Timer.After(1.5 * item.tries, SendNext)
end

function C.Broadcast(data)
	if not IsInGuild() or not (C_ChatInfo and C_ChatInfo.SendAddonMessage) or not GP.SerializeAvailable() then
		return
	end
	local ok, json = pcall(C_EncodingUtil.SerializeJSON, data)
	if not ok or not json then
		return
	end
	messageCounter = (messageCounter + 1) % 1000
	local id = ("%d%03d"):format(GP.Now() % 100000, messageCounter)
	local total = math.ceil(#json / CHUNK)
	if total > 40 then
		return -- message démesuré : les autres l'ignoreraient de toute façon
	end
	for i = 1, total do
		queue[#queue + 1] = { text = table.concat({ id, i, total, json:sub((i - 1) * CHUNK + 1, i * CHUNK) }, "\t"), tries = 0 }
	end
	while #queue > MAX_QUEUE do
		table.remove(queue, 1)
	end
	if not sending then
		sending = true
		SendNext()
	end
end

-- Un joueur vient de se connecter : chacun lui renvoie ses propres changements
-- pas encore repris par le serveur (crochet ANNOUNCE_PENDING des modules), avec
-- un délai aléatoire pour ne pas tous parler en même temps, et au plus une
-- réponse par minute quel que soit le nombre de demandes.
local lastReqAnswer = 0

local function AnswerRequest()
	if GetTime() - lastReqAnswer > 60 then
		lastReqAnswer = GetTime()
		C_Timer.After(2 + math.random() * 8, function()
			M.Fire("ANNOUNCE_PENDING")
		end)
	end
end

local function Receive(data, sender)
	if type(data) ~= "table" or type(data.t) ~= "string" then
		return
	end
	if data.t == "req" then
		AnswerRequest()
		return
	end
	local entry = peerHandlers[data.t]
	if entry and M.IsActive(entry.owner) then
		M.Protect(entry.fn, data, sender)
		GP.RefreshUI()
	end
end

local function OnChunk(message, sender)
	local id, index, total, part = strsplit("\t", message, 4)
	index, total = tonumber(index), tonumber(total)
	if not id or not index or not total or total > 40 or not part then
		return
	end
	local key = sender .. ":" .. id
	local entry = inbox[key] or { parts = {}, count = 0, started = GetTime() }
	inbox[key] = entry
	if not entry.parts[index] then
		entry.parts[index] = part
		entry.count = entry.count + 1
	end
	if entry.count < total then
		return
	end
	inbox[key] = nil
	local ok, data = pcall(C_EncodingUtil.DeserializeJSON, table.concat(entry.parts))
	if ok then
		Receive(data, sender)
	end
	-- Messages incomplets abandonnés (joueur déconnecté en cours d'envoi).
	for k, other in pairs(inbox) do
		if GetTime() - other.started > 120 then
			inbox[k] = nil
		end
	end
end

-- Réception ----------------------------------------------------------------------------------

local frame = CreateFrame("Frame")
frame:RegisterEvent("PLAYER_LOGIN")
frame:RegisterEvent("CHAT_MSG_ADDON")
frame:SetScript("OnEvent", function(_, event, prefix, message, _, sender)
	if event == "PLAYER_LOGIN" then
		-- Laisse le temps à la guilde d'être connue, puis demande les nouveautés.
		C_Timer.After(12, function()
			if IsInGuild() and HasPeerListeners() then
				C.Broadcast({ t = "req" })
			end
		end)
		return
	end
	-- Sur Forever, l'expéditeur arrive en « Prénom-Nom » (le nom de famille
	-- occupe le slot royaume) : on le renormalise en nom complet.
	local senderFull = GP.NormalizeName(sender or "")
	if senderFull == GP.GetPlayerFullName() then
		return
	end
	if prefix == PREFIX then
		if HasPeerListeners() and GP.SerializeAvailable() then
			M.Protect(OnChunk, message, senderFull)
		end
		return
	end
	local entry = prefixHandlers[prefix]
	if entry and M.IsActive(entry.owner) then
		M.Protect(entry.fn, message, senderFull)
	end
end)
