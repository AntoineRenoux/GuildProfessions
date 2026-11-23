local _, GP = ...

-- Module Artisanat, partie données : commandes de craft, offres des artisans
-- et catalogue des recettes de la guilde.
--
-- Deux façons de commander : directement à un artisan, ou par une demande
-- ouverte à tous (crafter = nil) sur laquelle les artisans proposent ; le
-- demandeur retient une offre. Puis l'artisan signale « prête » (échange en
-- main propre) ou « envoyée » (courrier), et le demandeur confirme la
-- réception. Une demande ouverte expire au bout de 5 jours.
--
-- Trois sources, comme pour les événements : le serveur, ce que les autres
-- joueurs connectés ont annoncé (GuildkeepDB.orderPeer, jamais renvoyé au
-- serveur) et les changements faits ici (orders, orderActions, orderOffers).
-- Entre joueurs connectés : messages d'addon GPROF sur le canal de guilde ;
-- chacun garde ce qu'il reçoit jusqu'à ce que le serveur le reflète, seul
-- l'auteur l'envoie au serveur.

GP.Craft = {}
local Craft = GP.Craft

local PREFIX = "GPROF"

function Craft.InitDB()
	GuildkeepDB.orders = GuildkeepDB.orders or {}
	GuildkeepDB.orderActions = GuildkeepDB.orderActions or {}
	GuildkeepDB.orderOffers = GuildkeepDB.orderOffers or {}
	GuildkeepDB.orderPeer = GuildkeepDB.orderPeer or {}
end

-- Messages envoyés aux joueurs connectés ---------------------------------------------------

-- Un champ ne doit contenir ni tabulation (séparateur) ni retour à la ligne.
local function Field(value, max)
	value = tostring(value or ""):gsub("[\t\r\n]", " ")
	return max and value:sub(1, max) or value
end

local function Send(parts)
	local fields = {}
	for i, part in ipairs(parts) do
		fields[i] = Field(part)
	end
	local message = table.concat(fields, "\t")
	if #message > 250 then
		return
	end
	GP.Comm.Send(PREFIX, message)
end

local function SendNewOrder(order, key)
	Send({ "NEW", order.crafter or "", order.requester, Field(order.item, 60), order.qty or 1, key or "",
		order.recipeId or "", Field(order.note, 60) })
end

local function SendStatus(order, status, at)
	Send({ "UPD", order.requester, Field(order.item, 60), status, GP.GetPlayerFullName(), order.id and tostring(order.id) or order.key,
		order.crafter or "", at or "" })
end

local function SendOffer(order, entry)
	Send({ "OFF", entry.order, order.requester, entry.crafter, entry.withdrawn and "1" or "0", entry.at, Field(entry.note, 60) })
end

-- Commandes ------------------------------------------------------------------------------

Craft.ORDER_OPEN_SECONDS = 5 * 24 * 3600
local ACTIVE = { open = true, accepted = true, ready = true, mailed = true }
Craft.ORDER_ACTIVE = ACTIVE

local function ServerNow()
	return GP.Now()
end

local function OrderPeer()
	local guild = GP.GuildKey(GP.GetPlayerGuildName())
	if not guild then
		return { orders = {}, offers = {}, actions = {} }
	end
	GuildkeepDB.orderPeer = GuildkeepDB.orderPeer or {}
	local store = GuildkeepDB.orderPeer[guild] or {}
	GuildkeepDB.orderPeer[guild] = store
	store.orders, store.offers, store.actions = store.orders or {}, store.offers or {}, store.actions or {}
	return store
end
Craft.OrderPeer = OrderPeer

-- « 2026-11-23 10:00 » (UTC, export serveur) → secondes Unix.
local function ParseUtc(text)
	local y, m, d, h, mi = (text or ""):match("(%d+)-(%d+)-(%d+) (%d+):(%d+)")
	if not y then
		return nil
	end
	local t = time({ year = tonumber(y), month = tonumber(m), day = tonumber(d), hour = tonumber(h), min = tonumber(mi), sec = 0 })
	-- time() lit l'heure locale : on corrige du décalage avec l'UTC.
	local utcNow, localNow = time(date("!*t")), time()
	return t + (localNow - utcNow)
end

function Craft.CreateOrder(crafter, item, qty, note, recipeId)
	local requester = GP.GetPlayerFullName()
	local localId = requester .. "-" .. time() .. "-" .. math.random(1000, 9999)
	local order = {
		requester = requester,
		crafter = crafter,
		item = item,
		qty = qty or 1,
		note = note and strtrim(note) ~= "" and strtrim(note):sub(1, 120) or nil,
		recipeId = recipeId,
		createdAt = date("%Y-%m-%d %H:%M"),
		createdTs = ServerNow(),
	}
	GuildkeepDB.orders[localId] = order
	SendNewOrder(order, localId)
	GP.RefreshUI()
	return localId
end

local function CopyOffers(list)
	local offers = {}
	for _, offer in ipairs(list or {}) do
		offers[offer.crafter] = { crafter = offer.crafter, note = offer.note, at = 0 }
	end
	return offers
end

-- Liste fusionnée pour l'affichage. Chaque commande : key (id serveur en
-- texte, ou id client), requester, crafter (nil = demande ouverte), item,
-- qty, note, status, offers (liste), pendingSync, expiresTs.
function Craft.GetOrders()
	local list, byKey = {}, {}
	local function add(order)
		list[#list + 1] = order
		byKey[order.key] = order
		if order.clientId then
			byKey[order.clientId] = order
		end
	end

	for _, o in ipairs(GP.GetServerData().orders or {}) do
		add({
			key = tostring(o.id), id = o.id, clientId = o.clientId, requester = o.requester, crafter = o.crafter,
			item = o.item, qty = o.qty or o.quantity or 1, note = o.note, status = o.status, recipeId = o.recipeId,
			createdAt = o.createdUtc, expiresTs = ParseUtc(o.expiresUtc), offerMap = CopyOffers(o.offers), statusAt = 0,
		})
	end
	local peer = OrderPeer()
	local function addLocal(key, o, pending)
		if byKey[key] then
			return
		end
		add({
			key = key, clientId = key, requester = o.requester, crafter = o.crafter, item = o.item, qty = o.qty or 1,
			note = o.note, status = "open", recipeId = o.recipeId, createdAt = o.createdAt, offerMap = {}, statusAt = 0,
			expiresTs = (not o.crafter and o.createdTs) and (o.createdTs + Craft.ORDER_OPEN_SECONDS) or nil, pendingSync = pending,
		})
	end
	for key, o in pairs(peer.orders) do
		addLocal(key, o, false)
	end
	for key, o in pairs(GuildkeepDB.orders) do
		if GP.IsInCurrentGuild(o.requester) then
			addLocal(key, o, true)
		end
	end

	-- Offres : celles annoncées ou faites ici remplacent celles du serveur (plus récentes).
	local function applyOffer(entry, pending)
		local order = byKey[entry.order]
		if not order then
			return
		end
		local known = order.offerMap[entry.crafter]
		if not known or (entry.at or 0) > (known.at or 0) then
			order.offerMap[entry.crafter] = {
				crafter = entry.crafter, note = entry.note, withdrawn = entry.withdrawn, at = entry.at or 0, pendingSync = pending,
			}
		end
	end
	for _, entry in pairs(peer.offers) do
		applyOffer(entry, false)
	end
	for _, entry in pairs(GuildkeepDB.orderOffers) do
		applyOffer(entry, true)
	end

	-- Changements d'état : le plus récent d'ici ou d'un autre joueur, par-dessus le serveur.
	local function applyAction(key, action, pending)
		local order = byKey[key]
		if order and (action.at or 0) >= order.statusAt and action.status ~= order.status then
			order.status = action.status
			order.statusAt = action.at or 0
			if action.crafter then
				order.crafter = action.crafter
			end
			order.pendingSync = order.pendingSync or pending
		end
	end
	for key, action in pairs(peer.actions) do
		applyAction(key, action, false)
	end
	for key, action in pairs(GuildkeepDB.orderActions) do
		applyAction(key, action, true)
	end

	local now = ServerNow()
	for _, order in ipairs(list) do
		if order.status == "open" and order.expiresTs and order.expiresTs < now then
			order.status = "expired"
		end
		order.offers = {}
		for _, offer in pairs(order.offerMap) do
			if not offer.withdrawn then
				order.offers[#order.offers + 1] = offer
			end
		end
		table.sort(order.offers, function(a, b) return a.crafter < b.crafter end)
		order.offerMap = nil
	end
	table.sort(list, function(a, b)
		return (a.createdAt or "") > (b.createdAt or "")
	end)
	return list
end

-- Clé de référence d'une commande pour les changements : l'id serveur s'il existe.
local function OrderRef(order)
	return order.id and tostring(order.id) or order.key
end

function Craft.SetOrderStatus(order, status, crafter)
	local previous = GuildkeepDB.orderActions[order.key]
	GuildkeepDB.orderActions[order.key] = {
		status = status, crafter = crafter, at = math.max(ServerNow(), (previous and previous.at or 0) + 1),
	}
	order.status, order.crafter = status, crafter or order.crafter
	SendStatus(order, status, GuildkeepDB.orderActions[order.key].at)
	GP.RefreshUI()
end

function Craft.SetOffer(order, note, withdrawn)
	local me = GP.GetPlayerFullName()
	local key = OrderRef(order) .. "|" .. me
	local previous = GuildkeepDB.orderOffers[key]
	local entry = {
		order = OrderRef(order), crafter = me, withdrawn = withdrawn and true or false,
		note = note and strtrim(note) ~= "" and strtrim(note):sub(1, 120) or (previous and previous.note) or nil,
		at = math.max(ServerNow(), (previous and previous.at or 0) + 1),
	}
	GuildkeepDB.orderOffers[key] = entry
	SendOffer(order, entry)
	GP.RefreshUI()
end

function Craft.MyOffer(order)
	local me = GP.GetPlayerFullName()
	for _, offer in ipairs(order.offers or {}) do
		if offer.crafter == me then
			return offer
		end
	end
end

-- Recettes connues des personnages de ce compte dans la guilde affichée : ids et noms.
function Craft.MyKnownRecipes()
	local ids, names = {}, {}
	for name, char in pairs(GuildkeepDB.characters) do
		if GP.IsInCurrentGuild(name) then
			for _, prof in pairs(char.professions or {}) do
				for _, id in ipairs(prof.recipes or {}) do
					ids[id] = true
					local recipeName = GP.GetRecipeName(id)
					if recipeName then
						names[recipeName:lower()] = true
					end
				end
			end
		end
	end
	return ids, names
end

function Craft.CanMake(order, known)
	local ids, names = known and known.ids, known and known.names
	if not ids then
		ids, names = Craft.MyKnownRecipes()
	end
	return (order.recipeId and ids[order.recipeId]) or (order.item and names[order.item:lower()]) or false
end

-- Catalogue des recettes de la guilde : serveur, plus tout ce que ce poste a vu.
-- id → { name, profession }
function Craft.GetCatalog()
	local catalog = {}
	for id, entry in pairs(GP.GetServerData().catalog or {}) do
		local spellId = tonumber(id)
		if spellId and type(entry) == "table" then
			catalog[spellId] = { name = entry[1], profession = entry[2] }
		end
	end
	for name, char in pairs(GuildkeepDB.characters) do
		if GP.IsInCurrentGuild(name) then
			for profName, prof in pairs(char.professions or {}) do
				for _, list in ipairs({ prof.catalog or {}, prof.recipes or {} }) do
					for _, id in ipairs(list) do
						local recipeName = GP.GetRecipeName(id)
						if recipeName and not catalog[id] then
							catalog[id] = { name = recipeName, profession = profName }
						end
					end
				end
			end
		end
	end
	-- Recettes connues des autres membres (annuaire), même sans catalogue serveur.
	for _, member in ipairs(GP.GetRoster()) do
		for _, prof in ipairs(member.professions or {}) do
			for _, id in ipairs(prof.recipes or {}) do
				if not catalog[id] then
					local recipeName = GP.GetRecipeName(id)
					if recipeName then
						catalog[id] = { name = recipeName, profession = prof.name }
					end
				end
			end
		end
	end
	return catalog
end

-- Purge : ce que le serveur reflète déjà, et ce qui est trop vieux.
function Craft.PruneOrders()
	local byKey = {}
	for _, section in ipairs(GP.AllServerSections()) do
		for _, order in ipairs(section.orders or {}) do
			byKey[tostring(order.id)] = order
			if order.clientId then
				byKey[order.clientId] = order
			end
		end
	end
	local oldest = ServerNow() - 14 * 24 * 3600
	local function serverOffer(order, crafter)
		for _, offer in ipairs(order and order.offers or {}) do
			if offer.crafter == crafter then
				return offer
			end
		end
	end
	local function pruneOrders(orders)
		for key, order in pairs(orders) do
			if byKey[key] or (order.createdTs or ServerNow()) < oldest then
				orders[key] = nil
			end
		end
	end
	local function pruneActions(actions)
		for key, action in pairs(actions) do
			local order = byKey[key]
			local reflected = order and order.status == action.status and (not action.crafter or order.crafter == action.crafter)
			local closed = order and not ACTIVE[order.status]
			if reflected or closed or (action.at and action.at < oldest) then
				actions[key] = nil
			end
		end
	end
	local function pruneOffers(offers)
		for key, entry in pairs(offers) do
			local order = byKey[entry.order]
			local shown = serverOffer(order, entry.crafter)
			local reflected = order and ((entry.withdrawn and not shown) or (not entry.withdrawn and shown))
			if reflected or (order and order.status ~= "open") or (entry.at or 0) < oldest then
				offers[key] = nil
			end
		end
	end
	pruneOrders(GuildkeepDB.orders)
	pruneActions(GuildkeepDB.orderActions)
	pruneOffers(GuildkeepDB.orderOffers)
	for _, store in pairs(GuildkeepDB.orderPeer or {}) do
		pruneOrders(store.orders or {})
		pruneActions(store.actions or {})
		pruneOffers(store.offers or {})
	end
end

-- Export manuel (même forme que l'upload du compagnon) -------------------------------------

local function BuildExport(payload)
	local orders
	for localId, order in pairs(GuildkeepDB.orders) do
		orders = orders or {}
		orders[#orders + 1] = {
			clientId = localId,
			requester = order.requester,
			crafter = order.crafter,
			item = order.item,
			quantity = order.qty or 1,
			note = order.note,
			createdAt = order.createdAt,
			recipeId = order.recipeId,
		}
	end

	local orderActions
	for key, action in pairs(GuildkeepDB.orderActions) do
		orderActions = orderActions or {}
		local serverId = tonumber(key)
		if serverId then
			orderActions[#orderActions + 1] = { serverId = serverId, status = action.status, crafter = action.crafter }
		else
			orderActions[#orderActions + 1] = { clientId = key, status = action.status, crafter = action.crafter }
		end
	end

	local offers
	for _, entry in pairs(GuildkeepDB.orderOffers or {}) do
		offers = offers or {}
		local orderId = tonumber(entry.order)
		offers[#offers + 1] = {
			orderId = orderId, orderClientId = (not orderId) and entry.order or nil,
			crafter = entry.crafter, note = entry.note, withdrawn = entry.withdrawn and true or false,
		}
	end
	payload.orders, payload.orderActions, payload.offers = orders, orderActions, offers
end

-- Messages reçus des joueurs connectés ---------------------------------------------------------

local STATUS_LABELS = {
	accepted = "acceptée",
	ready = "prête à échanger",
	mailed = "envoyée par courrier",
	done = "reçue",
	cancelled = "annulée",
}
local VALID_STATUS = { accepted = true, ready = true, mailed = true, done = true, cancelled = true }

local function Toast(text)
	print("|cff33ff99Guildkeep|r — " .. text)
	if RaidNotice_AddMessage and RaidWarningFrame then
		pcall(RaidNotice_AddMessage, RaidWarningFrame, text, ChatTypeInfo["RAID_WARNING"])
	end
	if PlaySound and SOUNDKIT and SOUNDKIT.RAID_WARNING then
		pcall(PlaySound, SOUNDKIT.RAID_WARNING)
	end
end

local function FindOrder(key)
	for _, order in ipairs(Craft.GetOrders()) do
		if order.key == key or order.clientId == key then
			return order
		end
	end
end

local function OnMessage(message, sender)
	local kind, a, b, c, d, e, f, g = strsplit("\t", message)
	local me = GP.GetPlayerFullName()
	local peer = OrderPeer()
	if kind == "NEW" then
		-- a=artisan (vide = demande ouverte), b=demandeur, c=objet, d=quantité, e=clé, f=recette, g=note
		if b ~= sender then
			return
		end
		if e and e ~= "" then
			peer.orders[e] = peer.orders[e] or {
				requester = b, crafter = a ~= "" and a or nil, item = c, qty = tonumber(d) or 1,
				recipeId = tonumber(f), note = g ~= "" and g or nil,
				createdAt = date("%Y-%m-%d %H:%M"), createdTs = GetServerTime and GetServerTime() or time(),
			}
		end
		if a == me then
			Toast(("Nouvelle commande de %s : %s× %s — /gkeep pour la voir."):format(b, d or "1", c or "?"))
		elseif (a == nil or a == "") and Craft.CanMake({ item = c, recipeId = tonumber(f) }) then
			Toast(("%s cherche quelqu'un pour %s× %s, et tu sais le faire — /gkeep, onglet Commandes."):format(b, d or "1", c or "?"))
		end
	elseif kind == "UPD" then
		-- a=demandeur, b=objet, c=état, d=auteur, e=clé, f=artisan, g=horodatage
		if d ~= sender or not VALID_STATUS[c] then
			return
		end
		local order = e and e ~= "" and FindOrder(e)
		-- Seuls le demandeur et l'artisan (désigné, ou choisi à l'instant) font avancer une commande.
		local allowed = sender == a or (order and order.crafter == sender) or (c == "accepted" and f == sender)
		if not allowed then
			return
		end
		if e and e ~= "" and tonumber(g) then
			local known = peer.actions[e]
			if not known or tonumber(g) > (known.at or 0) then
				peer.actions[e] = { status = c, crafter = f ~= "" and f or nil, by = sender, at = tonumber(g) }
			end
		end
		if a == me and sender ~= me then
			Toast(("Ta commande « %s » est %s (%s)."):format(b or "?", STATUS_LABELS[c] or c, sender))
		elseif f == me and sender == a then
			Toast(c == "accepted" and ("%s te confie « %s »."):format(sender, b or "?")
				or ("La commande « %s » de %s est %s."):format(b or "?", sender, STATUS_LABELS[c] or c))
		end
	elseif kind == "OFF" then
		-- a=clé de la commande, b=demandeur, c=artisan, d=retirée, e=horodatage, f=note
		if c ~= sender or not a or a == "" then
			return
		end
		local key = a .. "|" .. c
		local at = tonumber(e) or 0
		local known = peer.offers[key]
		if not known or at > (known.at or 0) then
			peer.offers[key] = { order = a, crafter = c, withdrawn = d == "1", at = at, note = f ~= "" and f or nil }
		end
		if b == me and d ~= "1" then
			Toast(("%s propose de faire ta demande%s — /gkeep, onglet Commandes."):format(c, f and f ~= "" and (" : « " .. f .. " »") or ""))
		end
	end
	GP.RefreshUI()
end

GP.Modules.Define("craft", "data", function(ctx)
	Craft.InitDB()
	Craft.PruneOrders()
	ctx:OnAddonMessage(PREFIX, OnMessage)
	ctx:On("EXPORT", BuildExport)
end)
