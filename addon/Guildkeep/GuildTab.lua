local _, GP = ...

-- Accès à l'addon depuis la fenêtre « Guilde et communautés » : un onglet
-- latéral de plus, sous ceux de Blizzard (Discussion, Membres, Infos…).
--
-- On ne suppose ni le nom ni le nombre des onglets Blizzard (ils varient selon
-- le client) : on repère les CheckButton visibles collés au bord droit de la
-- fenêtre, et on se place sous le plus bas, à la même taille et au même
-- espacement. La mise en page est recalculée tant que la fenêtre est ouverte
-- (changement de club, mode réduit…). Repli sur l'ancienne GuildFrame.

local tabs = {}
local LAYOUT_INTERVAL = 0.25

local function UpdateChecked()
	local shown = GP.IsMainFrameShown and GP.IsMainFrameShown() or false
	for _, tab in ipairs(tabs) do
		tab:SetChecked(shown)
	end
end

GP.OnMainFrameVisibilityChanged = UpdateChecked

local function ShowTooltip(self)
	GameTooltip:SetOwner(self, "ANCHOR_RIGHT")
	GameTooltip:SetText("Guildkeep", 1, 1, 1)
	GameTooltip:AddLine("Métiers, équipement et commandes de craft de la guilde.", nil, nil, nil, true)
	local count = GP.CountOrdersToHandle and GP.CountOrdersToHandle() or 0
	if count > 0 then
		GameTooltip:AddLine(("%d commande%s à traiter"):format(count, count > 1 and "s" or ""), 1, 0.82, 0)
	end
	GameTooltip:AddLine("Aussi : /gkeep", 0.62, 0.62, 0.62)
	GameTooltip:Show()
end

-- Facture des onglets latéraux Blizzard : icône dans le cadre SpellBook-SkillLineTab.
local function CreateSideTab(parent, name)
	local tab = CreateFrame("CheckButton", name, parent)
	tab:SetSize(32, 32)

	tab.background = tab:CreateTexture(nil, "BACKGROUND")
	tab.background:SetTexture("Interface\\SpellBook\\SpellBook-SkillLineTab")

	tab.icon = tab:CreateTexture(nil, "ARTWORK")
	tab.icon:SetTexture(GP.ICON)
	tab.icon:SetTexCoord(0.06, 0.94, 0.06, 0.94)

	local highlight = tab:CreateTexture(nil, "HIGHLIGHT")
	highlight:SetTexture("Interface\\Buttons\\ButtonHilight-Square")
	highlight:SetBlendMode("ADD")
	highlight:SetAllPoints(tab.icon)

	local checked = tab:CreateTexture(nil, "OVERLAY")
	checked:SetTexture("Interface\\Buttons\\CheckButtonHilight")
	checked:SetBlendMode("ADD")
	checked:SetAllPoints(tab.icon)
	tab:SetCheckedTexture(checked)

	tab:SetScript("OnClick", function()
		PlaySound(SOUNDKIT.IG_CHARACTER_INFO_TAB)
		GP.ToggleMainFrame(parent)
		UpdateChecked()
	end)
	tab:SetScript("OnEnter", ShowTooltip)
	tab:SetScript("OnLeave", GameTooltip_Hide)

	tabs[#tabs + 1] = tab
	return tab
end

-- Adapte l'onglet à la taille des onglets voisins (le cadre déborde de l'icône).
local function ResizeTab(tab, size)
	if tab.size == size then
		return
	end
	tab.size = size
	tab:SetSize(size, size)
	local ratio = size / 32
	tab.background:ClearAllPoints()
	tab.background:SetSize(64 * ratio, 64 * ratio)
	tab.background:SetPoint("TOPLEFT", -3 * ratio, 11 * ratio)
	tab.icon:ClearAllPoints()
	tab.icon:SetAllPoints()
end

-- Onglets latéraux Blizzard visibles : CheckButton enfants dont le bord gauche
-- touche ou dépasse le bord droit de la fenêtre. Triés de haut en bas.
local function FindSideTabs(frame, exclude)
	local right = frame:GetRight()
	local found = {}
	if not right then
		return found
	end
	for _, child in ipairs({ frame:GetChildren() }) do
		local kind = child:GetObjectType()
		if child ~= exclude and (kind == "CheckButton" or kind == "Button") and child:IsShown() then
			local left, top = child:GetLeft(), child:GetTop()
			local width, height = child:GetSize()
			if left and top and left >= right - 8 and width <= 64 and height <= 72 then
				found[#found + 1] = child
			end
		end
	end
	table.sort(found, function(a, b) return a:GetTop() > b:GetTop() end)
	return found
end

local function IsGuildSelected(frame)
	local clubId = frame.GetSelectedClubId and frame:GetSelectedClubId()
	if clubId and C_Club and C_Club.GetClubInfo and Enum and Enum.ClubType then
		local info = C_Club.GetClubInfo(clubId)
		if info then
			return info.clubType == Enum.ClubType.Guild
		end
	end
	return IsInGuild()
end

local function LayoutCommunitiesTab(frame, tab)
	local siblings = IsGuildSelected(frame) and FindSideTabs(frame, tab) or {}
	local last = siblings[#siblings]
	if not last then
		tab:Hide()
		return
	end
	local gap = 20
	if #siblings >= 2 then
		gap = math.floor(siblings[#siblings - 1]:GetBottom() - last:GetTop() + 0.5)
	end
	ResizeTab(tab, math.floor(math.min(last:GetWidth(), last:GetHeight()) + 0.5))
	if tab.anchor ~= last or tab.gap ~= gap then
		tab.anchor, tab.gap = last, gap
		tab:ClearAllPoints()
		tab:SetPoint("TOP", last, "BOTTOM", 0, -gap)
	end
	tab:Show()
end

local function AttachToCommunities()
	local frame = CommunitiesFrame
	if not frame or frame.GuildkeepTab then
		return
	end
	local tab = CreateSideTab(frame, "GuildkeepCommunitiesTab")
	tab:Hide()
	frame.GuildkeepTab = tab

	local elapsed = LAYOUT_INTERVAL
	frame:HookScript("OnShow", function()
		elapsed = 0
		LayoutCommunitiesTab(frame, tab)
		UpdateChecked()
	end)
	-- Les onglets Blizzard changent après coup (club sélectionné, mode réduit) :
	-- on suit leur état à faible cadence tant que la fenêtre est ouverte.
	frame:HookScript("OnUpdate", function(_, delta)
		elapsed = elapsed + delta
		if elapsed >= LAYOUT_INTERVAL then
			elapsed = 0
			LayoutCommunitiesTab(frame, tab)
		end
	end)
	if frame:IsShown() then
		LayoutCommunitiesTab(frame, tab)
	end
end

local function AttachToGuildFrame()
	local frame = GuildFrame
	if not frame or frame.GuildkeepTab then
		return
	end
	local tab = CreateSideTab(frame, "GuildkeepGuildFrameTab")
	tab:SetPoint("TOPLEFT", frame, "TOPRIGHT", 0, -48)
	frame.GuildkeepTab = tab
end

local function TryAttach()
	AttachToCommunities()
	AttachToGuildFrame()
end

-- Diagnostic en jeu : /gkeep debug
function GP.DebugGuildTab()
	local frame = CommunitiesFrame
	print("|cff33ff99Guildkeep|r — diagnostic de l'onglet de guilde")
	if not frame then
		print("  CommunitiesFrame absente (fenêtre de guilde jamais ouverte ?)")
		return
	end
	local tab = frame.GuildkeepTab
	print(("  fenêtre %s, guilde sélectionnée : %s"):format(frame:IsShown() and "ouverte" or "fermée", tostring(IsGuildSelected(frame))))
	local detected = {}
	for _, child in ipairs(FindSideTabs(frame, tab)) do
		detected[child] = true
	end
	-- Tous les enfants à droite de la fenêtre, retenus ou non comme onglets.
	local right = frame:GetRight() or 0
	for _, child in ipairs({ frame:GetChildren() }) do
		local left = child:GetLeft()
		if child ~= tab and left and left >= right - 8 then
			print(("  %s %s (%s) %dx%d %s"):format(detected[child] and "[onglet]" or "[ignoré]",
				child:GetDebugName() or "?", child:GetObjectType(), child:GetWidth(), child:GetHeight(),
				child:IsShown() and "visible" or "masqué"))
		end
	end
	if tab then
		print(("  notre onglet : %s, %dx%d"):format(tab:IsShown() and "visible" or "masqué", tab:GetWidth(), tab:GetHeight()))
	else
		print("  notre onglet : non créé")
	end
end

local loader = CreateFrame("Frame")
loader:RegisterEvent("PLAYER_LOGIN")
loader:RegisterEvent("ADDON_LOADED")
loader:SetScript("OnEvent", function(_, event, addonName)
	if event == "PLAYER_LOGIN" or addonName == "Blizzard_Communities" or addonName == "Blizzard_GuildUI" then
		TryAttach()
	end
end)
-- Fenêtre déjà chargée avant nous (chargement non différé sur ce client).
TryAttach()
