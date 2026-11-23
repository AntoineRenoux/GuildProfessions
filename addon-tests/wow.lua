-- Imitation de l'API de WoW pour tester l'addon Guildkeep hors du jeu.
--
-- Chaque « poste » (Wow.New) est un client de jeu isolé : son propre
-- environnement Lua (les fichiers de l'addon y sont chargés dans l'ordre du
-- .toc), ses SavedVariables, ses cadres, ses minuteries et ses messages
-- d'addon. Les cadres de l'interface sont des objets factices qui acceptent
-- toute méthode ; les listes (ScrollBox) appellent vraiment l'initialisation
-- de leurs lignes, pour exercer le code d'affichage avec de vraies données.
--
-- Une API lue par l'addon mais absente d'ici est notée dans `poste.unknown` :
-- les tests vérifient qu'il n'y en a aucune.

local Wow = {}

-- Objets factices (cadres, textures, textes, valeurs inconnues) ----------------------------

local Object = {}
local Methods = {}

local function IsCapitalized(key)
	return type(key) == "string" and key:match("^%u") ~= nil
end

-- Un champ en majuscule inconnu (méthode ou sous-cadre : SetPoint, Inset, Text…)
-- devient un objet factice appelable ; un champ en minuscule reste nil, comme
-- les champs posés par l'addon lui-même (row.icon, row.gpReady…).
Object.__index = function(self, key)
	local method = Methods[key]
	if method then
		return method
	end
	if IsCapitalized(key) then
		local value = setmetatable({ _kind = "Auto", _game = rawget(self, "_game") }, Object)
		rawset(self, key, value)
		return value
	end
	return nil
end
Object.__call = function(self)
	return setmetatable({ _kind = "Auto", _game = rawget(self, "_game") }, Object)
end
local function Zero()
	return 0
end
Object.__add, Object.__sub, Object.__mul, Object.__div, Object.__unm, Object.__mod = Zero, Zero, Zero, Zero, Zero, Zero
Object.__len = Zero
Object.__lt = function() return false end
Object.__le = function() return false end
Object.__concat = function(a, b)
	return (type(a) == "string" and a or "") .. (type(b) == "string" and b or "")
end
Object.__tostring = function(self)
	return "<" .. tostring(rawget(self, "_kind")) .. ">"
end

local function NewObject(game, kind, name, parent)
	local object = setmetatable({ _game = game, _kind = kind, _name = name, _parent = parent, _scripts = {}, _hooks = {},
		_events = {}, _shown = true }, Object)
	if game then
		game.objects[#game.objects + 1] = object
		if name and game.env then
			game.env[name] = object
		end
	end
	return object
end
Wow.NewObject = NewObject

local function RunScript(self, name, ...)
	local scripts = rawget(self, "_scripts")
	if scripts and scripts[name] then
		scripts[name](self, ...)
	end
	for _, hook in ipairs(rawget(self, "_hooks") and self._hooks[name] or {}) do
		hook(self, ...)
	end
end

function Methods:RegisterEvent(event)
	self._events[event] = true
end
function Methods:UnregisterEvent(event)
	self._events[event] = nil
end
function Methods:UnregisterAllEvents()
	self._events = {}
end
function Methods:IsEventRegistered(event)
	return self._events[event] == true
end
function Methods:SetScript(name, fn)
	self._scripts[name] = fn
end
function Methods:GetScript(name)
	return self._scripts[name]
end
function Methods:HookScript(name, fn)
	self._hooks[name] = self._hooks[name] or {}
	table.insert(self._hooks[name], fn)
end
function Methods:Show()
	local was = self._shown
	self._shown = true
	if not was then
		RunScript(self, "OnShow")
	end
end
function Methods:Hide()
	local was = self._shown
	self._shown = false
	if was then
		RunScript(self, "OnHide")
	end
end
function Methods:SetShown(shown)
	if shown then
		self:Show()
	else
		self:Hide()
	end
end
function Methods:IsShown()
	return self._shown == true
end
Methods.IsVisible = Methods.IsShown
function Methods:Click(...)
	RunScript(self, "OnClick", ...)
end
function Methods:SetText(text)
	self._text = text
end
function Methods:GetText()
	return self._text or ""
end
function Methods:GetNumber()
	return tonumber(self._text) or 0
end
function Methods:SetChecked(checked)
	self._checked = checked and true or false
end
function Methods:GetChecked()
	return self._checked == true
end
function Methods:SetEnabled(enabled)
	self._enabled = enabled and true or false
end
function Methods:IsEnabled()
	return self._enabled ~= false
end
function Methods:GetStringWidth()
	return #(self._text or "") * 6
end
function Methods:SetWidth(width)
	self._width = width
end
function Methods:GetWidth()
	return self._width or 100
end
function Methods:GetHeight()
	return 20
end
function Methods:GetSize()
	return self:GetWidth(), 20
end
function Methods:GetLeft() return nil end
function Methods:GetRight() return nil end
function Methods:GetTop() return nil end
function Methods:GetBottom() return nil end
function Methods:GetEffectiveScale()
	return 1
end
function Methods:SetID(id)
	self._id = id
end
function Methods:GetID()
	return self._id or 0
end
function Methods:GetObjectType()
	return self._kind
end
function Methods:GetName()
	return self._name
end
function Methods:GetDebugName()
	return self._name or self._kind
end
function Methods:GetParent()
	return self._parent
end
function Methods:GetChildren() end
function Methods:HasFocus()
	return false
end
function Methods:IsMouseOver()
	return false
end
function Methods:CreateTexture()
	return NewObject(self._game, "Texture", nil, self)
end
function Methods:CreateFontString()
	return NewObject(self._game, "FontString", nil, self)
end
function Methods:CreateAnimationGroup()
	return NewObject(self._game, "AnimationGroup", nil, self)
end
-- Vue de liste (CreateScrollBoxListLinearView) et ScrollBox.
function Methods:SetElementInitializer(_, fn)
	self._init = fn
end
function Methods:SetDataProvider(provider)
	self._items = provider and provider.items or {}
	local view = rawget(self, "_view")
	if view and view._init then
		for _, item in ipairs(self._items) do
			view._init(NewObject(self._game, "Button", nil, self), item)
		end
	end
end
function Methods:Cancel()
	self._cancelled = true
end

-- Sérialisation (C_EncodingUtil) : table Lua sous forme de texte, relue par load.
local function Serialize(value)
	local kind = type(value)
	if kind == "string" then
		return ("%q"):format(value)
	elseif kind == "number" then
		if math.type(value) == "integer" then
			return tostring(value)
		end
		return ("%.17g"):format(value)
	elseif kind == "boolean" or kind == "nil" then
		return tostring(value)
	elseif kind == "table" then
		local parts = {}
		for k, v in pairs(value) do
			parts[#parts + 1] = "[" .. Serialize(k) .. "]=" .. Serialize(v)
		end
		return "{" .. table.concat(parts, ",") .. "}"
	end
	error("type non sérialisable : " .. kind)
end

local function Deserialize(text)
	return assert(load("return " .. text, "message", "t", {}))()
end

-- Poste de jeu ------------------------------------------------------------------------------

local Game = {}
Game.__index = Game

-- options : addonDir, first, last, classFile, guild (nil = hors guilde), officer,
-- settings (GuildkeepSettings), db (GuildkeepDB), serverData, now, skip (fichiers
-- à ne pas charger), noSettingsApi (client sans API Settings moderne).
function Wow.New(options)
	local game = setmetatable({
		options = options,
		prints = {}, errors = {}, sent = {}, prefixes = {}, unknown = {}, objects = {},
		timers = {}, tickers = {}, hooks = {}, settingsCategories = {}, interfacePanels = {},
		now = options.now or 1795000000, clock = 1000,
		first = options.first or "Thorgal", last = options.last or "Marteau",
		classFile = options.classFile or "WARRIOR", guild = options.guild == nil and "FORMER HOPE" or options.guild,
		officer = options.officer or false, roster = options.roster or {},
		professions = options.professions, equipment = options.equipment or {}, quests = options.quests or {},
		raid = nil, -- { instance, members = { { nom, rang, classe, en ligne } } }
		spells = options.spells or {},
		fileErrors = {},
	}, Game)
	game.env = game:BuildEnv()
	return game
end

function Game:SenderName()
	return self.first .. "-" .. self.last
end

function Game:FullName()
	return self.first .. " " .. self.last
end

function Game:BuildEnv()
	local game = self
	local env = {}
	local api = {}

	-- Bibliothèque Lua (celle de WoW : Lua 5.1 et ses alias)
	for _, name in ipairs({ "assert", "error", "ipairs", "next", "pairs", "pcall", "xpcall", "select", "tonumber", "tostring",
		"type", "setmetatable", "getmetatable", "rawget", "rawset", "string", "table", "math" }) do
		api[name] = _G[name]
	end
	api.unpack = table.unpack
	api.date = function(format, t)
		return os.date(format, t)
	end
	api.time = os.time
	api.tinsert = table.insert
	api.wipe = function(t)
		for k in pairs(t) do
			t[k] = nil
		end
		return t
	end
	api.strtrim = function(s)
		return (s:gsub("^%s+", ""):gsub("%s+$", ""))
	end
	api.strsplit = function(delimiter, text, limit)
		local parts, start = {}, 1
		while true do
			if limit and #parts == limit - 1 then
				parts[#parts + 1] = text:sub(start)
				break
			end
			local i = text:find(delimiter, start, true)
			if not i then
				parts[#parts + 1] = text:sub(start)
				break
			end
			parts[#parts + 1] = text:sub(start, i - 1)
			start = i + 1
		end
		return table.unpack(parts)
	end
	api.print = function(...)
		local parts = {}
		for i = 1, select("#", ...) do
			parts[#parts + 1] = tostring((select(i, ...)))
		end
		game.prints[#game.prints + 1] = table.concat(parts, " ")
	end
	api.geterrorhandler = function()
		return function(err)
			game.errors[#game.errors + 1] = tostring(err)
		end
	end
	api.hooksecurefunc = function(name, fn)
		game.hooks[#game.hooks + 1] = { name = name, fn = fn }
	end

	-- Cadres et interface
	api.CreateFrame = function(kind, name, parent, template)
		local frame = NewObject(game, kind, name, parent)
		frame._template = template
		return frame
	end
	api.UIParent = NewObject(game, "Frame", "UIParent")
	api.GameTooltip = NewObject(game, "GameTooltip", "GameTooltip")
	api.RaidWarningFrame = NewObject(game, "Frame", "RaidWarningFrame")
	api.ChatFontNormal = NewObject(game, "Font", "ChatFontNormal")
	api.Item = NewObject(game, "Item", "Item")
	api.GameTooltip_Hide = function() end
	api.UISpecialFrames = {}
	api.StaticPopupDialogs = {}
	api.StaticPopup_Show = function() end
	api.PanelTemplates_SetNumTabs = function() end
	api.PanelTemplates_SetTab = function() end
	api.PanelTemplates_TabResize = function() end
	api.SearchBoxTemplate_OnTextChanged = function() end
	api.SetPortraitToTexture = function() end
	api.PlaySound = function() end
	api.SOUNDKIT = {}
	api.CreateScrollBoxListLinearView = function()
		return NewObject(game, "ScrollBoxListLinearView")
	end
	api.CreateDataProvider = function(items)
		return { items = items }
	end
	api.ScrollBoxConstants = { RetainScrollPosition = 1 }
	api.ScrollUtil = {
		InitScrollBoxListWithScrollBar = function(box, _, view)
			rawset(box, "_view", view)
		end,
		AddManagedScrollBarVisibilityBehavior = function() end,
	}
	api.RAID_CLASS_COLORS = {}
	api.CLASS_ICON_TCOORDS = {}
	api.ITEM_QUALITY_COLORS = {}
	api.LOCALIZED_CLASS_NAMES_MALE = {}
	api.YES, api.NO, api.CANCEL, api.CLOSE = "Oui", "Non", "Annuler", "Fermer"
	api.ChatTypeInfo = { RAID_WARNING = {} }
	api.RaidNotice_AddMessage = function() end
	api.ChatEdit_InsertLink = function() end
	api.ChatFrame_SendTell = function() end
	api.HandleModifiedItemClick = function() end
	api.IsModifiedClick = function() return false end
	api.GetCursorPosition = function() return 0, 0 end
	api.SlashCmdList = {}
	api.C_UI = { Reload = function() game.reloaded = true end }
	api.ReloadUI = function() game.reloaded = true end

	-- Options du jeu
	if not self.options.noSettingsApi then
		api.Settings = self:SettingsApi()
		api.CreateSettingsListSectionHeaderInitializer = function(name)
			return { kind = "header", name = name }
		end
		api.CreateSettingsButtonInitializer = function(name, text, onClick)
			return { kind = "button", name = name, text = text, onClick = onClick }
		end
	else
		api.InterfaceOptions_AddCategory = function(panel)
			game.interfacePanels[#game.interfacePanels + 1] = panel
		end
		api.InterfaceOptionsFrame_OpenToCategory = function(panel)
			game.openedPanel = panel
		end
	end

	-- Temps
	api.GetServerTime = function() return game.now end
	api.GetTime = function() return game.clock end
	api.C_Timer = {
		After = function(seconds, fn)
			game.timers[#game.timers + 1] = { seconds = seconds, fn = fn }
		end,
		NewTicker = function(seconds, fn)
			local ticker = NewObject(game, "Ticker")
			ticker._fn, ticker._seconds = fn, seconds
			game.tickers[#game.tickers + 1] = ticker
			return ticker
		end,
	}

	-- Personnage, guilde, groupe
	api.UnitFullName = function() return game.first, game.last end
	api.UnitName = function() return game.first end
	api.UnitClass = function() return "Classe", game.classFile end
	api.UnitRace = function() return "Humain", "Human", 1 end
	api.UnitSex = function() return 2 end
	api.UnitLevel = function() return 60 end
	api.UnitExists = function() return false end
	api.UnitIsPlayer = function() return false end
	api.UnitInParty = function() return false end
	api.UnitInRaid = function() return false end
	api.UnitIsGroupLeader = function() return game.raid ~= nil end
	api.UnitIsGroupAssistant = function() return false end
	api.GetGuildInfo = function() return game.guild end
	api.IsInGuild = function() return game.guild ~= nil end
	api.IsGuildLeader = function() return false end
	api.C_GuildInfo = {
		IsGuildOfficer = function() return game.officer end,
		GuildRoster = function() end,
	}
	api.GetNumGuildMembers = function() return #game.roster end
	api.GetGuildRosterInfo = function(i)
		local m = game.roster[i]
		if m then
			return m[1], "Rang", m[2], 60, "Classe", "Zone", "", "", m[3] ~= false
		end
	end
	api.InCombatLockdown = function() return false end
	api.IsInGroup = function() return game.raid ~= nil end
	api.IsInRaid = function() return game.raid ~= nil end
	api.IsInInstance = function()
		if game.raid then
			return true, "raid"
		end
		return false, "none"
	end
	api.GetInstanceInfo = function() return game.raid and game.raid.instance end
	api.GetNumGroupMembers = function() return game.raid and #game.raid.members or 0 end
	api.GetRaidRosterInfo = function(i)
		local m = game.raid.members[i]
		return m[1], m[2], 1, 60, "Classe", m[3], "Zone", m[4] ~= false
	end
	api.LE_PARTY_CATEGORY_INSTANCE = 2
	api.C_PartyInfo = { InviteUnit = function() end, ConvertToRaid = function() end }
	api.InviteUnit = function() end
	api.SetLootMethod = function() return true end

	-- Métiers, sorts, objets, équipement
	api.GetProfessions = function()
		if not game.professions then
			return nil
		end
		return 1, 2
	end
	api.GetProfessionInfo = function(index)
		local p = game.professions and game.professions[index]
		if p then
			return p.name, "icone", p.level, p.max, 0, 0, p.skillLine
		end
	end
	api.C_Spell = {
		GetSpellInfo = function(id)
			local name = game.spells[id]
			return name and { name = name } or nil
		end,
		GetSpellTexture = function() return "icone" end,
		GetSpellLink = function() return nil end,
	}
	api.GetItemInfo = function() return nil end
	api.GetItemCount = function() return 0 end
	api.GetInventoryItemLink = function(_, slot) return game.equipment[slot] end
	api.LOOT_ITEM = "%s reçoit le butin : %s."
	api.LOOT_ITEM_MULTIPLE = "%s reçoit le butin : %sx%d."
	api.LOOT_ITEM_SELF = "Vous recevez le butin : %s."
	api.LOOT_ITEM_SELF_MULTIPLE = "Vous recevez le butin : %sx%d."
	api.Enum = {}

	-- Quêtes
	api.C_QuestLog = {
		GetNumQuestLogEntries = function() return #game.quests end,
		GetInfo = function(i) return game.quests[i] end,
		GetQuestTagInfo = function(id)
			for _, q in ipairs(game.quests) do
				if q.questID == id then
					return { tagID = q.tagID, tagName = q.tagName }
				end
			end
		end,
		IsComplete = function() return false end,
		IsQuestFlaggedCompleted = function() return false end,
	}

	-- Messages d'addon
	api.C_ChatInfo = {
		RegisterAddonMessagePrefix = function(prefix)
			game.prefixes[prefix] = true
			return true
		end,
		SendAddonMessage = function(prefix, message, channel)
			game.sent[#game.sent + 1] = { prefix = prefix, message = message, channel = channel }
			return 0
		end,
	}
	api.C_EncodingUtil = {
		SerializeJSON = Serialize,
		DeserializeJSON = Deserialize,
		CompressString = function(s) return s end,
		DecompressString = function(s) return s end,
		-- Encodage sans espace (hexadécimal), comme le base64 du jeu.
		EncodeBase64 = function(s)
			return (s:gsub(".", function(c) return ("%02x"):format(c:byte()) end))
		end,
		DecodeBase64 = function(s)
			return (s:gsub("%x%x", function(h) return string.char(tonumber(h, 16)) end))
		end,
	}

	-- API absentes de ce client imité : nil, comme une API absente du jeu.
	for _, name in ipairs({ "C_Item", "C_Texture", "C_Club", "C_DateAndTime", "C_TradeSkillUI", "CommunitiesFrame", "GuildFrame",
		"GuildRoster", "GetSpellInfo", "GetSpellTexture", "GetItemInfoInstant", "GetDetailedItemLevelInfo", "GetSpecialization",
		"GetSpecializationInfo", "GetNumQuestLogEntries", "GetQuestLogTitle", "GetQuestTagInfo", "QuestLogPushQuest" }) do
		api[name] = false
	end

	-- SavedVariables et données du compagnon
	api.GuildkeepDB = self.options.db
	api.GuildkeepSettings = self.options.settings

	for name, value in pairs(api) do
		if value ~= false then
			env[name] = value
		end
	end
	-- Noms connus même quand ce client imité ne les a pas (variantes d'API).
	local known = { GuildkeepDB = true, GuildkeepSettings = true, Guildkeep_ServerData = true, Settings = true,
		CreateSettingsListSectionHeaderInitializer = true, CreateSettingsButtonInitializer = true,
		InterfaceOptions_AddCategory = true, InterfaceOptionsFrame_OpenToCategory = true }
	for name in pairs(api) do
		known[name] = true
	end
	env._G = env
	setmetatable(env, {
		__index = function(_, name)
			if not known[name] then
				game.unknown[name] = true
			end
			return nil
		end,
	})
	return env
end

-- API Settings moderne : catégories, réglages et cases enregistrés pour les tests.
function Game:SettingsApi()
	local game = self
	local Settings = { VarType = { Boolean = "boolean" } }
	function Settings.RegisterVerticalLayoutCategory(name)
		local category = { name = name, id = #game.settingsCategories + 1, settings = {}, checkboxes = {}, initializers = {} }
		function category:GetID()
			return self.id
		end
		local layout = {}
		function layout:AddInitializer(initializer)
			table.insert(category.initializers, initializer)
		end
		category.layout = layout
		return category, layout
	end
	function Settings.RegisterAddOnSetting(category, variable, key, tbl, varType, name, default)
		assert(type(tbl) == "table", "la table des réglages doit exister")
		assert(varType == "boolean", "réglage booléen attendu")
		if tbl[key] == nil then
			tbl[key] = default
		end
		local setting = { variable = variable, key = key, tbl = tbl, name = name, default = default }
		function setting:GetValue()
			return self.tbl[self.key]
		end
		function setting:SetValue(value)
			self.tbl[self.key] = value
			if self.callback then
				self.callback(self, value)
			end
		end
		function setting:SetValueChangedCallback(callback)
			self.callback = callback
		end
		category.settings[#category.settings + 1] = setting
		return setting
	end
	function Settings.CreateCheckbox(category, setting, tooltip)
		local initializer = { setting = setting, tooltip = tooltip }
		function initializer:SetParentInitializer(parent, predicate)
			self.parent, self.predicate = parent, predicate
		end
		category.checkboxes[#category.checkboxes + 1] = initializer
		return initializer
	end
	function Settings.RegisterAddOnCategory(category)
		game.settingsCategories[#game.settingsCategories + 1] = category
	end
	function Settings.OpenToCategory(id)
		game.openedCategory = id
	end
	return Settings
end

-- Charge les fichiers de l'addon dans l'ordre du .toc. Une erreur dans un
-- fichier est notée et n'empêche pas les suivants, comme dans le jeu.
function Game:Load()
	local addonDir = self.options.addonDir
	local skip = {}
	for _, name in ipairs(self.options.skip or {}) do
		skip[name] = true
	end
	self.GP = {}
	for line in io.lines(addonDir .. "/Guildkeep.toc") do
		local file = line:match("^%s*([^#%s][^%s]*%.lua)%s*$")
		if file and not skip[file] then
			local chunk, err = loadfile(addonDir .. "/" .. file, "t", self.env)
			if not chunk then
				error("fichier illisible " .. file .. " : " .. tostring(err))
			end
			local ok, runErr = pcall(chunk, "Guildkeep", self.GP)
			if not ok then
				self.fileErrors[#self.fileErrors + 1] = file .. " : " .. tostring(runErr)
			end
			-- Data.lua tel que le compagnon l'aurait écrit pour ce test.
			if file == "Data.lua" and self.options.serverData then
				self.env.Guildkeep_ServerData = self.options.serverData
			end
		end
	end
	return self
end

-- Événement du jeu : remis aux cadres abonnés, dans l'ordre de création.
function Game:Fire(event, ...)
	for _, object in ipairs(self.objects) do
		local events = rawget(object, "_events")
		if events and events[event] and object._scripts.OnEvent then
			object._scripts.OnEvent(object, event, ...)
		end
	end
end

-- Chargement de l'addon puis connexion, comme au lancement du jeu.
function Game:Login()
	self:Fire("ADDON_LOADED", "Guildkeep")
	self:Fire("PLAYER_LOGIN")
	return self
end

-- Exécute les minuteries en attente (et celles qu'elles programment).
function Game:RunTimers(maxRounds)
	for _ = 1, maxRounds or 50 do
		if #self.timers == 0 then
			return
		end
		local due = self.timers
		self.timers = {}
		for _, timer in ipairs(due) do
			self.clock = self.clock + (timer.seconds or 0)
			timer.fn()
		end
	end
end

-- Un tic de chaque minuterie répétée encore active.
function Game:Tick()
	for _, ticker in ipairs(self.tickers) do
		if not ticker._cancelled then
			ticker._fn()
		end
	end
end

function Game:Slash(text)
	self.env.SlashCmdList.GUILDKEEP(text)
end

-- Messages d'addon envoyés par ce poste, remis à un autre (puis oubliés).
function Game:DeliverTo(other)
	self:RunTimers()
	local sent = self.sent
	self.sent = {}
	for _, message in ipairs(sent) do
		other:Fire("CHAT_MSG_ADDON", message.prefix, message.message, message.channel, self:SenderName())
	end
	return #sent
end

-- Textes du chat sans les codes de couleur.
function Wow.Plain(text)
	return (text:gsub("|c%x%x%x%x%x%x%x%x", ""):gsub("|r", ""))
end

function Game:PlainPrints()
	local list = {}
	for i, line in ipairs(self.prints) do
		list[i] = Wow.Plain(line)
	end
	return list
end

-- Libellés des onglets de la fenêtre principale (créée au besoin).
function Game:TabLabels()
	local labels = {}
	local i = 1
	while rawget(self.env, "GuildkeepFrameTab" .. i) do
		-- Sans la pastille « (n) » des choses à traiter.
		labels[#labels + 1] = (Wow.Plain(self.env["GuildkeepFrameTab" .. i]._text or ""):gsub(" %(%d+%)$", ""))
		i = i + 1
	end
	return labels
end

return Wow
