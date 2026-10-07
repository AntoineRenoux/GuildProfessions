local _, GP = ...

-- Scanne le personnage local vers GuildProfessionsDB. Les SavedVariables ne
-- sont écrites sur disque qu'au logout//reload : c'est là que le compagnon
-- les lit et les uploade.

local function Now()
	return date("%Y-%m-%d %H:%M")
end

local function ScanIdentity()
	local char = GP.GetLocalCharacter()
	char.classFile = select(2, UnitClass("player"))
	local _, raceFile, raceId = UnitRace("player")
	char.raceFile = raceFile
	char.raceId = raceId
	char.gender = UnitSex("player")
	char.level = UnitLevel("player")
	char.guild = GP.GetPlayerGuildName()
end

-- Personnage non éligible (hors guilde / autre guilde) : aucun scan, et on
-- efface ce qui aurait été enregistré avant (reroll scanné par erreur).
local function GateEligibility()
	if GP.IsEligibleCharacter() then
		return true
	end
	local name = GP.GetPlayerFullName()
	if GuildProfessionsDB.characters and GuildProfessionsDB.characters[name] then
		GuildProfessionsDB.characters[name] = nil
		GP.RefreshUI()
	end
	return false
end

local function ScanProfessions()
	if not (GetProfessions and GetProfessionInfo) then
		return
	end
	local char = GP.GetLocalCharacter()
	char.professions = char.professions or {}
	-- GetProfessions peut rendre des trous (nil) : pairs sur le constructeur les saute.
	local prof1, prof2, arch, fishing, cooking, firstAid = GetProfessions()
	local seen = {}
	for _, index in pairs({ prof1, prof2, arch, fishing, cooking, firstAid }) do
		local name, _, level, max = GetProfessionInfo(index)
		if name then
			seen[name] = true
			local prof = char.professions[name] or {}
			prof.level = level
			prof.max = max
			prof.scannedAt = Now()
			char.professions[name] = prof
		end
	end
	-- Métier désappris : on retire l'entrée locale.
	for name in pairs(char.professions) do
		if not seen[name] then
			char.professions[name] = nil
		end
	end
	GP.RefreshUI()
end

local function CurrentTradeSkillName()
	if not C_TradeSkillUI then
		return nil
	end
	if C_TradeSkillUI.GetBaseProfessionInfo then
		local info = C_TradeSkillUI.GetBaseProfessionInfo()
		if info then
			return info.professionName or info.parentProfessionName or info.name
		end
	end
	if C_TradeSkillUI.GetTradeSkillLine then
		local _, name = C_TradeSkillUI.GetTradeSkillLine()
		return name
	end
	return nil
end

local function ScanRecipes()
	if not (C_TradeSkillUI and C_TradeSkillUI.GetAllRecipeIDs and C_TradeSkillUI.GetRecipeInfo) then
		return
	end
	local profName = CurrentTradeSkillName()
	if not profName or profName == "" then
		return
	end
	local ids = C_TradeSkillUI.GetAllRecipeIDs()
	if not ids or #ids == 0 then
		return
	end
	local learned = {}
	for _, id in ipairs(ids) do
		local info = C_TradeSkillUI.GetRecipeInfo(id)
		if info and info.learned then
			learned[#learned + 1] = id
		end
	end
	table.sort(learned)
	-- Résolution eager des noms : alimente le cache persistant pour l'upload.
	for _, id in ipairs(learned) do
		GP.GetRecipeName(id)
	end
	local char = GP.GetLocalCharacter()
	char.professions = char.professions or {}
	local prof = char.professions[profName] or {}
	prof.recipes = learned
	prof.scannedAt = Now()
	char.professions[profName] = prof
	GP.RefreshUI()
end

local function ScanEquipment()
	local char = GP.GetLocalCharacter()
	local equipment = {}
	for slot = 1, 19 do
		local link = GetInventoryItemLink("player", slot)
		if link then
			-- Clés en chaîne : sérialisation JSON stable (pas de table mixte).
			equipment[tostring(slot)] = link
		end
	end
	char.equipment = equipment
	GP.RefreshUI()
end

local function FullScan()
	pcall(GP.MigrateLegacyCharacterKey)
	if not GateEligibility() then
		return
	end
	pcall(ScanIdentity)
	pcall(ScanProfessions)
	pcall(ScanEquipment)
end

local frame = CreateFrame("Frame")
frame:RegisterEvent("PLAYER_LOGIN")
frame:RegisterEvent("PLAYER_GUILD_UPDATE")
frame:RegisterEvent("SKILL_LINES_CHANGED")
frame:RegisterEvent("TRADE_SKILL_LIST_UPDATE")
frame:RegisterEvent("PLAYER_EQUIPMENT_CHANGED")
frame:SetScript("OnEvent", function(_, event)
	-- Forever vient de sortir : on isole chaque scan pour qu'un changement
	-- d'API ne casse pas tout l'addon, juste le scan concerné.
	if event == "PLAYER_LOGIN" then
		-- GetGuildInfo peut rendre nil juste au login : on re-vérifie un peu
		-- après, et PLAYER_GUILD_UPDATE couvre le reste.
		FullScan()
		if C_Timer and C_Timer.After then
			C_Timer.After(5, FullScan)
		end
	elseif event == "PLAYER_GUILD_UPDATE" then
		FullScan()
	elseif event == "SKILL_LINES_CHANGED" then
		if GateEligibility() then
			pcall(ScanProfessions)
		end
	elseif event == "TRADE_SKILL_LIST_UPDATE" then
		if GateEligibility() then
			pcall(ScanRecipes)
		end
	elseif event == "PLAYER_EQUIPMENT_CHANGED" then
		if GateEligibility() then
			pcall(ScanEquipment)
		end
	end
end)
