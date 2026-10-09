#!/usr/bin/env bash
# Empaquette le compagnon pour distribution aux guildeux : exécutable autonome
# + config.json pré-rempli (URL de prod + token de guilde lu sur le VPS).
# Le joueur n'a AUCUNE configuration : dézipper, double-cliquer.
#
# Produit aussi companion-manifest.json (version + SHA-256 de chaque zip), que
# les compagnons déjà installés consultent pour se mettre à jour tout seuls.
#
# Usage : scripts/package-companion.sh [--deploy]
#   --deploy : envoie zips + manifeste sur le VPS → mise à jour auto des compagnons.
# Prérequis : SDK .NET 10 (DOTNET_ROOT), accès SSH au VPS (ou GP_GUILD_TOKEN).
set -euo pipefail

cd "$(dirname "$0")/.."

API_URL="${GP_API_URL:-https://gp.warpvault.com}"
SITE_URL="${GP_SITE_URL:-https://formerhopesupport.warpvault.com}"
VPS="${GP_VPS:-root@195.200.15.142}"
DEPLOY=false
[ "${1:-}" = "--deploy" ] && DEPLOY=true

# Version monotone : 1.0.<nombre de commits>. Un commit = une version plus récente.
VERSION="1.0.$(git rev-list --count HEAD)"
if [ -n "$(git status --porcelain -- companion addon)" ]; then
	echo "⚠ companion/ ou addon/ a des modifications non commitées : commite d'abord, sinon la version $VERSION ne correspondra pas au code." >&2
	exit 1
fi

if [ -z "${GP_GUILD_TOKEN:-}" ]; then
	echo "Lecture du token de guilde sur le VPS ($VPS)..."
	GP_GUILD_TOKEN=$(ssh "$VPS" "grep '^API_GUILD_TOKEN=' /opt/guildprofessions/.env | cut -d= -f2")
fi
if [ -z "$GP_GUILD_TOKEN" ]; then
	echo "Token de guilde introuvable (définis GP_GUILD_TOKEN)." >&2
	exit 1
fi

OUT=publish/companion-dist
rm -rf "$OUT"
mkdir -p "$OUT"

# Zip de l'addon (le dossier à la racine, règle CurseForge/manuelle), avec la
# version inscrite dans le .toc : c'est elle que le pack tout-en-un compare.
STAGE=$(mktemp -d)
cp -r addon/GuildProfessions "$STAGE/"
sed -i "s/^## Version:.*/## Version: $VERSION/" "$STAGE/GuildProfessions/GuildProfessions.toc"
(cd "$STAGE" && zip -qr "$OLDPWD/$OUT/GuildProfessions-addon.zip" GuildProfessions)
rm -rf "$STAGE"
ADDON_SHA=$(sha256sum "$OUT/GuildProfessions-addon.zip" | cut -d' ' -f1)
echo "  → $OUT/GuildProfessions-addon.zip (v$VERSION)"

ASSETS=""
for RID in win-x64 linux-x64 osx-x64 osx-arm64; do
	echo "— publication $RID (v$VERSION)..."
	dotnet publish companion -c Release -r "$RID" --self-contained \
		-p:PublishSingleFile=true -p:PublishTrimmed=false -p:Version="$VERSION" \
		-o "$OUT/$RID" --nologo -v quiet
	cat > "$OUT/$RID/config.json" <<EOF
{
	"apiBaseUrl": "$API_URL",
	"guildToken": "$GP_GUILD_TOKEN",
	"uploadToken": "",
	"wowPath": "",
	"pollMinutes": 10,
	"autoStart": true,
	"autoUpdate": true
}
EOF
	rm -f "$OUT/$RID"/*.pdb
	ZIP="GuildProfessionsCompanion-$RID.zip"
	(cd "$OUT" && zip -qr "$ZIP" "$RID")
	SHA=$(sha256sum "$OUT/$ZIP" | cut -d' ' -f1)
	ASSETS="$ASSETS${ASSETS:+,}
		\"$RID\": { \"url\": \"$SITE_URL/downloads/$ZIP\", \"sha256\": \"$SHA\" }"
	echo "  → $OUT/$ZIP"

	# Pack tout-en-un : le même exécutable, réglé pour installer l'addon et le
	# tenir à jour. Ses mises à jour suivent le zip compagnon ci-dessus (seul
	# l'exécutable est remplacé, la config du pack est conservée).
	BUNDLE_DIR="$OUT/bundle-$RID/GuildProfessions"
	mkdir -p "$BUNDLE_DIR"
	cp "$OUT/$RID"/GuildProfessionsCompanion* "$BUNDLE_DIR/"
	cat > "$BUNDLE_DIR/config.json" <<EOF
{
	"apiBaseUrl": "$API_URL",
	"guildToken": "$GP_GUILD_TOKEN",
	"uploadToken": "",
	"wowPath": "",
	"pollMinutes": 10,
	"autoStart": true,
	"autoUpdate": true,
	"installAddon": true
}
EOF
	# Le démarrage automatique n'existe que sous Windows (clé Run).
	if [ "$RID" = "win-x64" ]; then
		STARTUP="Il se lance ensuite tout seul au démarrage de Windows."
	else
		STARTUP="Laisse-le tourner pendant que tu joues (lancement : chmod +x GuildProfessionsCompanion && ./GuildProfessionsCompanion)."
	fi
	cat > "$BUNDLE_DIR/LISEZMOI.txt" <<EOF
GuildProfessions — pack tout-en-un (FORMER HOPE)

1. Lance GuildProfessionsCompanion.
2. C'est tout : il trouve World of Warcraft, installe l'addon GuildProfessions,
   synchronise tes métiers avec la guilde, et tient l'addon et lui-même à jour.
   $STARTUP

En jeu : /gp. Si WoW était ouvert pendant l'installation : /reload.
Aide : $SITE_URL — ou ping Zesso sur Discord.
Il ne te demandera jamais de mot de passe ni d'e-mail.
EOF
	BUNDLE_ZIP="GuildProfessions-AllInOne-$RID.zip"
	(cd "$OUT/bundle-$RID" && zip -qr "../$BUNDLE_ZIP" GuildProfessions)
	rm -rf "$OUT/bundle-$RID"
	echo "  → $OUT/$BUNDLE_ZIP"
done

cat > "$OUT/companion-manifest.json" <<EOF
{
	"version": "$VERSION",
	"publishedUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
	"assets": {$ASSETS
	},
	"addon": { "version": "$VERSION", "url": "$SITE_URL/downloads/GuildProfessions-addon.zip", "sha256": "$ADDON_SHA" }
}
EOF
echo "  → $OUT/companion-manifest.json (v$VERSION)"

if $DEPLOY; then
	echo "— envoi sur le VPS..."
	ssh "$VPS" "mkdir -p /opt/guildprofessions/downloads"
	# Zips d'abord, manifeste en dernier : un compagnon ne voit jamais une
	# version annoncée dont le zip n'est pas encore en ligne.
	scp -q "$OUT"/*.zip "$VPS:/opt/guildprofessions/downloads/"
	scp -q "$OUT/companion-manifest.json" "$VPS:/opt/guildprofessions/downloads/"
	echo "Publié : les compagnons installés passeront en v$VERSION au démarrage ou sous 6 h."
else
	echo "Terminé (non publié). Relance avec --deploy pour mettre à jour les compagnons."
fi
