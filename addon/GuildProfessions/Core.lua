local ADDON_NAME, GP = ...

GP.VERSION = "0.1.0"

-- SavedVariables ------------------------------------------------------------

local function InitDB()
	GuildProfessionsDB = GuildProfessionsDB or {}
	GuildProfessionsDB.characters = GuildProfessionsDB.characters or {}
end

function GP.GetLocalCharacter()
	local name = UnitName("player")
	GuildProfessionsDB.characters[name] = GuildProfessionsDB.characters[name] or {}
	return GuildProfessionsDB.characters[name]
end

function GP.GetServerData()
	return GuildProfessions_ServerData or { characters = {} }
end

-- Fusion serveur + scan local ------------------------------------------------
-- Le roster affiché = données serveur (tout le monde) écrasées par les données
-- scannées localement (plus fraîches) pour les personnages de ce compte.

-- Le scan local stocke les métiers en map [nom] = {...} ; l'affichage attend une liste.
local function LocalProfessionsToList(map)
	local list = {}
	for name, p in pairs(map or {}) do
		list[#list + 1] = {
			name = name, level = p.level, max = p.max, note = p.note,
			recipes = p.recipes, source = "addon", scannedAt = p.scannedAt,
		}
	end
	return list
end

function GP.GetRoster()
	local roster, index = {}, {}

	local function upsert(entry)
		local existing = index[entry.name]
		if not existing then
			existing = { name = entry.name, professions = {} }
			index[entry.name] = existing
			roster[#roster + 1] = existing
		end
		existing.classFile = entry.classFile or existing.classFile
		existing.level = entry.level or existing.level
		for _, prof in ipairs(entry.professions or {}) do
			local replaced = false
			for i, known in ipairs(existing.professions) do
				if known.name == prof.name then
					existing.professions[i] = prof
					replaced = true
					break
				end
			end
			if not replaced then
				existing.professions[#existing.professions + 1] = prof
			end
		end
	end

	for _, char in ipairs(GP.GetServerData().characters or {}) do
		upsert(char)
	end
	for name, char in pairs(GuildProfessionsDB.characters) do
		upsert({
			name = name, classFile = char.classFile, level = char.level,
			professions = LocalProfessionsToList(char.professions),
		})
	end

	table.sort(roster, function(a, b) return a.name < b.name end)
	return roster
end

-- Noms de recettes ------------------------------------------------------------

local recipeNameCache = {}

function GP.GetRecipeName(spellId)
	local cached = recipeNameCache[spellId]
	if cached then
		return cached
	end
	local name
	if C_Spell and C_Spell.GetSpellInfo then
		local info = C_Spell.GetSpellInfo(spellId)
		name = info and info.name
	elseif GetSpellInfo then
		name = GetSpellInfo(spellId)
	end
	if not name then
		local server = GP.GetServerData()
		name = server.recipeNames and server.recipeNames[spellId]
	end
	if name then
		recipeNameCache[spellId] = name
	end
	return name
end

-- Point d'extension UI : redéfini par UI.lua, no-op tant qu'elle n'est pas chargée.
function GP.RefreshUI() end

-- Initialisation ----------------------------------------------------------------

local frame = CreateFrame("Frame")
frame:RegisterEvent("ADDON_LOADED")
frame:SetScript("OnEvent", function(self, event, arg1)
	if event == "ADDON_LOADED" and arg1 == ADDON_NAME then
		InitDB()
		self:UnregisterEvent("ADDON_LOADED")
		local server = GP.GetServerData()
		print(("|cff33ff99GuildProfessions|r v%s chargé — /gp pour ouvrir%s"):format(
			GP.VERSION, server.mock and " |cffffcc00(données de démonstration)|r" or ""))
	end
end)

SLASH_GUILDPROFESSIONS1 = "/gp"
SLASH_GUILDPROFESSIONS2 = "/guildprofessions"
SlashCmdList.GUILDPROFESSIONS = function()
	GP.ToggleMainFrame()
end
