#!/usr/bin/env bash
# Empaquette le compagnon pour distribution aux guildeux : exécutable autonome
# + config.json pré-rempli (URL de prod + token de guilde lu sur le VPS).
# Le joueur n'a AUCUNE configuration : dézipper, double-cliquer.
#
# Usage : scripts/package-companion.sh
# Prérequis : SDK .NET 10 (DOTNET_ROOT), accès SSH au VPS (ou GP_GUILD_TOKEN).
set -euo pipefail

cd "$(dirname "$0")/.."

API_URL="${GP_API_URL:-https://gp.warpvault.com}"
VPS="${GP_VPS:-root@195.200.15.142}"

if [ -z "${GP_GUILD_TOKEN:-}" ]; then
	echo "Lecture du token de guilde sur le VPS ($VPS)..."
	GP_GUILD_TOKEN=$(ssh "$VPS" "grep '^API_GUILD_TOKEN=' /opt/guildprofessions/.env | cut -d= -f2")
fi
if [ -z "$GP_GUILD_TOKEN" ]; then
	echo "Token de guilde introuvable (définis GP_GUILD_TOKEN)." >&2
	exit 1
fi

rm -rf publish/companion-dist

# Zip de l'addon (le dossier à la racine, règle CurseForge/manuelle).
mkdir -p publish/companion-dist
(cd addon && zip -qr ../publish/companion-dist/GuildProfessions-addon.zip GuildProfessions)
echo "  → publish/companion-dist/GuildProfessions-addon.zip"

for RID in win-x64 linux-x64 osx-x64 osx-arm64; do
	echo "— publication $RID..."
	dotnet publish companion -c Release -r "$RID" --self-contained \
		-p:PublishSingleFile=true -p:PublishTrimmed=false \
		-o "publish/companion-dist/$RID" --nologo -v quiet
	cat > "publish/companion-dist/$RID/config.json" <<EOF
{
	"apiBaseUrl": "$API_URL",
	"guildToken": "$GP_GUILD_TOKEN",
	"uploadToken": "",
	"wowPath": "",
	"pollMinutes": 10,
	"autoStart": true
}
EOF
	rm -f "publish/companion-dist/$RID"/*.pdb
	(cd publish/companion-dist && zip -qr "GuildProfessionsCompanion-$RID.zip" "$RID")
	echo "  → publish/companion-dist/GuildProfessionsCompanion-$RID.zip"
done

echo "Terminé. Distribue le zip : dézipper puis lancer GuildProfessionsCompanion, c'est tout."
