local _, GP = ...

-- Rappels d'événements (module Événements, partie « reminders ») :
--   - rappel personnel : quelques minutes avant un événement auquel le joueur a
--     répondu présent ou peut-être (chat, alerte à l'écran, son) ; réglage par
--     compte (activé, nombre de minutes), une seule fois par événement ;
--   - rappel aux inscrits : l'organisateur ou un officier poste un message dans
--     le canal de guilde (clic du joueur, jamais automatique) ;
--   - service « events.upcoming » : les prochains événements, pour les autres
--     modules (lien d'une annonce), sans qu'ils touchent aux internes d'ici.

local E = GP.Events
local R = {}
E.Reminders = R

R.DEFAULT_MINUTES = 30
R.MAX_MINUTES = 24 * 60

local function Settings()
	if type(GuildkeepSettings) ~= "table" then
		GuildkeepSettings = {}
	end
	local s = GuildkeepSettings.eventReminders
	if type(s) ~= "table" then
		s = {}
		GuildkeepSettings.eventReminders = s
	end
	if type(s.enabled) ~= "boolean" then
		s.enabled = true
	end
	if type(s.minutes) ~= "number" or s.minutes < 1 or s.minutes > R.MAX_MINUTES then
		s.minutes = R.DEFAULT_MINUTES
	end
	return s
end

function R.Get()
	local s = Settings()
	return s.enabled, s.minutes
end

function R.Set(enabled, minutes)
	local s = Settings()
	s.enabled = enabled and true or false
	if type(minutes) == "number" then
		s.minutes = math.max(1, math.min(R.MAX_MINUTES, math.floor(minutes)))
	end
end

-- Rappels déjà faits : clé de l'événement → heure de début (un événement déplacé re-déclenche).
local function Done()
	GuildkeepDB.eventReminded = GuildkeepDB.eventReminded or {}
	return GuildkeepDB.eventReminded
end

local function Alert(text)
	GP.Chat(text)
	if RaidNotice_AddMessage and RaidWarningFrame and ChatTypeInfo then
		pcall(RaidNotice_AddMessage, RaidWarningFrame, text, ChatTypeInfo["RAID_WARNING"])
	end
	if PlaySound and SOUNDKIT and SOUNDKIT.RAID_WARNING then
		pcall(PlaySound, SOUNDKIT.RAID_WARNING)
	end
end

-- Vérifié toutes les 30 s ; rien en combat (l'alerte attend la sortie).
function R.Check()
	local enabled, minutes = R.Get()
	if not enabled or (InCombatLockdown and InCombatLockdown()) then
		return
	end
	local now, done = GP.Now(), Done()
	for _, event in ipairs(E.GetEvents()) do
		local mine = event.mine
		local answered = mine and (mine.response == "yes" or mine.response == "tentative")
		local left = (event.startsAt or 0) - now
		if answered and not event.cancelled and left > 0 and left <= minutes * 60 and done[event.key] ~= event.startsAt then
			done[event.key] = event.startsAt
			Alert(("|cffffd100%s|r commence dans %d min (%s)."):format(event.title, math.ceil(left / 60), date("%H:%M", event.startsAt)))
		end
	end
	-- Vieux rappels oubliés au bout d'une semaine.
	for key, startsAt in pairs(done) do
		if type(startsAt) ~= "number" or startsAt < now - 7 * 24 * 3600 then
			done[key] = nil
		end
	end
end

-- Message du rappel aux inscrits, limité à la taille d'un message de chat.
function R.GuildMessage(event)
	local names = {}
	for _, row in ipairs(event.signups or {}) do
		if (row.response == "yes" or row.response == "tentative") and row.placement ~= "bench" then
			names[#names + 1] = (row.character:match("^(%S+)")) or row.character
		end
	end
	local text = ("[Guildkeep] Rappel : %s à %s"):format(event.title, date("%H:%M", event.startsAt))
	if #names > 0 then
		text = text .. " — " .. table.concat(names, ", ")
	end
	if #text > 250 then
		text = text:sub(1, 247) .. "..."
	end
	return text
end

-- Rappel aux inscrits dans le canal de guilde : organisateur ou officier, sur clic.
function R.RemindGuild(event)
	if not E.CanManage(event) then
		return false, "seuls l'organisateur et les officiers rappellent les inscrits"
	end
	if event.cancelled or not E.IsUpcoming(event) then
		return false, "cet événement est annulé ou passé"
	end
	local send = (C_ChatInfo and C_ChatInfo.SendChatMessage) or SendChatMessage
	if not send then
		return false, "envoi de message indisponible"
	end
	pcall(send, R.GuildMessage(event), "GUILD")
	return true
end

-- Prochains événements, pour les autres modules : { key, id, title, startsAt }.
local function Upcoming()
	local list = {}
	for _, event in ipairs(E.GetEvents()) do
		if E.IsUpcoming(event) then
			list[#list + 1] = { key = event.key, id = event.id, title = event.title, startsAt = event.startsAt }
		end
	end
	return list
end

GP.Modules.Define("events", "reminders", function(ctx)
	ctx:Provide("events.upcoming", Upcoming)
	ctx:Every(30, R.Check)
	ctx:Listen({ "PLAYER_REGEN_ENABLED" }, R.Check)
	ctx:Slash({ "rappel", "reminder" }, function(argument)
		local text = strtrim(argument or ""):lower()
		local minutes = tonumber(text)
		if text == "off" or text == "non" then
			R.Set(false)
			GP.Chat("rappels d'événements désactivés.")
		elseif minutes then
			R.Set(true, minutes)
			GP.Chat(("rappel %d min avant chaque événement où tu es inscrit."):format(select(2, R.Get())))
		else
			local enabled, current = R.Get()
			GP.Chat(enabled and ("rappel %d min avant chaque événement où tu es inscrit. « /gkeep rappel off » pour arrêter."):format(current)
				or "rappels désactivés. « /gkeep rappel 30 » pour les remettre.")
		end
	end)
end)
