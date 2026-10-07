# GuildProfessions

Annuaire des métiers de guilde pour **WoW: Forever**, alimenté par Discord et synchronisé en jeu.

Trois composants :

| Composant | Rôle |
|---|---|
| [`addon/GuildProfessions/`](addon/GuildProfessions/) | Addon Lua (TOC 16001) : fenêtre « Guilde+ » à deux onglets — Métiers (filtres niveau/patron, commandes de craft) et Membres (rendu 3D de l'équipement) |
| [`server/`](server/) | Bot Discord (Discord.Net) + API HTTP (ASP.NET minimal) + SQLite, déployé sur VPS |
| [`companion/`](companion/) | App compagnon .NET sur le PC du joueur : écrit `Data.lua` (serveur → jeu) et uploade les `SavedVariables` (jeu → serveur) |

Un addon WoW n'a accès ni au réseau ni au disque : le compagnon fait le pont, sur le modèle de GatherMate2 et du WeakAuras Companion. Les notifications temps réel entre joueurs connectés passent par les addon messages (canal guilde).

**Toute la conception est dans [PLAN.md](PLAN.md)** : architecture, modèle de données, commandes du bot, contrats d'API, scanners de l'addon, ordre de réalisation et stratégie de vérification.

## Démarrage rapide (cible)

- **Joueur** : installer l'addon dans `Interface/AddOns/`, lancer le compagnon avec son token (`/token` sur Discord), `/gp` en jeu.
- **Serveur (VPS)** : `cp .env.example .env`, remplir les tokens, puis `docker compose up -d --build`.

## Dev local (serveur)

```bash
# SDK .NET 10 requis (installé dans ~/.dotnet10 sur le poste principal)
export DOTNET_ROOT=$HOME/.dotnet10 PATH=$HOME/.dotnet10:$PATH
dotnet test GuildProfessions.slnx          # tests de la règle de fusion
dotnet run --project server                # API sur http://localhost:5000, bot off sans Discord:Token
dotnet dotnet-ef migrations add <Nom> --project server/GuildProfessions.Server.csproj
```

L'API répond sur `GET /healthz`, `GET /api/v1/export` (header `X-Guild-Token`) et `POST /api/v1/upload` (header `X-Upload-Token`, délivré par la commande Discord `/token`).

## Configurer le bot : test puis prod

Le bot enregistre ses commandes sur les serveurs listés dans `Discord:GuildIds`
(IDs séparés par des virgules). Le passage test → prod consiste à **ajouter**
l'ID du serveur de guilde, sans rien changer d'autre :

| Environnement | Où configurer | Exemple |
|---|---|---|
| Test local (`dotnet run`) | `server/appsettings.Local.json` (gitignoré, modèle : `appsettings.Local.json.example`) | `"GuildIds": "111111111111111111"` |
| VPS (docker compose) | `.env` | `DISCORD_GUILD_IDS=111111111111111111,222222222222222222` |

Création du bot (une fois) : [discord.com/developers/applications](https://discord.com/developers/applications)
→ New Application → onglet **Bot** → Reset Token (= `Discord:Token`). Invitation sur un serveur :
`https://discord.com/oauth2/authorize?client_id=<APPLICATION_ID>&scope=bot+applications.commands&permissions=51200`
(51200 = envoyer des messages, intégrer des liens, joindre des fichiers).

## Distribuer le compagnon (zéro config pour les joueurs)

```bash
scripts/package-companion.sh
```

Le script publie les exécutables autonomes win-x64 et linux-x64 et glisse à côté un
`config.json` **pré-rempli** (URL de prod `https://gp.warpvault.com` + token de guilde lu
sur le VPS). Résultat : deux zips dans `publish/companion-dist/`. Côté joueur : **dézipper,
lancer `GuildProfessionsCompanion`, c'est tout** — chemin WoW auto-détecté (Windows et
Steam/Proton), aucun compte, aucun token à saisir. L'upload passe par le token de guilde
partagé ; un `uploadToken` personnel (`/token` sur Discord) reste possible mais optionnel.

Le compagnon tourne en tâche de fond : il écrit `Data.lua` quand les données serveur
changent (puis `/reload` en jeu) et uploade les SavedVariables à chaque déconnexion//reload.
L'API de prod est servie en HTTPS par le Caddy du VPS (bloc dans `/opt/entracte/deploy/Caddyfile`).

## État d'avancement

- [x] Étape 0 — dépôt, structure, plan fondateur
- [x] Étape 1 — addon, onglet Métiers sur données mock *(code livré — à valider en jeu sur le client Forever)*
- [x] Étape 2 — serveur (bot + API + SQLite) *(7 tests verts + smoke test curl — à connecter à un vrai bot Discord)*
- [x] Étape 3 — compagnon (down-sync + up-sync) *(boucle E2E validée en local : SavedVariables → upload → export → Data.lua)*
- [x] Étape 4 — commandes de craft *(E2E validé : commande en jeu → serveur → Data.lua ; toasts addon message à valider à deux comptes)*
- [x] Étape 4bis — canal manuel sans compagnon : chaînes d'import/export (`/export-addon`, `/import`, boutons Importer/Exporter dans `/gp`) *(codec à valider en jeu : C_EncodingUtil sur Forever)*
- [x] Étape 5 — onglet Membres / armurerie *(scan équipement + rendu 3D DressUpModel — à valider en jeu)*
