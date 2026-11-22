local _, GP = ...

-- Accès à l'addon depuis la fenêtre de guilde : un onglet latéral de plus,
-- sous ceux de Blizzard (Discussion, Membres, Avantages, Infos de guilde).
-- La fenêtre Guilde & Communautés (Blizzard_Communities) est chargée à la
-- demande : on s'y greffe à son chargement. Repli sur l'ancienne GuildFrame
-- si le client l'utilise.

local tabs = {}

local function UpdateChecked()
	local shown = GP.IsMainFrameShown and GP.IsMainFrameShown() or false
	for _, tab in ipairs(tabs) do
		tab:SetChecked(shown)
	end
end

GP.OnMainFrameVisibilityChanged = UpdateChecked

local function ShowTooltip(self)
	GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
	GameTooltip:SetText("GuildProfessions", 1, 1, 1)
	GameTooltip:AddLine("Métiers, équipement et commandes de craft de la guilde.", nil, nil, nil, true)
	local count = GP.CountOrdersToHandle and GP.CountOrdersToHandle() or 0
	if count > 0 then
		GameTooltip:AddLine(("%d commande%s à traiter"):format(count, count > 1 and "s" or ""), 1, 0.82, 0)
	end
	GameTooltip:AddLine("Aussi : /gp", 0.62, 0.62, 0.62)
	GameTooltip:Show()
end

-- Même facture que les onglets latéraux Blizzard (fond SpellBook-SkillLineTab).
local function CreateSideTab(parent, name)
	local tab = CreateFrame("CheckButton", name, parent)
	tab:SetSize(32, 32)

	local background = tab:CreateTexture(nil, "BACKGROUND")
	background:SetTexture("Interface\\SpellBook\\SpellBook-SkillLineTab")
	background:SetSize(64, 64)
	background:SetPoint("TOPLEFT", -3, 11)

	tab:SetNormalTexture(GP.ICON)
	tab:SetHighlightTexture("Interface\\Buttons\\ButtonHilight-Square", "ADD")
	tab:SetCheckedTexture("Interface\\Buttons\\CheckButtonHilight", "ADD")

	tab:SetScript("OnClick", function()
		PlaySound(SOUNDKIT.IG_CHARACTER_INFO_TAB)
		GP.ToggleMainFrame(parent)
		UpdateChecked()
	end)
	tab:SetScript("OnEnter", ShowTooltip)
	tab:SetScript("OnLeave", GameTooltip_Hide)

	tabs[#tabs + 1] = tab
	UpdateChecked()
	return tab
end

local function AttachToCommunities()
	local frame = CommunitiesFrame
	if not frame or frame.GuildProfessionsTab then
		return
	end
	-- Dernier onglet de guilde présent : notre onglet se place dessous.
	local anchor
	for _, key in ipairs({ "GuildInfoTab", "GuildBenefitsTab", "RosterTab", "ChatTab" }) do
		if frame[key] then
			anchor = frame[key]
			break
		end
	end
	if not anchor then
		return
	end
	local tab = CreateSideTab(frame, "GuildProfessionsCommunitiesTab")
	tab:SetPoint("TOP", anchor, "BOTTOM", 0, -20)
	-- Mêmes règles d'affichage que les onglets de guilde : masqué sur une
	-- communauté (hors guilde) et en mode réduit.
	local function sync()
		tab:SetShown(anchor:IsShown())
	end
	anchor:HookScript("OnShow", sync)
	anchor:HookScript("OnHide", sync)
	sync()
	frame.GuildProfessionsTab = tab
end

local function AttachToGuildFrame()
	local frame = GuildFrame
	if not frame or frame.GuildProfessionsTab then
		return
	end
	local tab = CreateSideTab(frame, "GuildProfessionsGuildFrameTab")
	tab:SetPoint("TOPLEFT", frame, "TOPRIGHT", 0, -48)
	frame.GuildProfessionsTab = tab
end

local loader = CreateFrame("Frame")
loader:RegisterEvent("PLAYER_LOGIN")
loader:RegisterEvent("ADDON_LOADED")
loader:SetScript("OnEvent", function(_, event, addonName)
	if event == "PLAYER_LOGIN" or addonName == "Blizzard_Communities" then
		AttachToCommunities()
	end
	if event == "PLAYER_LOGIN" or addonName == "Blizzard_GuildUI" then
		AttachToGuildFrame()
	end
end)
