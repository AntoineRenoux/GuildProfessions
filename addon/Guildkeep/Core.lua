local _, GP = ...

-- Socle commun : données de base (SavedVariables, données du serveur, roster),
-- identité du joueur, droits d'officier, temps et noms d'objets ou de recettes.
-- Aucune fonctionnalité ici : chaque module (Modules.lua) s'appuie sur ce socle
-- sans jamais appeler un autre module.

GP.VERSION = "0.1.0"
GP.ICON = "Interface\\Icons\\INV_Misc_Gear_01"

-- Messages dans le chat ------------------------------------------------------------

function GP.Chat(text)
	print("|cff33ff99Guildkeep|r — " .. text)
end

-- Temps ------------------------------------------------------------------------------

-- Horodatage commun à tous les clients (heure du serveur).
function GP.Now()
	return GetServerTime and GetServerTime() or time()
end

-- Horodatage strictement plus récent que l'état connu : la modification gagne
-- même si deux changements tombent dans la même seconde.
function GP.NextAt(previous)
	return math.max(GP.Now(), (previous or 0) + 1)
end

-- SavedVariables ---------------------------------------------------------------------

local function GuildKey(name)
	return name and strtrim(name):lower() or nil
end
GP.GuildKey = GuildKey

-- Tables du socle ; chaque module initialise les siennes à son chargement.
function GP.InitDB()
	GuildkeepDB = GuildkeepDB or {}
	GuildkeepDB.characters = GuildkeepDB.characters or {}
	GuildkeepDB.importedByGuild = GuildkeepDB.importedByGuild or {}
	-- Ancienne version : un seul import, rangé désormais sous sa guilde.
	local legacy = GuildkeepDB.importedServerData
	if legacy then
		if legacy.guildName then
			GuildkeepDB.importedByGuild[GuildKey(legacy.guildName)] = legacy
		end
		GuildkeepDB.importedServerData = nil
	end
end

-- Identité du joueur -------------------------------------------------------------------

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

-- « Prénom-Nom » (expéditeur d'un message, roster du jeu) → « Prénom Nom ».
function GP.NormalizeName(name)
	return name and (name:gsub("%-", " ", 1)) or nil
end

function GP.GetLocalCharacter()
	local name = GP.GetPlayerFullName()
	GuildkeepDB.characters[name] = GuildkeepDB.characters[name] or {}
	return GuildkeepDB.characters[name]
end

-- Migration : les versions précédentes de l'addon indexaient par prénom seul.
function GP.MigrateLegacyCharacterKey()
	local firstName = UnitName("player")
	local fullName = GP.GetPlayerFullName()
	local characters = GuildkeepDB.characters
	if firstName and fullName and firstName ~= fullName and characters[firstName] then
		characters[fullName] = characters[fullName] or characters[firstName]
		characters[firstName] = nil
	end
end

function GP.GetPlayerGuildName()
	return (GetGuildInfo("player"))
end

-- Un personnage hors guilde ne doit ni être relevé, ni exporté, ni passer de
-- commandes : les rerolls ne sont pas listés sans le consentement du joueur.
-- Tout personnage en guilde est relevé : le compagnon n'envoie ses données
-- qu'à sa propre guilde, et seulement si elle est inscrite.
function GP.IsEligibleCharacter()
	if not GP.GetPlayerGuildName() then
		return false, "hors guilde"
	end
	return true
end

-- Droits d'officier -----------------------------------------------------------------------

-- Rang dans la guilde d'un membre (0 = maître de guilde), d'après le roster du client.
local function RosterRank(name)
	if not GetNumGuildMembers or not GetGuildRosterInfo then
		return nil
	end
	for i = 1, GetNumGuildMembers() do
		local rosterName, _, rankIndex = GetGuildRosterInfo(i)
		if rosterName and GP.NormalizeName(rosterName) == name then
			return rankIndex
		end
	end
end

-- Officier = maître de guilde, rang juste en dessous, ou statut d'officier donné par le jeu.
local OFFICER_MAX_RANK = 1

function GP.IsOfficer()
	if IsGuildLeader and IsGuildLeader() then
		return true
	end
	if C_GuildInfo and C_GuildInfo.IsGuildOfficer then
		local ok, officer = pcall(C_GuildInfo.IsGuildOfficer)
		if ok and officer then
			return true
		end
	end
	local rank = RosterRank(GP.GetPlayerFullName())
	return rank ~= nil and rank <= OFFICER_MAX_RANK
end

-- Un autre joueur a-t-il le rang d'officier ? Sert à trier ce qu'on reçoit
-- des joueurs connectés ; le serveur refait toujours le contrôle.
function GP.IsOfficerName(name)
	local rank = RosterRank(name)
	return rank ~= nil and rank <= OFFICER_MAX_RANK
end

-- Données du serveur -------------------------------------------------------------------------

-- Plusieurs guildes : Data.lua (écrit par le compagnon) contient une section
-- par guilde inscrite sur le poste ; l'addon prend celle de la guilde du
-- personnage connecté, réévaluée à chaque appel (connexion, /reload,
-- changement de guilde). Ancien format (une seule guilde) toujours lu.
local EMPTY = { characters = {} }

local function FileSections()
	local file = Guildkeep_ServerData
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
	for _, section in pairs(GuildkeepDB and GuildkeepDB.importedByGuild or {}) do
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
	local imported = GuildkeepDB and GuildkeepDB.importedByGuild and GuildkeepDB.importedByGuild[guild]
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
	local char = GuildkeepDB and GuildkeepDB.characters[characterName]
	if not char or not char.guild then
		return characterName == GP.GetPlayerFullName()
	end
	return GuildKey(char.guild) == GuildKey(GP.GetPlayerGuildName())
end

-- Roster : serveur + relevés locaux ------------------------------------------------------------
-- Le roster affiché = données serveur (tout le monde) écrasées par les données
-- relevées localement (plus fraîches) pour les personnages de ce compte.

-- Le relevé local stocke les métiers en map [nom] = {...} ; l'affichage attend une liste.
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
	for name, char in pairs(GuildkeepDB.characters) do
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

-- Noms de recettes ------------------------------------------------------------------------------

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
		if GuildkeepDB then
			GuildkeepDB.recipeNames = GuildkeepDB.recipeNames or {}
			GuildkeepDB.recipeNames[spellId] = name
		end
	end
	return name
end

-- Objets ------------------------------------------------------------------------------------

local function GetItemData(itemId)
	local getter = (C_Item and C_Item.GetItemInfo) or GetItemInfo
	if not getter then
		return nil
	end
	local ok, name, link, quality, _, _, _, _, _, _, icon = pcall(getter, itemId)
	if ok then
		return name, link, quality, icon
	end
end

-- Nom, lien, icône d'un objet ; le client peut ne pas l'avoir encore en cache.
function GP.ItemInfo(itemId, fallbackName)
	local name, link, _, icon = GetItemData(itemId)
	if not icon then
		local instant = (C_Item and C_Item.GetItemInfoInstant) or GetItemInfoInstant
		if instant then
			local ok, _, _, _, _, instantIcon = pcall(instant, itemId)
			icon = ok and instantIcon or nil
		end
	end
	return name or fallbackName or ("Objet #" .. itemId), link, icon or "Interface\\Icons\\INV_Misc_QuestionMark"
end

-- Point d'extension de l'interface : redéfini par MainFrame.lua.
function GP.RefreshUI() end
