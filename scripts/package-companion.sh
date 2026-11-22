#!/usr/bin/env bash
# Empaquette Guildkeep pour distribution :
# - Guildkeep-addon.zip : l'addon seul (installation manuelle) ;
# - GuildkeepCompanion-{système}.zip : l'exécutable du compagnon, pour sa mise à jour
#   automatique (les compagnons installés n'en prennent que l'exécutable) ;
# - Guildkeep-AllInOne-{système}.zip : le pack modèle, sans guilde. Le serveur
#   le personnalise à chaque téléchargement (/api/v1/pack/{code}/{système}) en
#   y mettant le config.json de la guilde : aucun token n'est empaqueté ici.
# - companion-manifest.json : versions et SHA-256, consulté par les compagnons
#   installés pour se mettre à jour seuls (exécutable et addon).
#
# Usage : scripts/package-companion.sh [--deploy]
#   --deploy : envoie zips + manifeste sur le VPS → mise à jour auto des compagnons.
# Prérequis : SDK .NET 10 (DOTNET_ROOT), accès SSH au VPS pour --deploy.
set -euo pipefail

cd "$(dirname "$0")/.."

SITE_URL="${GP_SITE_URL:-https://guildkeep.warpvault.com}"
VPS="${GP_VPS:-root@195.200.15.142}"
DEPLOY=false
[ "${1:-}" = "--deploy" ] && DEPLOY=true

# Version monotone : 1.0.<nombre de commits>. Un commit = une version plus récente.
VERSION="1.0.$(git rev-list --count HEAD)"
if [ -n "$(git status --porcelain -- companion addon)" ]; then
	echo "⚠ companion/ ou addon/ a des modifications non commitées : commite d'abord, sinon la version $VERSION ne correspondra pas au code." >&2
	exit 1
fi

OUT=publish/companion-dist
rm -rf "$OUT"
mkdir -p "$OUT"

# Zip de l'addon (le dossier à la racine, règle CurseForge/manuelle), avec la
# version inscrite dans le .toc : c'est elle que le compagnon compare.
STAGE=$(mktemp -d)
cp -r addon/Guildkeep "$STAGE/"
# -i.bak : syntaxe acceptée par le sed GNU (Linux) comme par celui de macOS.
sed -i.bak "s/^## Version:.*/## Version: $VERSION/" "$STAGE/Guildkeep/Guildkeep.toc"
rm -f "$STAGE/Guildkeep/Guildkeep.toc.bak"
(cd "$STAGE" && zip -qr "$OLDPWD/$OUT/Guildkeep-addon.zip" Guildkeep)
rm -rf "$STAGE"
ADDON_SHA=$(sha256sum "$OUT/Guildkeep-addon.zip" | cut -d' ' -f1)
echo "  → $OUT/Guildkeep-addon.zip (v$VERSION)"

ASSETS=""
for RID in win-x64 linux-x64 osx-x64 osx-arm64; do
	echo "— publication $RID (v$VERSION)..."
	dotnet publish companion -c Release -r "$RID" --self-contained \
		-p:PublishSingleFile=true -p:PublishTrimmed=false -p:Version="$VERSION" \
		-o "$OUT/$RID" --nologo -v quiet
	rm -f "$OUT/$RID"/*.pdb

	# Mise à jour automatique : l'exécutable seul.
	ZIP="GuildkeepCompanion-$RID.zip"
	(cd "$OUT/$RID" && zip -qr "../$ZIP" GuildkeepCompanion*)
	SHA=$(sha256sum "$OUT/$ZIP" | cut -d' ' -f1)
	ASSETS="$ASSETS${ASSETS:+,}
		\"$RID\": { \"url\": \"$SITE_URL/downloads/$ZIP\", \"sha256\": \"$SHA\" }"
	echo "  → $OUT/$ZIP"

	# Pack modèle : exécutable + config.json sans guilde (remplacé par le serveur).
	BUNDLE_DIR="$OUT/bundle-$RID/Guildkeep"
	mkdir -p "$BUNDLE_DIR"
	cp "$OUT/$RID"/GuildkeepCompanion* "$BUNDLE_DIR/"
	cat > "$BUNDLE_DIR/config.json" <<EOF
{
	"apiBaseUrl": "$SITE_URL",
	"guildName": "",
	"guildToken": "",
	"uploadToken": "",
	"wowPath": "",
	"pollMinutes": 10,
	"autoStart": true,
	"autoUpdate": true,
	"installAddon": true
}
EOF
	case "$RID" in
		win-x64)
			START_FR="Il se lance ensuite tout seul au démarrage de Windows."
			START_EN="It then starts by itself with Windows."
			MAC_FR=""
			MAC_EN="" ;;
		osx-*)
			START_FR="Laisse-le tourner pendant que tu joues."
			START_EN="Leave it running while you play."
			MAC_FR="
Mac : la première fois, macOS refuse de l'ouvrir (app non notarisée par Apple).
Autorise-le une seule fois, au choix :
  - lance-le, puis Réglages Système → Confidentialité et sécurité,
    tout en bas : « Ouvrir quand même » ;
  - ou dans le Terminal : xattr -dr com.apple.quarantine ~/Downloads/Guildkeep
    (adapte le chemin si tu as dézippé ailleurs).
Lancement : cd ~/Downloads/Guildkeep && ./GuildkeepCompanion
"
			MAC_EN="
Mac: the first time, macOS refuses to open it (app not notarized by Apple).
Allow it once, either way:
  - run it, then System Settings → Privacy & Security,
    at the bottom: \"Open Anyway\";
  - or in Terminal: xattr -dr com.apple.quarantine ~/Downloads/Guildkeep
    (adjust the path if you unzipped elsewhere).
Run: cd ~/Downloads/Guildkeep && ./GuildkeepCompanion
" ;;
		*)
			START_FR="Laisse-le tourner pendant que tu joues (chmod +x GuildkeepCompanion && ./GuildkeepCompanion)."
			START_EN="Leave it running while you play (chmod +x GuildkeepCompanion && ./GuildkeepCompanion)."
			MAC_FR=""
			MAC_EN="" ;;
	esac
	cat > "$BUNDLE_DIR/LISEZMOI-README.txt" <<EOF
Guildkeep — pack de ta guilde / your guild's pack
$MAC_FR
1. Lance GuildkeepCompanion.
2. C'est tout : il trouve World of Warcraft, installe l'addon Guildkeep,
   synchronise tes métiers avec ta guilde, et tient l'addon et lui-même à jour.
   $START_FR
   Personnages dans plusieurs guildes ? Lance aussi le pack de chacune : un seul
   GuildkeepCompanion les synchronise toutes.

En jeu : /gkeep, ou l'onglet Guildkeep dans la fenêtre de guilde.
Sur Discord : /gkeep aide. Il ne te demandera jamais de mot de passe ni d'e-mail.

---
$MAC_EN
1. Run GuildkeepCompanion.
2. That's it: it finds World of Warcraft, installs the Guildkeep addon, syncs
   your professions with your guild, and keeps the addon and itself up to date.
   $START_EN
   Characters in several guilds? Run each guild's pack too: a single
   GuildkeepCompanion syncs them all.

In game: /gkeep, or the Guildkeep tab in the guild window.
On Discord: /gkeep help. It will never ask for a password or an email.
EOF
	TEMPLATE="Guildkeep-AllInOne-$RID.zip"
	(cd "$OUT/bundle-$RID" && zip -qr "../$TEMPLATE" Guildkeep)
	rm -rf "$OUT/bundle-$RID"
	echo "  → $OUT/$TEMPLATE (modèle, personnalisé par le serveur)"
done

cat > "$OUT/companion-manifest.json" <<EOF
{
	"version": "$VERSION",
	"publishedUtc": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
	"assets": {$ASSETS
	},
	"addon": { "version": "$VERSION", "url": "$SITE_URL/downloads/Guildkeep-addon.zip", "sha256": "$ADDON_SHA" }
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
