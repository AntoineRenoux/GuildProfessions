local _, GP = ...

-- Module Tableau de bord : premier onglet de la fenêtre, un résumé de ce qui
-- arrive et de ce qui t'attend. Il ne connaît aucun autre module : chacun
-- ajoute sa carte par le crochet HOME_CARDS du socle (seuls les modules actifs
-- sont appelés, chacun protégé : un module en panne n'a pas de carte, les
-- autres s'affichent). Le bouton de thème change les couleurs de la fenêtre.
--
-- Carte : { id, order, title, lines = { … }, tab = clé de l'onglet à ouvrir }.

local UI = GP.UI
local COLOR, Paint, Label = UI.COLOR, UI.Paint, UI.Label

GP.Home = {}
local H = GP.Home

H.MAX_CARDS = 6
H.MAX_LINES = 4
local CARD_HEIGHT = 112

-- Cartes des modules actifs, dans l'ordre.
function H.Cards()
	local cards = {}
	GP.Modules.Fire("HOME_CARDS", cards)
	local valid = {}
	for _, card in ipairs(cards) do
		if type(card) == "table" and type(card.title) == "string" then
			card.lines = type(card.lines) == "table" and card.lines or {}
			valid[#valid + 1] = card
		end
	end
	table.sort(valid, function(a, b) return (a.order or 100) < (b.order or 100) end)
	return valid
end

local panel

local function CreateCard(parent)
	local card = CreateFrame("Frame", nil, parent)
	card:SetHeight(CARD_HEIGHT)
	card.bg = card:CreateTexture(nil, "BACKGROUND")
	card.bg:SetAllPoints()
	card.bg:SetColorTexture(unpack(COLOR.panel))
	card.accent = card:CreateTexture(nil, "ARTWORK")
	card.accent:SetPoint("TOPLEFT")
	card.accent:SetPoint("BOTTOMLEFT")
	card.accent:SetWidth(2)
	card.title = Label(card, nil, "GameFontNormal")
	card.title:SetPoint("TOPLEFT", 10, -8)
	card.title:SetPoint("RIGHT", -84, 0)
	card.title:SetWordWrap(false)
	card.open = UI.Button(card, "Ouvrir", 72, function(self)
		if self.tab then
			GP.ShowTab(self.tab)
		end
	end)
	card.open:SetPoint("TOPRIGHT", -6, -5)
	card.text = Label(card, nil, "GameFontHighlightSmall")
	card.text:SetPoint("TOPLEFT", card.title, "BOTTOMLEFT", 0, -8)
	card.text:SetPoint("RIGHT", -10, 0)
	card.text:SetJustifyH("LEFT")
	card.text:SetJustifyV("TOP")
	card.text:SetWordWrap(true)
	return card
end

local function BuildHomeTab(_, parent)
	panel = parent
	panel.greeting = Label(parent, nil, "GameFontNormalLarge")
	panel.greeting:SetPoint("TOPLEFT", 10, -8)
	panel.sub = Label(parent, nil, "GameFontHighlightSmall", COLOR.muted)
	panel.sub:SetPoint("TOPLEFT", panel.greeting, "BOTTOMLEFT", 0, -4)
	panel.theme = UI.Button(parent, "Thème", 150, function()
		GP.Theme.Next()
	end)
	panel.theme:SetPoint("TOPRIGHT", -10, -8)

	-- Deux colonnes de cartes.
	local left = CreateFrame("Frame", nil, parent)
	left:SetPoint("TOPLEFT", 8, -56)
	left:SetPoint("BOTTOMRIGHT", parent, "BOTTOM", -6, 4)
	local right = CreateFrame("Frame", nil, parent)
	right:SetPoint("TOPLEFT", parent, "TOP", 6, -56)
	right:SetPoint("BOTTOMRIGHT", -8, 4)
	panel.cards = {}
	for i = 1, H.MAX_CARDS do
		local column = (i % 2 == 1) and left or right
		local card = CreateCard(column)
		local row = math.floor((i - 1) / 2)
		card:SetPoint("TOPLEFT", 0, -row * (CARD_HEIGHT + 10))
		card:SetPoint("RIGHT", 0, 0)
		panel.cards[i] = card
	end
	panel.empty = Label(parent, "Rien à afficher pour l'instant : active des modules dans Options > Extensions > Guildkeep.",
		"GameFontHighlight", COLOR.muted)
	panel.empty:SetPoint("CENTER")
	panel.tutorialTargets = { theme = panel.theme, firstCard = panel.cards[1], firstOpen = panel.cards[1].open }
end

local function RefreshHome()
	local guild = GP.GetPlayerGuildName()
	panel.greeting:SetText(("Bonjour %s"):format(UnitName("player") or ""))
	panel.sub:SetText(guild and (("%s · %s"):format(guild, date("%d/%m/%Y"))) or "hors guilde")
	panel.theme:SetText("Thème : " .. GP.Theme.Current().label)
	local cards = H.Cards()
	for i, frame in ipairs(panel.cards) do
		local card = cards[i]
		frame:SetShown(card ~= nil)
		if card then
			frame.accent:SetColorTexture(unpack(COLOR.gold))
			frame.title:SetText(card.title)
			local lines = {}
			for j = 1, math.min(#card.lines, H.MAX_LINES) do
				lines[j] = card.lines[j]
			end
			frame.text:SetText(#lines > 0 and table.concat(lines, "\n") or Paint(COLOR.muted, "Rien de neuf."))
			frame.open.tab = card.tab
			frame.open:SetShown(card.tab ~= nil)
		end
	end
	panel.empty:SetShown(#cards == 0)
end

GP.Modules.Define("home", "ui", function(ctx)
	ctx:Tutorial("home", {
		tab = "tabHome",
		title = "Tableau de bord",
		steps = {
			{ target = function() return panel and panel.cards and panel.cards[1] end, title = "Les cartes",
				text = "Une carte par module : les prochains événements et ta réponse, les annonces non lues, tes commandes, ta présence, ton accueil…",
				hint = "Les cartes apparaissent avec les modules activés." },
			{ target = function() return panel and panel.cards and panel.cards[1] and panel.cards[1].open end, title = "Ouvrir",
				text = "« Ouvrir » t'emmène dans l'onglet du module pour agir : répondre à un événement, lire une annonce, avancer une commande.",
				hint = "Aucune carte pour l'instant." },
			{ target = function() return panel and panel.theme end, title = "Thème",
				text = "Change les couleurs de la fenêtre (or, argent, émeraude, arcanes, braise). Aussi dans Options > Extensions > Guildkeep ou avec /gkeep theme." },
		},
	})
	ctx:Slash({ "tableau", "home", "dashboard" }, function()
		GP.ShowTab("tabHome")
	end)
	ctx:AddTab({
		label = "Tableau",
		key = "tabHome",
		order = 5,
		build = BuildHomeTab,
		refresh = RefreshHome,
	})
end)
