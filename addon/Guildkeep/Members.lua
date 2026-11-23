local _, GP = ...

-- Module Membres, partie données : relève l'équipement porté par le personnage
-- connecté (GuildkeepDB.characters[nom].equipment), affiché dans l'armurerie
-- de l'onglet Membres (MembersUI.lua).

local function ScanEquipment()
	local char = GP.GetLocalCharacter()
	local equipment = {}
	for slot = 1, 19 do
		local link = GetInventoryItemLink("player", slot)
		if link then
			-- Clés en chaîne : sérialisation JSON stable (pas de table mixte).
			equipment[tostring(slot)] = link
		end
	end
	char.equipment = equipment
	GP.RefreshUI()
end

GP.Modules.Define("members", "data", function(ctx)
	ctx:On("FULL_SCAN", ScanEquipment)
	ctx:Listen({ "PLAYER_EQUIPMENT_CHANGED" }, function()
		if GP.GateEligibility() then
			ScanEquipment()
		end
	end)
end)
