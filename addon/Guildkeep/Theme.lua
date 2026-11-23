local _, GP = ...

-- Thèmes de couleur (socle). L'interface n'emploie que les couleurs
-- sémantiques de la boîte à outils (GP.UI.COLOR) : un thème en change
-- l'accent (titres, onglet choisi, soulignements), la surbrillance et les
-- barres d'avancement, sans toucher aux couleurs qui portent un sens (classe,
-- qualité d'objet, vert pour « fait », rouge pour « absent »).
-- Choix par compte (GuildkeepSettings.theme), dans Options > Extensions >
-- Guildkeep, sur le tableau de bord ou avec /gkeep theme. Préférence
-- d'affichage propre à chaque joueur : elle reste dans le jeu.

GP.Theme = {}
local Theme = GP.Theme

Theme.DEFAULT = "or"
Theme.LIST = {
	{ id = "or", label = "Or de guilde",
		gold = { 1, 0.82, 0 }, selected = { 1, 0.82, 0, 0.14 }, bar = { 0.2, 0.55, 1 }, barMax = { 0.25, 0.8, 0.35 } },
	{ id = "argent", label = "Argent",
		gold = { 0.82, 0.88, 1 }, selected = { 0.75, 0.85, 1, 0.14 }, bar = { 0.45, 0.6, 0.9 }, barMax = { 0.6, 0.85, 1 } },
	{ id = "emeraude", label = "Émeraude",
		gold = { 0.4, 0.95, 0.6 }, selected = { 0.3, 0.9, 0.5, 0.14 }, bar = { 0.2, 0.7, 0.45 }, barMax = { 0.35, 0.9, 0.5 } },
	{ id = "arcanes", label = "Arcanes",
		gold = { 0.8, 0.6, 1 }, selected = { 0.7, 0.45, 1, 0.16 }, bar = { 0.55, 0.35, 0.95 }, barMax = { 0.75, 0.5, 1 } },
	{ id = "braise", label = "Braise",
		gold = { 1, 0.6, 0.25 }, selected = { 1, 0.5, 0.15, 0.15 }, bar = { 0.9, 0.4, 0.15 }, barMax = { 1, 0.65, 0.25 } },
}
local KEYS = { "gold", "selected", "bar", "barMax" }

local BY_ID = {}
for _, def in ipairs(Theme.LIST) do
	BY_ID[def.id] = def
end

local function Normalize(text)
	text = (text or ""):lower()
	return (text:gsub("é", "e"):gsub("è", "e"):gsub("%s+", ""))
end

function Theme.Find(text)
	local wanted = Normalize(text)
	for _, def in ipairs(Theme.LIST) do
		if def.id == wanted or Normalize(def.label) == wanted then
			return def
		end
	end
end

function Theme.Current()
	local id = type(GuildkeepSettings) == "table" and GuildkeepSettings.theme
	return BY_ID[id] or BY_ID[Theme.DEFAULT]
end

-- Copie les couleurs du thème dans la palette commune, en place : les composants
-- qui la consultent au moment de peindre suivent aussitôt.
local function Paint(def)
	local palette = GP.UI and GP.UI.COLOR
	if not palette then
		return
	end
	for _, key in ipairs(KEYS) do
		local target, source = palette[key], def[key]
		for i = 1, #source do
			target[i] = source[i]
		end
	end
end

-- Au chargement (préférences lues) : le thème choisi avant que la fenêtre ne soit dessinée.
function Theme.Init()
	if type(GuildkeepSettings) == "table" and not BY_ID[GuildkeepSettings.theme] then
		GuildkeepSettings.theme = Theme.DEFAULT
	end
	Paint(Theme.Current())
end

function Theme.Set(id)
	local def = BY_ID[id] or Theme.Find(id)
	if not def then
		return nil, "thème inconnu"
	end
	GuildkeepSettings.theme = def.id
	Paint(def)
	GP.RefreshUI()
	return def
end

-- Thème suivant de la liste (bouton du tableau de bord).
function Theme.Next()
	local current = Theme.Current()
	for i, def in ipairs(Theme.LIST) do
		if def == current then
			return Theme.Set(Theme.LIST[i % #Theme.LIST + 1].id)
		end
	end
	return Theme.Set(Theme.DEFAULT)
end

function Theme.Labels()
	local labels = {}
	for _, def in ipairs(Theme.LIST) do
		labels[#labels + 1] = def.id
	end
	return table.concat(labels, ", ")
end

-- /gkeep theme [nom]
function Theme.Slash(rest)
	rest = strtrim(rest or "")
	if rest == "" then
		GP.Chat(("thème : %s. /gkeep theme <%s>"):format(Theme.Current().label, Theme.Labels()))
		return
	end
	local def, err = Theme.Set(rest)
	if not def then
		GP.Chat(("%s : %s"):format(err, Theme.Labels()))
		return
	end
	GP.Chat(("thème %s appliqué (complet après /reload)."):format(def.label))
end
