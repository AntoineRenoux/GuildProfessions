local _, GP = ...

-- Panneau Guildkeep dans les options du jeu (Options > Extensions) : une case
-- par module, enregistrée par compte (GuildkeepSettings.modules). API Settings
-- moderne (liste verticale) ; repli sur un panneau dessiné à la main si elle
-- manque ou change. Le choix s'applique au prochain chargement de l'interface.

local M = GP.Modules

local category -- catégorie enregistrée, pour l'ouvrir depuis /gkeep options

local RELOAD_HINT = "Pris en compte au prochain /reload."

local function Tooltip(def)
	return def.description .. "\n\n" .. RELOAD_HINT
end

local function ReloadInterface()
	if C_UI and C_UI.Reload then
		C_UI.Reload()
	elseif ReloadUI then
		ReloadUI()
	end
end

-- Le jeu écrit la case dans GuildkeepSettings, puis prévient : on relit la
-- valeur un instant après, quelle que soit la forme du rappel.
local function WatchSetting(setting, variable, id)
	local function changed()
		C_Timer.After(0, function()
			M.OnPreferenceChanged(id, M.IsEnabled(id))
		end)
	end
	if setting.SetValueChangedCallback then
		setting:SetValueChangedCallback(changed)
	elseif Settings.SetOnValueChangedCallback then
		Settings.SetOnValueChangedCallback(variable, changed)
	end
end

local function CreateSetting(cat, def)
	local variable = "GUILDKEEP_MODULE_" .. def.id:upper()
	local booleanType = Settings.VarType and Settings.VarType.Boolean or "boolean"
	local setting
	if Settings.RegisterAddOnSetting then
		local ok, result = pcall(Settings.RegisterAddOnSetting, cat, variable, def.id, GuildkeepSettings.modules,
			booleanType, def.label, true)
		setting = ok and result or nil
	end
	if not setting and Settings.RegisterProxySetting then
		local ok, result = pcall(Settings.RegisterProxySetting, cat, variable, booleanType, def.label, true,
			function() return M.IsEnabled(def.id) end,
			function(value) GuildkeepSettings.modules[def.id] = value and true or false end)
		setting = ok and result or nil
	end
	if not setting then
		error("Guildkeep : réglage du module " .. def.id .. " impossible à créer")
	end
	pcall(WatchSetting, setting, variable, def.id)
	return setting
end

local function RegisterModernPanel()
	local createCheckbox = Settings and (Settings.CreateCheckbox or Settings.CreateCheckBox)
	if not (Settings and Settings.RegisterVerticalLayoutCategory and Settings.RegisterAddOnCategory and createCheckbox) then
		return false
	end
	local cat, layout = Settings.RegisterVerticalLayoutCategory("Guildkeep")
	if layout and CreateSettingsListSectionHeaderInitializer then
		pcall(function()
			layout:AddInitializer(CreateSettingsListSectionHeaderInitializer("Modules — pris en compte au prochain /reload",
				"Coche les modules que tu veux utiliser. " .. RELOAD_HINT))
		end)
	end
	local initializers = {}
	for _, def in ipairs(M.LIST) do
		local setting = CreateSetting(cat, def)
		local initializer = createCheckbox(cat, setting, Tooltip(def))
		initializers[def.id] = initializer
		-- Soirée de raid grisée tant que les Événements sont décochés.
		for _, dep in ipairs(def.depends or {}) do
			local parent = initializers[dep]
			if parent and initializer and initializer.SetParentInitializer then
				pcall(initializer.SetParentInitializer, initializer, parent, function()
					return M.IsEnabled(dep)
				end)
			end
		end
	end
	if layout and CreateSettingsButtonInitializer then
		pcall(function()
			layout:AddInitializer(CreateSettingsButtonInitializer("Appliquer les changements", "Recharger l'interface",
				ReloadInterface, "Recharge l'interface pour activer ou désactiver les modules cochés.", true))
		end)
	end
	Settings.RegisterAddOnCategory(cat)
	category = cat
	return true
end

-- Repli : panneau dessiné à la main --------------------------------------------------------

local function BuildPanel()
	local panel = CreateFrame("Frame")
	panel.name = "Guildkeep"

	local title = panel:CreateFontString(nil, "ARTWORK", "GameFontNormalLarge")
	title:SetPoint("TOPLEFT", 16, -16)
	title:SetText("Guildkeep — modules")
	local hint = panel:CreateFontString(nil, "ARTWORK", "GameFontHighlightSmall")
	hint:SetPoint("TOPLEFT", title, "BOTTOMLEFT", 0, -8)
	hint:SetText("Coche les modules que tu veux utiliser. " .. RELOAD_HINT)

	local checks = {}
	local function Refresh()
		for _, def in ipairs(M.LIST) do
			local check = checks[def.id]
			check:SetChecked(M.IsEnabled(def.id))
			local available = true
			for _, dep in ipairs(def.depends or {}) do
				available = available and M.IsEnabled(dep)
			end
			check:SetEnabled(available)
		end
	end

	local previous = hint
	for _, def in ipairs(M.LIST) do
		local check = CreateFrame("CheckButton", nil, panel, "UICheckButtonTemplate")
		check:SetPoint("TOPLEFT", previous, "BOTTOMLEFT", previous == hint and -2 or 0, -8)
		local label = check.Text or check.text
		if label then
			label:SetText(def.label)
		end
		check:SetScript("OnClick", function(self)
			M.SetEnabled(def.id, self:GetChecked() and true or false)
			Refresh()
		end)
		check:SetScript("OnEnter", function(self)
			GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
			GameTooltip:SetText(def.label, 1, 1, 1)
			GameTooltip:AddLine(Tooltip(def), nil, nil, nil, true)
			GameTooltip:Show()
		end)
		check:SetScript("OnLeave", GameTooltip_Hide)
		checks[def.id] = check
		previous = check
	end

	local reload = CreateFrame("Button", nil, panel, "UIPanelButtonTemplate")
	reload:SetSize(180, 22)
	reload:SetPoint("TOPLEFT", previous, "BOTTOMLEFT", 2, -16)
	reload:SetText("Recharger l'interface")
	reload:SetScript("OnClick", ReloadInterface)

	panel:SetScript("OnShow", Refresh)
	return panel
end

local function RegisterCanvasPanel()
	local panel = BuildPanel()
	if Settings and Settings.RegisterCanvasLayoutCategory and Settings.RegisterAddOnCategory then
		local cat = Settings.RegisterCanvasLayoutCategory(panel, "Guildkeep")
		Settings.RegisterAddOnCategory(cat)
		category = cat
	elseif InterfaceOptions_AddCategory then
		InterfaceOptions_AddCategory(panel)
		category = panel
	end
end

-- Appelé par le registre après le chargement des modules (ADDON_LOADED).
function M.RegisterSettingsPanel()
	local ok, done = pcall(RegisterModernPanel)
	if ok and done then
		return
	end
	if not ok then
		local handler = geterrorhandler and geterrorhandler()
		if handler then
			handler(done)
		end
	end
	RegisterCanvasPanel()
end

-- /gkeep options
function M.OpenSettings()
	if not category then
		GP.Chat("panneau d'options indisponible : /gkeep modules pour choisir les modules.")
		return
	end
	if Settings and Settings.OpenToCategory and category.GetID then
		Settings.OpenToCategory(category:GetID())
	elseif InterfaceOptionsFrame_OpenToCategory then
		InterfaceOptionsFrame_OpenToCategory(category)
	end
end
