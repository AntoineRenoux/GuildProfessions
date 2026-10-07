-- Données de démonstration (étape 1 du plan).
-- En production, ce fichier est régénéré par l'app compagnon à partir de
-- GET /api/v1/export ; ne jamais y stocker d'état local (il est écrasé).
GuildProfessions_ServerData = {
	version = 1,
	mock = true,
	generatedUtc = "2026-10-07 12:00",

	-- Résolution des noms de recettes côté données : l'addon tente d'abord
	-- C_Spell.GetSpellInfo, puis retombe sur cette table.
	recipeNames = {
		[17187] = "Transmutation : arcanite",
		[17635] = "Flacon des Titans",
		[17556] = "Potion de soins majeure",
		[17570] = "Potion de mana majeure",
		[16729] = "Heaume Cœur de lion",
		[21161] = "Marteau de Sulfuron",
		[16745] = "Cuirasse en thorium",
		[20034] = "Enchantement d'arme (Croisé)",
		[20036] = "Enchantement de bracelets (Intelligence supérieure)",
		[18560] = "Étoffe lunaire",
		[18405] = "Robe en tisse-sort",
		[14046] = "Sac en étoffe runique",
		[12754] = "Fusil à triple canon",
		[15633] = "Gyrocoptère de poche",
		[19068] = "Jambières en peau du Chaos",
		[18245] = "Côtelette de sanglier épicée",
	},

	characters = {
		{
			name = "Thorgal", classFile = "WARRIOR", level = 60,
			professions = {
				{ name = "Forge", level = 300, max = 300, note = "Dispo mardi soir", source = "discord", recipes = { 16729, 21161, 16745 } },
				{ name = "Minage", level = 300, max = 300, source = "discord" },
			},
		},
		{
			name = "Elaria", classFile = "MAGE", level = 60,
			professions = {
				{ name = "Couture", level = 300, max = 300, source = "addon", scannedAt = "2026-10-06 21:12", recipes = { 18560, 18405, 14046 } },
				{ name = "Enchantement", level = 285, max = 300, source = "addon", scannedAt = "2026-10-06 21:12", recipes = { 20034, 20036 } },
			},
		},
		{
			name = "Grimbart", classFile = "PALADIN", level = 60,
			professions = {
				{ name = "Alchimie", level = 295, max = 300, note = "Transmute CD dispo sur demande", source = "discord", recipes = { 17187, 17635, 17556 } },
				{ name = "Herboristerie", level = 300, max = 300, source = "discord" },
			},
		},
		{
			name = "Nayra", classFile = "PRIEST", level = 58,
			professions = {
				{ name = "Couture", level = 240, max = 300, source = "discord", recipes = { 14046 } },
				{ name = "Secourisme", level = 225, max = 300, source = "discord" },
			},
		},
		{
			name = "Sylvek", classFile = "HUNTER", level = 60,
			professions = {
				{ name = "Ingénierie", level = 290, max = 300, source = "discord", recipes = { 12754, 15633 } },
				{ name = "Minage", level = 275, max = 300, source = "discord" },
			},
		},
		{
			name = "Mirana", classFile = "DRUID", level = 60,
			professions = {
				{ name = "Travail du cuir", level = 300, max = 300, source = "discord", recipes = { 19068 } },
				{ name = "Dépeçage", level = 300, max = 300, source = "discord" },
			},
		},
		{
			name = "Dorn", classFile = "ROGUE", level = 55,
			professions = {
				{ name = "Cuisine", level = 300, max = 300, note = "Cuisine de raid, fournir les ingrédients", source = "discord", recipes = { 18245 } },
				{ name = "Pêche", level = 150, max = 225, source = "discord" },
			},
		},
		{
			name = "Aldric", classFile = "WARLOCK", level = 60,
			professions = {
				{ name = "Alchimie", level = 260, max = 300, source = "discord", recipes = { 17556, 17570 } },
				{ name = "Herboristerie", level = 290, max = 300, source = "discord" },
			},
		},
	},
}
