local _, GP = ...

-- Relevé de l'identité du personnage connecté vers GuildkeepDB (socle). Les
-- SavedVariables ne sont écrites sur disque qu'au logout ou /reload : c'est là
-- que le compagnon les lit et les envoie. Les modules relèvent le reste
-- (métiers, équipement…) sur le crochet FULL_SCAN, après l'identité.

local M = GP.Modules

local function ScanIdentity()
	local char = GP.GetLocalCharacter()
	char.classFile = select(2, UnitClass("player"))
	local _, raceFile, raceId = UnitRace("player")
	char.raceFile = raceFile
	char.raceId = raceId
	char.gender = UnitSex("player")
	char.level = UnitLevel("player")
	char.guild = GP.GetPlayerGuildName()
	-- Rang d'officier : le serveur l'exige pour gérer les événements des autres.
	char.officer = GP.IsOfficer() or nil
end

-- Personnage non éligible (hors guilde) : aucun relevé, et on efface ce qui
-- aurait été enregistré avant (reroll relevé par erreur). Les modules
-- appellent cette garde avant chacun de leurs relevés.
function GP.GateEligibility()
	if GP.IsEligibleCharacter() then
		return true
	end
	local name = GP.GetPlayerFullName()
	if GuildkeepDB and GuildkeepDB.characters and GuildkeepDB.characters[name] then
		GuildkeepDB.characters[name] = nil
		GP.RefreshUI()
	end
	return false
end

local function FullScan()
	pcall(GP.MigrateLegacyCharacterKey)
	if not GP.GateEligibility() then
		return
	end
	-- Forever vient de sortir : chaque relevé est isolé pour qu'un changement
	-- d'API ne casse que le relevé concerné.
	pcall(ScanIdentity)
	M.Fire("FULL_SCAN")
end

local frame = CreateFrame("Frame")
frame:RegisterEvent("PLAYER_LOGIN")
frame:RegisterEvent("PLAYER_GUILD_UPDATE")
frame:SetScript("OnEvent", function(_, event)
	FullScan()
	-- GetGuildInfo peut rendre nil juste au login : on re-vérifie un peu
	-- après, et PLAYER_GUILD_UPDATE couvre le reste.
	if event == "PLAYER_LOGIN" and C_Timer and C_Timer.After then
		C_Timer.After(5, FullScan)
	end
end)
