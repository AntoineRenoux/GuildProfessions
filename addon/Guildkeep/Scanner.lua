local _, GP = ...

-- Scanne le personnage local vers GuildkeepDB. Les SavedVariables ne
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
	if GuildkeepDB.characters and GuildkeepDB.characters[name] then
		GuildkeepDB.characters[name] = nil
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
		local name, icon, level, max = GetProfessionInfo(index)
		if name then
			seen[name] = true
			local prof = char.professions[name] or {}
			prof.level = level
			prof.max = max
			prof.icon = icon
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

-- Relevé des recettes : à l'ouverture d'une fenêtre de métier. Le client charge la
-- liste en plusieurs temps : on relève un peu après l'ouverture, et on réessaie tant
-- qu'elle n'est pas prête. Le dernier résultat est gardé pour /gkeep debug.
local recipeScan = { status = "aucune fenêtre de métier ouverte depuis le chargement" }

local function Chat(text)
	print("|cff33ff99Guildkeep|r " .. text)
end

-- Nom du métier tel que GetProfessionInfo le donne (clé de char.professions) :
-- rapprochement par identifiant de ligne de compétence, plus fiable que le nom
-- (« Travail du cuir » contre « Travail du cuir classique », etc.).
local function CurrentProfessionName()
	local base = C_TradeSkillUI.GetBaseProfessionInfo and C_TradeSkillUI.GetBaseProfessionInfo()
	local child = C_TradeSkillUI.GetChildProfessionInfo and C_TradeSkillUI.GetChildProfessionInfo()
	local wanted = {}
	for _, info in ipairs({ base or {}, child or {} }) do
		for _, key in ipairs({ "professionID", "parentProfessionID" }) do
			if info[key] then
				wanted[info[key]] = true
			end
		end
	end
	if GetProfessions and GetProfessionInfo then
		local prof1, prof2, arch, fishing, cooking, firstAid = GetProfessions()
		for _, index in pairs({ prof1, prof2, arch, fishing, cooking, firstAid }) do
			local name, _, _, _, _, _, skillLine = GetProfessionInfo(index)
			if name and skillLine and wanted[skillLine] then
				return name
			end
		end
	end
	local name = (base and (base.parentProfessionName or base.professionName)) or nil
	if (not name or name == "") and C_TradeSkillUI.GetTradeSkillLine then
		name = select(2, C_TradeSkillUI.GetTradeSkillLine())
	end
	if name == "" then
		return nil
	end
	return name
end

-- Renvoie true quand le relevé est fait (ou impossible), false pour réessayer.
local function ScanRecipes()
	if not (C_TradeSkillUI and C_TradeSkillUI.GetAllRecipeIDs and C_TradeSkillUI.GetRecipeInfo) then
		recipeScan.status = "API C_TradeSkillUI absente de ce client"
		return true
	end
	-- Fenêtre d'un autre joueur (lien), de la guilde ou d'un PNJ : pas nos recettes.
	for _, check in ipairs({ "IsTradeSkillLinked", "IsTradeSkillGuild", "IsNPCCrafting" }) do
		if C_TradeSkillUI[check] and C_TradeSkillUI[check]() then
			recipeScan.status = "fenêtre d'un autre joueur ou d'un PNJ : ignorée"
			return true
		end
	end
	if C_TradeSkillUI.IsTradeSkillReady and not C_TradeSkillUI.IsTradeSkillReady() then
		recipeScan.status = "liste des recettes pas encore chargée"
		return false
	end
	local profName = CurrentProfessionName()
	if not profName then
		recipeScan.status = "métier de la fenêtre non identifié"
		return false
	end
	local ids = C_TradeSkillUI.GetAllRecipeIDs()
	if not ids or #ids == 0 then
		recipeScan.status = profName .. " : liste des recettes vide pour l'instant"
		return false
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
	local before = prof.recipes and #prof.recipes or -1
	prof.recipes = learned
	prof.scannedAt = Now()
	char.professions[profName] = prof
	recipeScan.status = ("%s : %d recettes apprises sur %d (%s)"):format(profName, #learned, #ids, Now())
	if before ~= #learned then
		Chat(("%d recettes de %s relevées. Elles partent vers ta guilde à ta prochaine déconnexion ou /reload."):format(#learned, profName))
	end
	GP.RefreshUI()
	return true
end

-- Relevé différé, regroupé (plusieurs événements arrivent d'un coup), avec reprises.
local pending = false
local function ScheduleRecipeScan(attempt)
	attempt = attempt or 1
	if pending and attempt == 1 then
		return
	end
	pending = true
	C_Timer.After(attempt == 1 and 0.5 or 1, function()
		local ok, done = pcall(ScanRecipes)
		if not ok then
			recipeScan.status = "erreur : " .. tostring(done)
			done = true
		end
		if done or attempt >= 6 then
			pending = false
		else
			ScheduleRecipeScan(attempt + 1)
		end
	end)
end

function GP.DebugRecipeScan()
	Chat("— relevé des recettes : " .. recipeScan.status)
	local char = GuildkeepDB and GuildkeepDB.characters and GuildkeepDB.characters[GP.GetPlayerFullName()]
	for name, prof in pairs(char and char.professions or {}) do
		print(("  %s %d/%d : %s"):format(name, prof.level or 0, prof.max or 0,
			prof.recipes and (#prof.recipes .. " recettes") or "recettes jamais relevées (ouvre la fenêtre du métier)"))
	end
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
frame:RegisterEvent("TRADE_SKILL_SHOW")
frame:RegisterEvent("TRADE_SKILL_LIST_UPDATE")
frame:RegisterEvent("TRADE_SKILL_DATA_SOURCE_CHANGED")
frame:RegisterEvent("NEW_RECIPE_LEARNED")
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
	elseif event == "TRADE_SKILL_SHOW" or event == "TRADE_SKILL_LIST_UPDATE"
		or event == "TRADE_SKILL_DATA_SOURCE_CHANGED" or event == "NEW_RECIPE_LEARNED" then
		if GateEligibility() then
			ScheduleRecipeScan()
		end
	elseif event == "PLAYER_EQUIPMENT_CHANGED" then
		if GateEligibility() then
			pcall(ScanEquipment)
		end
	end
end)
