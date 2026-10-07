local _, GP = ...

-- Chaînes d'import/export (canal manuel, sans compagnon) : même format des
-- deux côtés — "GP1!" .. base64(compression(json)). Le serveur implémente le
-- miroir dans StringCodec.cs. S'appuie sur C_EncodingUtil (API moderne).

local PREFIX = "GP1!"

local function Available()
	return C_EncodingUtil ~= nil
		and C_EncodingUtil.SerializeJSON ~= nil
		and C_EncodingUtil.DeserializeJSON ~= nil
		and C_EncodingUtil.CompressString ~= nil
		and C_EncodingUtil.DecompressString ~= nil
		and C_EncodingUtil.EncodeBase64 ~= nil
		and C_EncodingUtil.DecodeBase64 ~= nil
end

function GP.SerializeAvailable()
	return Available()
end

function GP.EncodeTable(data)
	if not Available() then
		return nil, "C_EncodingUtil indisponible sur ce client"
	end
	local ok, result = pcall(function()
		local json = C_EncodingUtil.SerializeJSON(data)
		local compressed = C_EncodingUtil.CompressString(json)
		return PREFIX .. C_EncodingUtil.EncodeBase64(compressed)
	end)
	if ok then
		return result
	end
	return nil, tostring(result)
end

local function TryDecompress(bytes)
	-- Le format de compression par défaut peut varier d'un client à l'autre :
	-- on tente le défaut puis chaque méthode connue.
	local ok, text = pcall(C_EncodingUtil.DecompressString, bytes)
	if ok and text then
		return text
	end
	if Enum and Enum.CompressionMethod then
		for _, method in pairs(Enum.CompressionMethod) do
			ok, text = pcall(C_EncodingUtil.DecompressString, bytes, method)
			if ok and text then
				return text
			end
		end
	end
	return nil
end

function GP.DecodeString(text)
	if not Available() then
		return nil, "C_EncodingUtil indisponible sur ce client"
	end
	text = (text or ""):gsub("%s+", "")
	if text:sub(1, #PREFIX) ~= PREFIX then
		return nil, "ce n'est pas une chaîne GuildProfessions (préfixe GP1! absent)"
	end
	local ok, result = pcall(function()
		local compressed = C_EncodingUtil.DecodeBase64(text:sub(#PREFIX + 1))
		local json = TryDecompress(compressed)
		if not json then
			error("décompression impossible")
		end
		return C_EncodingUtil.DeserializeJSON(json)
	end)
	if ok and type(result) == "table" then
		return result
	end
	return nil, "chaîne invalide ou incomplète (" .. tostring(result) .. ")"
end

-- Export jeu -> Discord : même forme JSON que l'upload du compagnon
-- (UploadPayload côté serveur, clés camelCase).
function GP.BuildExportPayload()
	local characters = {}
	for name, char in pairs(GuildProfessionsDB.characters) do
		local professions = {}
		for profName, prof in pairs(char.professions or {}) do
			local recipes
			if prof.recipes and #prof.recipes > 0 then
				recipes = {}
				for _, id in ipairs(prof.recipes) do
					recipes[#recipes + 1] = { id = id, name = GP.GetRecipeName(id) }
				end
			end
			professions[#professions + 1] = {
				name = profName,
				level = prof.level or 0,
				max = prof.max or 0,
				scannedAt = prof.scannedAt,
				recipes = recipes,
			}
		end
		characters[#characters + 1] = {
			name = name,
			classFile = char.classFile,
			raceFile = char.raceFile,
			raceId = char.raceId,
			gender = char.gender,
			level = char.level,
			guild = char.guild,
			professions = professions,
		}
	end

	local orders
	for localId, order in pairs(GuildProfessionsDB.orders) do
		orders = orders or {}
		orders[#orders + 1] = {
			clientId = localId,
			requester = order.requester,
			crafter = order.crafter,
			item = order.item,
			quantity = order.qty or 1,
			note = order.note,
			createdAt = order.createdAt,
		}
	end

	local orderActions
	for key, action in pairs(GuildProfessionsDB.orderActions) do
		orderActions = orderActions or {}
		local serverId = tonumber(key)
		if serverId then
			orderActions[#orderActions + 1] = { serverId = serverId, status = action.status }
		else
			orderActions[#orderActions + 1] = { clientId = key, status = action.status }
		end
	end

	return { characters = characters, orders = orders, orderActions = orderActions }
end

function GP.BuildExportString()
	return GP.EncodeTable(GP.BuildExportPayload())
end

-- Import Discord -> jeu : la chaîne contient l'équivalent de Data.lua
-- (ExportDto côté serveur). Persisté en SavedVariables : survit aux /reload.
function GP.ImportString(text)
	local data, err = GP.DecodeString(text)
	if not data then
		return false, err
	end
	if type(data.characters) ~= "table" then
		return false, "contenu inattendu (pas de roster dans la chaîne)"
	end
	data.importedAt = date("%Y-%m-%d %H:%M")
	GuildProfessionsDB.importedServerData = data
	GP.RefreshUI()
	return true
end
