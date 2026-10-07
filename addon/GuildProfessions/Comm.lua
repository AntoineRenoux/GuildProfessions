local _, GP = ...

-- Notifications temps réel entre joueurs connectés via addon messages (canal
-- guilde). Ce n'est qu'un signal immédiat : la persistance passe toujours par
-- SavedVariables -> compagnon -> serveur.

local PREFIX = "GPROF"

GP.Comm = {}

local function PlayerName()
	return UnitName("player")
end

local function Send(message)
	if not IsInGuild() then
		return
	end
	if C_ChatInfo and C_ChatInfo.SendAddonMessage then
		pcall(C_ChatInfo.SendAddonMessage, PREFIX, message, "GUILD")
	end
end

function GP.Comm.SendNewOrder(order)
	Send(table.concat({ "NEW", order.crafter, order.requester, order.item, tostring(order.qty or 1) }, "\t"))
end

function GP.Comm.SendStatus(order, status)
	Send(table.concat({ "UPD", order.requester, order.item, status, PlayerName() }, "\t"))
end

local STATUS_LABELS = {
	accepted = "acceptée",
	done = "terminée",
	cancelled = "annulée",
}

local function Toast(text)
	print("|cff33ff99GuildProfessions|r — " .. text)
	if RaidNotice_AddMessage and RaidWarningFrame then
		pcall(RaidNotice_AddMessage, RaidWarningFrame, text, ChatTypeInfo["RAID_WARNING"])
	end
	if PlaySound and SOUNDKIT and SOUNDKIT.RAID_WARNING then
		pcall(PlaySound, SOUNDKIT.RAID_WARNING)
	end
end

local function OnMessage(message, sender)
	local kind, a, b, c, d = strsplit("\t", message)
	if kind == "NEW" and a == PlayerName() then
		-- a=crafter, b=requester, c=item, d=qty
		Toast(("Nouvelle commande de %s : %s× %s — /gp pour la voir."):format(b or sender, d or "1", c or "?"))
		GP.RefreshUI()
	elseif kind == "UPD" and a == PlayerName() then
		-- a=requester, b=item, c=status, d=auteur
		Toast(("Ta commande « %s » est %s (%s)."):format(b or "?", STATUS_LABELS[c] or c or "?", d or sender))
		GP.RefreshUI()
	end
end

local frame = CreateFrame("Frame")
frame:RegisterEvent("PLAYER_LOGIN")
frame:RegisterEvent("CHAT_MSG_ADDON")
frame:SetScript("OnEvent", function(_, event, prefix, message, _, sender)
	if event == "PLAYER_LOGIN" then
		if C_ChatInfo and C_ChatInfo.RegisterAddonMessagePrefix then
			pcall(C_ChatInfo.RegisterAddonMessagePrefix, PREFIX)
		end
	elseif event == "CHAT_MSG_ADDON" and prefix == PREFIX then
		local shortSender = strsplit("-", sender or "")
		if shortSender ~= PlayerName() then
			pcall(OnMessage, message, shortSender)
		end
	end
end)
