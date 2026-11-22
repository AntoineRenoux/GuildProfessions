local ADDON_NAME, GP = ...

GP.VERSION = "0.1.0"

-- SavedVariables ------------------------------------------------------------

local function GuildKey(name)
	return name and strtrim(name):lower() or nil
end

local function InitDB()
	GuildProfessionsDB = GuildProfessionsDB or {}
	GuildProfessionsDB.characters = GuildProfessionsDB.characters or {}
	GuildProfessionsDB.orders = GuildProfessionsDB.orders or {}
	GuildProfessionsDB.orderActions = GuildProfessionsDB.orderActions or {}
	GuildProfessionsDB.importedByGuild = GuildProfessionsDB.importedByGuild or {}
	-- Ancienne version : un seul import, rangé désormais sous sa guilde.
	local legacy = GuildProfessionsDB.importedServerData
	if legacy then
		if legacy.guildName then
			GuildProfessionsDB.importedByGuild[GuildKey(legacy.guildName)] = legacy
		end
		GuildProfessionsDB.importedServerData = nil
	end
end

-- Purge les données locales déjà synchronisées : commandes locales reprises
-- par le serveur (clientId présent dans l'export) et actions déjà appliquées.
-- Toutes les guildes du fichier : au chargement, la guilde du personnage
-- n'est pas encore connue.
local function PruneSyncedOrders()
	local byClientId, byId = {}, {}
	for _, section in ipairs(GP.AllServerSections()) do
		for _, order in ipairs(section.orders or {}) do
			if order.clientId then
				byClientId[order.clientId] = order
			end
			byId[tostring(order.id)] = order
		end
	end
	for localId in pairs(GuildProfessionsDB.orders) do
		if byClientId[localId] then
			GuildProfessionsDB.orders[localId] = nil
		end
	end
	for key, action in pairs(GuildProfessionsDB.orderActions) do
		local serverOrder = byId[key] or byClientId[key]
		if serverOrder and serverOrder.status == action.status then
			GuildProfessionsDB.orderActions[key] = nil
		end
	end
end

-- Sur WoW: Forever, les personnages ont prénom + nom : UnitName ne rend que
-- le prénom (non unique !), UnitFullName rend le nom de famille dans le slot
-- « royaume ». La clé d'identité est TOUJOURS le nom complet « Prénom Nom ».
function GP.GetPlayerFullName()
	local name, surname = UnitFullName("player")
	name = name or UnitName("player")
	if surname and surname ~= "" then
		return name .. " " .. surname
	end
	return name
end

function GP.GetLocalCharacter()
	local name = GP.GetPlayerFullName()
	GuildProfessionsDB.characters[name] = GuildProfessionsDB.characters[name] or {}
	return GuildProfessionsDB.characters[name]
end

-- Migration : les versions précédentes de l'addon indexaient par prénom seul.
function GP.MigrateLegacyCharacterKey()
	local firstName = UnitName("player")
	local fullName = GP.GetPlayerFullName()
	local characters = GuildProfessionsDB.characters
	if firstName and fullName and firstName ~= fullName and characters[firstName] then
		characters[fullName] = characters[fullName] or characters[firstName]
		characters[firstName] = nil
	end
end

-- Plusieurs guildes : Data.lua (écrit par le compagnon) contient une section
-- par guilde inscrite sur le poste ; l'addon prend celle de la guilde du
-- personnage connecté, réévaluée à chaque appel (connexion, /reload,
-- changement de guilde). Ancien format (une seule guilde) toujours lu.
local EMPTY = { characters = {} }

local function FileSections()
	local file = GuildProfessions_ServerData
	if type(file) ~= "table" then
		return {}
	end
	if file.guilds then
		return file.guilds
	end
	return { file }
end

function GP.AllServerSections()
	local sections = {}
	for _, section in ipairs(FileSections()) do
		sections[#sections + 1] = section
	end
	for _, section in pairs(GuildProfessionsDB and GuildProfessionsDB.importedByGuild or {}) do
		sections[#sections + 1] = section
	end
	return sections
end

-- Deux sources pour la guilde courante : Data.lua et une chaîne importée à la
-- main (/gkeep export sur Discord). La plus récente gagne, à version égale
-- l'import manuel l'emporte.
function GP.GetServerData()
	local guild = GuildKey(GP.GetPlayerGuildName())
	if not guild then
		return EMPTY
	end
	local fromFile
	for _, section in ipairs(FileSections()) do
		if GuildKey(section.guildName) == guild then
			fromFile = section
			break
		end
	end
	local imported = GuildProfessionsDB and GuildProfessionsDB.importedByGuild and GuildProfessionsDB.importedByGuild[guild]
	if imported and (not fromFile or (imported.version or 0) >= (fromFile.version or 0) or fromFile.mock) then
		return imported
	end
	return fromFile or EMPTY
end

-- La guilde du personnage utilise-t-elle Guildkeep (données reçues pour elle) ?
function GP.IsGuildRegistered()
	return GP.GetServerData() ~= EMPTY
end

-- Un personnage local appartient-il à la guilde affichée ? Les rerolls du
-- compte qui sont dans une autre guilde n'apparaissent pas dans son annuaire.
function GP.IsInCurrentGuild(characterName)
	local char = GuildProfessionsDB and GuildProfessionsDB.characters[characterName]
	if not char or not char.guild then
		return characterName == GP.GetPlayerFullName()
	end
	return GuildKey(char.guild) == GuildKey(GP.GetPlayerGuildName())
end

-- Fusion serveur + scan local ------------------------------------------------
-- Le roster affiché = données serveur (tout le monde) écrasées par les données
-- scannées localement (plus fraîches) pour les personnages de ce compte.

-- Le scan local stocke les métiers en map [nom] = {...} ; l'affichage attend une liste.
local function LocalProfessionsToList(map)
	local list = {}
	for name, p in pairs(map or {}) do
		list[#list + 1] = {
			name = name, level = p.level, max = p.max, note = p.note, icon = p.icon,
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
		existing.raceFile = entry.raceFile or existing.raceFile
		existing.raceId = entry.raceId or existing.raceId
		existing.gender = entry.gender or existing.gender
		existing.equipment = entry.equipment or existing.equipment
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
		if GP.IsInCurrentGuild(name) then
			upsert({
			name = name, classFile = char.classFile, level = char.level,
			raceFile = char.raceFile, raceId = char.raceId, gender = char.gender,
			equipment = char.equipment,
			professions = LocalProfessionsToList(char.professions),
			})
		end
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
		-- Clés numériques (Data.lua) ou texte (JSON d'une chaîne importée).
		name = server.recipeNames and (server.recipeNames[spellId] or server.recipeNames[tostring(spellId)])
	end
	if name then
		recipeNameCache[spellId] = name
		-- Persisté dans les SavedVariables : le compagnon s'en sert pour
		-- envoyer les noms au serveur (recherche de recette via Discord).
		if GuildProfessionsDB then
			GuildProfessionsDB.recipeNames = GuildProfessionsDB.recipeNames or {}
			GuildProfessionsDB.recipeNames[spellId] = name
		end
	end
	return name
end

-- Éligibilité du personnage ----------------------------------------------------
-- Un personnage hors guilde (ou d'une autre guilde que celle du serveur de
-- données) ne doit ni être scanné, ni exporté, ni passer de commandes : les
-- rerolls ne sont pas listés sans le consentement du joueur.

function GP.GetPlayerGuildName()
	return (GetGuildInfo("player"))
end

-- Tout personnage en guilde est relevé : le compagnon n'envoie ses données
-- qu'à sa propre guilde, et seulement si elle est inscrite.
function GP.IsEligibleCharacter()
	if not GP.GetPlayerGuildName() then
		return false, "hors guilde"
	end
	return true
end

-- Commandes de craft ----------------------------------------------------------

function GP.CreateOrder(crafter, item, qty, note)
	local requester = GP.GetPlayerFullName()
	local localId = requester .. "-" .. time() .. "-" .. math.random(1000, 9999)
	local order = {
		requester = requester,
		crafter = crafter,
		item = item,
		qty = qty or 1,
		note = note ~= "" and note or nil,
		createdAt = date("%Y-%m-%d %H:%M"),
	}
	GuildProfessionsDB.orders[localId] = order
	GP.Comm.SendNewOrder(order)
	GP.RefreshUI()
	return localId
end

-- Liste fusionnée pour l'affichage : commandes du serveur (Data.lua) +
-- commandes locales pas encore synchronisées, avec les changements de statut
-- décidés en jeu par-dessus.
function GP.GetOrders()
	local merged = {}
	local actions = GuildProfessionsDB.orderActions
	for _, order in ipairs(GP.GetServerData().orders or {}) do
		local key = tostring(order.id)
		local action = actions[key] or (order.clientId and actions[order.clientId])
		merged[#merged + 1] = {
			key = key,
			id = order.id,
			requester = order.requester,
			crafter = order.crafter,
			item = order.item,
			-- qty vient de Data.lua (compagnon), quantity d'une chaîne importée
			qty = order.qty or order.quantity or 1,
			note = order.note,
			status = action and action.status or order.status,
			pendingSync = action ~= nil,
			createdAt = order.createdUtc,
		}
	end
	for localId, order in pairs(GuildProfessionsDB.orders) do
		-- Commande passée par un reroll d'une autre guilde : pas ici.
		if GP.IsInCurrentGuild(order.requester) then
			local action = actions[localId]
			merged[#merged + 1] = {
				key = localId,
				requester = order.requester,
				crafter = order.crafter,
				item = order.item,
				qty = order.qty or 1,
				note = order.note,
				status = action and action.status or "open",
				pendingSync = true,
				createdAt = order.createdAt,
			}
		end
	end
	table.sort(merged, function(a, b)
		return (a.createdAt or "") > (b.createdAt or "")
	end)
	return merged
end

function GP.SetOrderStatus(order, status)
	GuildProfessionsDB.orderActions[order.key] = { status = status, at = date("%Y-%m-%d %H:%M") }
	GP.Comm.SendStatus(order, status)
	GP.RefreshUI()
end

-- Point d'extension UI : redéfini par UI.lua, no-op tant qu'elle n'est pas chargée.
function GP.RefreshUI() end

-- Initialisation ----------------------------------------------------------------

local frame = CreateFrame("Frame")
frame:RegisterEvent("ADDON_LOADED")
frame:SetScript("OnEvent", function(self, event, arg1)
	if event == "ADDON_LOADED" and arg1 == ADDON_NAME then
		InitDB()
		PruneSyncedOrders()
		self:UnregisterEvent("ADDON_LOADED")
		local server = GP.GetServerData()
		print(("|cff33ff99GuildProfessions|r v%s chargé — /gp pour ouvrir%s"):format(
			GP.VERSION, server.mock and " |cffffcc00(données de démonstration)|r" or ""))
	end
end)

SLASH_GUILDPROFESSIONS1 = "/gp"
SLASH_GUILDPROFESSIONS2 = "/guildprofessions"
SlashCmdList.GUILDPROFESSIONS = function(msg)
	if strtrim(msg or ""):lower() == "debug" and GP.DebugGuildTab then
		GP.DebugGuildTab()
		return
	end
	GP.ToggleMainFrame()
end
