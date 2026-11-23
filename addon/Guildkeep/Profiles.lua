local _, GP = ...

-- Module Profils et accueil (données) : profil de chaque joueur (principal,
-- rerolls, rôles, disponibilités, note), talents et version de l'addon de
-- ses personnages, arrivées et départs relevés dans le roster de la guilde, et
-- liste d'accueil des nouveaux.
--
-- Trois sources, comme les autres modules : le serveur (section « profiles »
-- de Data.lua), ce que les joueurs connectés ont annoncé (GuildkeepDB.profilesPeer,
-- jamais renvoyé au serveur) et ce qui est fait ici (profileEdits, talents,
-- versions, rosterChanges, onboardingEdits). Le plus récent gagne ; ce que le
-- serveur reflète est purgé.
-- Droits : son profil ne regroupe que des personnages de ce compte ; une étape
-- d'accueil se coche pour soi, ou pour un autre par un officier.

GP.Profiles = {}
local P = GP.Profiles

P.ROLES = { "tank", "healer", "melee", "ranged" }
P.ROLE_LABELS = { tank = "Tank", healer = "Soigneur", melee = "DPS mêlée", ranged = "DPS distance" }
P.STEPS = { "discord", "main", "profile", "handbook", "intro" }
P.STEP_LABELS = {
	discord = "Rejoindre le Discord et lier son personnage",
	main = "Choisir son personnage principal",
	profile = "Remplir son profil (rôles, disponibilités)",
	handbook = "Lire la charte et les règles de butin",
	intro = "Se présenter à la guilde",
}
P.AVAILABILITY_MAX, P.NOTE_MAX, P.MAX_ALTS = 120, 200, 20

local KEEP_SECONDS = 60 * 24 * 3600
local NEWCOMER_SECONDS = 60 * 24 * 3600
local DUPLICATE_SECONDS = 12 * 3600
local TABLES = { "profileEdits", "talents", "versions", "rosterChanges", "onboardingEdits" }

local function Now()
	return GP.Now()
end

local function GuildKey()
	local guild = GP.GetPlayerGuildName()
	return guild and strtrim(guild):lower() or nil
end

function P.InitDB()
	for _, name in ipairs(TABLES) do
		GuildkeepDB[name] = GuildkeepDB[name] or {}
	end
	GuildkeepDB.profilesPeer = GuildkeepDB.profilesPeer or {}
	GuildkeepDB.rosterSnapshot = GuildkeepDB.rosterSnapshot or {}
end

local function PeerStore()
	local key = GuildKey()
	if not key then
		return { profiles = {}, onboarding = {} }
	end
	local store = GuildkeepDB.profilesPeer[key] or {}
	GuildkeepDB.profilesPeer[key] = store
	store.profiles = store.profiles or {}
	store.onboarding = store.onboarding or {}
	return store
end

local function Clip(text, max)
	if type(text) ~= "string" then
		return nil
	end
	text = strtrim(text)
	if text == "" then
		return nil
	end
	return #text > max and text:sub(1, max) or text
end

local function Contains(list, value)
	for _, v in ipairs(list or {}) do
		if v == value then
			return true
		end
	end
	return false
end

-- Personnages de ce compte dans la guilde affichée (le personnage joué d'abord).
function P.MyCharacters()
	local me = GP.GetPlayerFullName()
	local list = { me }
	local others = {}
	for name in pairs(GuildkeepDB.characters or {}) do
		if name ~= me and GP.IsInCurrentGuild(name) then
			others[#others + 1] = name
		end
	end
	table.sort(others)
	for _, name in ipairs(others) do
		list[#list + 1] = name
	end
	return list
end

local function IsMine(name)
	return Contains(P.MyCharacters(), name)
end

-- Lecture -------------------------------------------------------------------------------------

-- Profils, talents, journal et versions de la guilde affichée, prêts à afficher.
function P.Get()
	local server = GP.GetServerData().profiles or {}
	local profiles, byMain = {}, {}

	local function Put(main, fields, updatedAt, pending)
		local current = byMain[main]
		if current and (updatedAt or 0) <= (current.updatedAt or 0) then
			return
		end
		local entry = {
			main = main, alts = fields.alts or {}, roles = fields.roles or {}, availability = fields.availability,
			note = fields.note, updatedAt = updatedAt or 0, pendingSync = pending or nil,
		}
		if current then
			for i, p in ipairs(profiles) do
				if p == current then
					profiles[i] = entry
				end
			end
		else
			profiles[#profiles + 1] = entry
		end
		byMain[main] = entry
	end

	for _, p in ipairs(server.profiles or {}) do
		Put(p.main, p, p.updatedAt, false)
	end
	for main, edit in pairs(PeerStore().profiles) do
		Put(main, edit, edit.at, false)
	end
	for main, edit in pairs(GuildkeepDB.profileEdits) do
		if GP.IsInCurrentGuild(main) then
			Put(main, edit, edit.at, true)
		end
	end

	-- Un personnage n'a qu'un profil : le plus récent l'emporte (changement de principal).
	table.sort(profiles, function(a, b) return a.updatedAt > b.updatedAt end)
	local owner, kept = {}, {}
	for _, p in ipairs(profiles) do
		if not owner[p.main] then
			local alts = {}
			for _, alt in ipairs(p.alts) do
				if not owner[alt] and alt ~= p.main then
					alts[#alts + 1] = alt
				end
			end
			p.alts = alts
			owner[p.main] = p
			for _, alt in ipairs(alts) do
				owner[alt] = p
			end
			kept[#kept + 1] = p
		end
	end
	table.sort(kept, function(a, b) return a.main < b.main end)

	local talents = {}
	for _, t in ipairs(server.talents or {}) do
		talents[t.character] = t
	end
	for character, t in pairs(GuildkeepDB.talents) do
		if GP.IsInCurrentGuild(character) and (t.at or 0) > ((talents[character] or {}).at or 0) then
			talents[character] = { character = character, spec = t.spec, summary = t.summary, at = t.at }
		end
	end

	local versions = {}
	for _, v in ipairs(server.versions or {}) do
		versions[v.character] = v
	end
	for character, version in pairs(GuildkeepDB.versions) do
		if GP.IsInCurrentGuild(character) then
			versions[character] = { character = character, version = version, at = (versions[character] or {}).at or Now() }
		end
	end

	-- Journal : celui du serveur, plus ce qui a été relevé ici et n'y est pas encore.
	local journal = {}
	for _, r in ipairs(server.rosterChanges or {}) do
		journal[#journal + 1] = { character = r.character, kind = r.kind, at = r.at }
	end
	local guild = GuildKey()
	for _, r in ipairs(GuildkeepDB.rosterChanges) do
		if r.guild == guild then
			local known = false
			for _, s in ipairs(journal) do
				if s.character == r.character and s.kind == r.kind and math.abs(s.at - r.at) <= DUPLICATE_SECONDS then
					known = true
				end
			end
			if not known then
				journal[#journal + 1] = { character = r.character, kind = r.kind, at = r.at, pendingSync = true }
			end
		end
	end
	table.sort(journal, function(a, b) return a.at > b.at end)

	return { profiles = kept, owner = owner, talents = talents, versions = versions, journal = journal }
end

-- Profil où figure ce personnage (principal ou reroll).
function P.ProfileOf(character, data)
	return (data or P.Get()).owner[character]
end

-- Accueil : étapes faites par personnage suivi (serveur, puis coches plus récentes d'ici ou reçues).
function P.Onboarding(data)
	data = data or P.Get()
	local server = GP.GetServerData().profiles or {}
	local states = {}
	local function State(character)
		local state = states[character]
		if not state then
			state = { character = character, done = {}, updatedAt = 0, stamps = {} }
			states[character] = state
		end
		return state
	end
	for _, o in ipairs(server.onboarding or {}) do
		local state = State(o.character)
		state.updatedAt = o.updatedAt or 0
		for _, step in ipairs(o.done or {}) do
			state.done[step] = true
		end
	end
	local since = Now() - NEWCOMER_SECONDS
	for _, r in ipairs(data.journal) do
		if r.kind == "join" and r.at >= since then
			State(r.character)
		end
	end
	local function Apply(edit, pending)
		local state = State(edit.character)
		local stamp = state.stamps[edit.step] or state.updatedAt
		if (edit.at or 0) > stamp then
			state.done[edit.step] = edit.done and true or nil
			state.stamps[edit.step] = edit.at
			state.pendingSync = state.pendingSync or pending
		end
	end
	for _, edit in pairs(PeerStore().onboarding) do
		Apply(edit, false)
	end
	for _, edit in pairs(GuildkeepDB.onboardingEdits) do
		if GP.IsInCurrentGuild(edit.by) then
			Apply(edit, true)
		end
	end
	-- Étapes déduites : principal choisi, profil rempli.
	for character, state in pairs(states) do
		local profile = data.owner[character]
		if profile then
			state.done.main = true
			if #profile.roles > 0 or profile.availability then
				state.done.profile = true
			end
		end
		local count = 0
		for _, step in ipairs(P.STEPS) do
			if state.done[step] then
				count = count + 1
			end
		end
		state.count = count
	end
	return states
end

-- Liste d'accueil d'un personnage (même s'il n'est pas suivi).
function P.StepsOf(character, states)
	states = states or P.Onboarding()
	return states[character] or { character = character, done = {}, count = 0 }
end

-- Nouveaux dont l'accueil n'est pas fini, les moins avancés d'abord.
function P.Incomplete(states)
	local list = {}
	for _, state in pairs(states or P.Onboarding()) do
		if state.count < #P.STEPS then
			list[#list + 1] = state
		end
	end
	table.sort(list, function(a, b)
		if a.count ~= b.count then
			return a.count < b.count
		end
		return a.character < b.character
	end)
	return list
end

-- Changements faits en jeu --------------------------------------------------------------------

function P.SaveProfile(fields)
	local main = fields.main or GP.GetPlayerFullName()
	if not IsMine(main) then
		return nil, "le principal doit être un de tes personnages dans cette guilde"
	end
	local alts = {}
	for _, alt in ipairs(fields.alts or {}) do
		if alt ~= main and IsMine(alt) and not Contains(alts, alt) and #alts < P.MAX_ALTS then
			alts[#alts + 1] = alt
		end
	end
	local roles = {}
	for _, role in ipairs(P.ROLES) do
		if Contains(fields.roles, role) then
			roles[#roles + 1] = role
		end
	end
	local previous = P.ProfileOf(main)
	local entry = {
		main = main, alts = alts, roles = roles, availability = Clip(fields.availability, P.AVAILABILITY_MAX),
		note = Clip(fields.note, P.NOTE_MAX), by = GP.GetPlayerFullName(),
		at = GP.NextAt(previous and previous.updatedAt),
	}
	-- L'ancien profil local d'un autre principal de ce compte disparaît (changement de principal).
	for key in pairs(GuildkeepDB.profileEdits) do
		if key == main or Contains(alts, key) then
			GuildkeepDB.profileEdits[key] = nil
		end
	end
	GuildkeepDB.profileEdits[main] = entry
	GP.Comm.Broadcast({ t = "pf", k = main, e = entry })
	GP.RefreshUI()
	return main
end

function P.SetStep(character, step, done)
	if not Contains(P.STEPS, step) then
		return nil, "étape inconnue"
	end
	character = character or GP.GetPlayerFullName()
	if not IsMine(character) and not GP.IsOfficer() then
		return nil, "seuls les officiers cochent l'accueil d'un autre joueur"
	end
	local key = character .. "|" .. step
	local previous = GuildkeepDB.onboardingEdits[key]
	local entry = { character = character, step = step, done = done and true or false, by = GP.GetPlayerFullName(),
		at = GP.NextAt(math.max(previous and previous.at or 0, P.StepsOf(character).updatedAt or 0)) }
	GuildkeepDB.onboardingEdits[key] = entry
	GP.Comm.Broadcast({ t = "ob", k = key, e = entry })
	GP.RefreshUI()
	return key
end

-- Texte à coller sur Discord ou dans le canal de guilde pour relancer les nouveaux.
function P.ReminderText()
	local lines = {}
	for _, state in ipairs(P.Incomplete()) do
		local missing = {}
		for _, step in ipairs(P.STEPS) do
			if not state.done[step] then
				missing[#missing + 1] = P.STEP_LABELS[step]:lower()
			end
		end
		lines[#lines + 1] = ("%s : %s"):format(state.character, table.concat(missing, ", "))
	end
	if #lines == 0 then
		return nil
	end
	return "Bienvenue ! Il reste quelques étapes d'accueil (/gkeep accueil) :\n" .. table.concat(lines, "\n")
end

-- Relevés automatiques ----------------------------------------------------------------------------

-- Talents du personnage joué : points par arbre (« 31/20/0 ») et arbre principal.
function P.ScanTalents()
	if not GetNumTalentTabs or not GetTalentTabInfo then
		return
	end
	local ok, tabs = pcall(GetNumTalentTabs)
	if not ok or type(tabs) ~= "number" or tabs <= 0 then
		return
	end
	local points, spec, best = {}, nil, 0
	for i = 1, math.min(tabs, 4) do
		local okTab, a, b, c, _, e = pcall(GetTalentTabInfo, i)
		if not okTab then
			return
		end
		-- Deux formes selon le client : (nom, icône, points) ou (id, nom, description, icône, points).
		local name, spent = a, c
		if type(a) == "number" and type(b) == "string" then
			name, spent = b, e
		end
		spent = tonumber(spent) or 0
		points[#points + 1] = tostring(spent)
		if spent > best then
			spec, best = name, spent
		end
	end
	local summary = table.concat(points, "/")
	local me = GP.GetPlayerFullName()
	local known = GuildkeepDB.talents[me]
	if known and known.summary == summary and known.spec == spec then
		return
	end
	GuildkeepDB.talents[me] = { spec = spec, summary = summary, at = Now() }
end

function P.ReportVersion()
	GuildkeepDB.versions[GP.GetPlayerFullName()] = GP.VERSION
end

-- Compare le roster du jeu à celui vu la dernière fois : arrivées et départs.
function P.ScanRoster()
	local guild = GuildKey()
	if not guild or not GetNumGuildMembers or not GetGuildRosterInfo then
		return
	end
	local count = GetNumGuildMembers() or 0
	if count == 0 then
		return -- roster pas encore chargé
	end
	local names = {}
	for i = 1, count do
		local name = GetGuildRosterInfo(i)
		if name then
			names[GP.NormalizeName(name)] = true
		end
	end
	local snapshot = GuildkeepDB.rosterSnapshot[guild]
	GuildkeepDB.rosterSnapshot[guild] = { names = names, at = Now() }
	if not snapshot then
		return -- premier passage : la référence est posée
	end
	local joins, leaves = {}, {}
	for name in pairs(names) do
		if not snapshot.names[name] then
			joins[#joins + 1] = name
		end
	end
	for name in pairs(snapshot.names) do
		if not names[name] then
			leaves[#leaves + 1] = name
		end
	end
	-- Roster chargé à moitié (écart énorme) : on reprend la référence sans rien noter.
	local known = 0
	for _ in pairs(snapshot.names) do
		known = known + 1
	end
	if #joins + #leaves > math.max(10, known / 4) then
		return
	end
	table.sort(joins)
	table.sort(leaves)
	local me, at = GP.GetPlayerFullName(), Now()
	for _, list in ipairs({ { joins, "join" }, { leaves, "leave" } }) do
		for _, name in ipairs(list[1]) do
			table.insert(GuildkeepDB.rosterChanges, { character = name, kind = list[2], at = at, by = me, guild = guild })
		end
	end
	if #joins > 0 then
		GP.Chat(("%s a rejoint la guilde%s — /gkeep profils"):format(table.concat(joins, ", "),
			GP.IsOfficer() and " : pense à l'accueil" or ""))
	end
	if #joins + #leaves > 0 then
		GP.RefreshUI()
	end
end

-- Contenu reçu d'un autre joueur ------------------------------------------------------------------

local function Optional(v, max)
	return v == nil or (type(v) == "string" and #v <= max)
end

local function Names(list)
	if type(list) ~= "table" or #list > P.MAX_ALTS then
		return false
	end
	for _, v in ipairs(list) do
		if type(v) ~= "string" or #v > 64 then
			return false
		end
	end
	return true
end

local function Stamp(v)
	return type(v) == "number" and v > 0 and v < 4102444800
end

local function Receive(data, sender)
	local kind, entry, key = data.t, data.e, data.k
	if type(entry) ~= "table" or type(key) ~= "string" or #key > 140 or not Stamp(entry.at) or entry.by ~= sender then
		return
	end
	local store = PeerStore()
	if kind == "pf" then
		-- Profil : envoyé par un de ses propres personnages (le principal ou un reroll).
		if key ~= entry.main or type(entry.main) ~= "string" or not Names(entry.alts) or not Names(entry.roles)
			or not Optional(entry.availability, P.AVAILABILITY_MAX) or not Optional(entry.note, P.NOTE_MAX)
			or (sender ~= entry.main and not Contains(entry.alts, sender)) then
			return
		end
		local previous = store.profiles[key]
		if not previous or entry.at > (previous.at or 0) then
			store.profiles[key] = entry
		end
	elseif kind == "ob" then
		if type(entry.character) ~= "string" or not Contains(P.STEPS, entry.step) or key ~= entry.character .. "|" .. entry.step
			or (entry.character ~= sender and not GP.IsOfficerName(sender)) then
			return
		end
		local previous = store.onboarding[key]
		if not previous or entry.at > (previous.at or 0) then
			store.onboarding[key] = entry
		end
	end
end

function P.AnnouncePending()
	local me = GP.GetPlayerFullName()
	for key, entry in pairs(GuildkeepDB.profileEdits) do
		if entry.by == me then
			GP.Comm.Broadcast({ t = "pf", k = key, e = entry })
		end
	end
	for key, entry in pairs(GuildkeepDB.onboardingEdits) do
		if entry.by == me then
			GP.Comm.Broadcast({ t = "ob", k = key, e = entry })
		end
	end
end

-- Purge et export -----------------------------------------------------------------------------------

-- Efface ce que le serveur reflète déjà (toutes guildes du fichier) et ce qui est trop vieux.
function P.Prune()
	local profiles, talents, versions, changes, onboarding = {}, {}, {}, {}, {}
	for _, section in ipairs(GP.AllServerSections()) do
		local p = section.profiles or {}
		for _, x in ipairs(p.profiles or {}) do
			profiles[x.main] = math.max(profiles[x.main] or 0, x.updatedAt or 0)
		end
		for _, x in ipairs(p.talents or {}) do
			talents[x.character] = math.max(talents[x.character] or 0, x.at or 0)
		end
		for _, x in ipairs(p.versions or {}) do
			versions[x.character] = x.version
		end
		for _, x in ipairs(p.rosterChanges or {}) do
			changes[#changes + 1] = x
		end
		for _, x in ipairs(p.onboarding or {}) do
			onboarding[x.character] = math.max(onboarding[x.character] or 0, x.updatedAt or 0)
		end
	end
	local oldest = Now() - KEEP_SECONDS
	for key, e in pairs(GuildkeepDB.profileEdits) do
		if (profiles[key] or -1) >= (e.at or 0) or (e.at or 0) < oldest then
			GuildkeepDB.profileEdits[key] = nil
		end
	end
	for key, e in pairs(GuildkeepDB.talents) do
		if (talents[key] or -1) >= (e.at or 0) then
			GuildkeepDB.talents[key] = nil
		end
	end
	for key, version in pairs(GuildkeepDB.versions) do
		if versions[key] == version then
			GuildkeepDB.versions[key] = nil
		end
	end
	local kept = {}
	for _, r in ipairs(GuildkeepDB.rosterChanges) do
		local known = (r.at or 0) < Now() - 30 * 24 * 3600
		for _, s in ipairs(changes) do
			if s.character == r.character and s.kind == r.kind and math.abs((s.at or 0) - (r.at or 0)) <= DUPLICATE_SECONDS then
				known = true
			end
		end
		if not known then
			kept[#kept + 1] = r
		end
	end
	GuildkeepDB.rosterChanges = kept
	local function prune(rows)
		for key, e in pairs(rows or {}) do
			if (onboarding[e.character] or -1) >= (e.at or 0) or (e.at or 0) < oldest then
				rows[key] = nil
			end
		end
	end
	prune(GuildkeepDB.onboardingEdits)
	for _, store in pairs(GuildkeepDB.profilesPeer or {}) do
		prune(store.onboarding)
		for key, e in pairs(store.profiles or {}) do
			if (profiles[key] or -1) >= (e.at or 0) or (e.at or 0) < oldest then
				store.profiles[key] = nil
			end
		end
	end
end

local function List(rows, convert)
	local list
	for key, entry in pairs(rows) do
		local item = convert(key, entry)
		if item then
			list = list or {}
			list[#list + 1] = item
		end
	end
	return list
end

-- Même forme que l'upload du compagnon (UploadProfiles) ; nil s'il n'y a rien.
function P.BuildExport()
	local export = {
		profiles = List(GuildkeepDB.profileEdits, function(_, e)
			return { main = e.main, alts = e.alts, roles = e.roles, availability = e.availability, note = e.note, at = e.at }
		end),
		talents = List(GuildkeepDB.talents, function(character, e)
			return { character = character, spec = e.spec, summary = e.summary, at = e.at }
		end),
		versions = List(GuildkeepDB.versions, function(character, version)
			return { character = character, version = version }
		end),
		rosterChanges = List(GuildkeepDB.rosterChanges, function(_, e)
			return { character = e.character, kind = e.kind, at = e.at, by = e.by }
		end),
		onboarding = List(GuildkeepDB.onboardingEdits, function(_, e)
			return { character = e.character, step = e.step, done = e.done, by = e.by, at = e.at }
		end),
	}
	if not (export.profiles or export.talents or export.versions or export.rosterChanges or export.onboarding) then
		return nil
	end
	return export
end

-- Chargement du module (données) ---------------------------------------------------------------

GP.Modules.Define("profiles", "data", function(ctx)
	P.InitDB()
	P.Prune()
	ctx:OnPeer("pf", Receive)
	ctx:OnPeer("ob", Receive)
	ctx:On("ANNOUNCE_PENDING", P.AnnouncePending)
	ctx:On("EXPORT", function(payload)
		payload.profiles = P.BuildExport()
	end)
	ctx:Listen({ "PLAYER_LOGIN" }, function()
		P.ReportVersion()
		ctx:After(8, P.ScanTalents)
	end)
	ctx:Listen({ "CHARACTER_POINTS_CHANGED" }, function()
		ctx:After(1, P.ScanTalents)
	end)
	-- Le roster change souvent (connexions) : un relevé groupé quelques secondes après.
	local scheduled = false
	ctx:Listen({ "GUILD_ROSTER_UPDATE" }, function()
		if scheduled then
			return
		end
		scheduled = true
		ctx:After(5, function()
			scheduled = false
			P.ScanRoster()
		end)
	end)
end)
