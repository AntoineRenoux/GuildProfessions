-- Tests hors jeu de l'addon Guildkeep (modules indépendants et activables).
-- Usage : lua addon-tests/run.lua [dossier de l'addon]
-- Par défaut : addon/Guildkeep à côté de ce dossier. Code de sortie 1 en cas d'échec.

local here = (arg[0]:match("^(.*)[/\\]") or ".")
package.path = here .. "/?.lua;" .. package.path

local T = require("helpers")
T.addonDir = arg[1] or (here .. "/../addon/Guildkeep")

local files = { "test_chargement", "test_desactivation", "test_echec", "test_fonctionnel", "test_anciens", "test_verification", "test_communication" }
local passed, failed = 0, 0
for _, name in ipairs(files) do
	local tests = require(name)
	print(("== %s"):format(name))
	for _, test in ipairs(tests) do
		local ok, err = xpcall(test.fn, debug.traceback)
		if ok then
			passed = passed + 1
			print("  ok      " .. test.name)
		else
			failed = failed + 1
			print("  ÉCHEC   " .. test.name .. "\n" .. tostring(err))
		end
	end
end
print(("%d test(s) réussi(s), %d en échec"):format(passed, failed))
os.exit(failed == 0 and 0 or 1)
