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

## Publier le compagnon pour les guildeux

```bash
# Windows (exécutable autonome unique, aucun prérequis)
dotnet publish companion -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish/win-x64

# Linux (Lutris/Proton)
dotnet publish companion -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64
```

Au premier lancement, le compagnon crée un `config.json` à remplir : `apiBaseUrl` (l'URL du VPS), `guildToken` (fourni par l'officier), `uploadToken` (`/token` sur Discord), `wowPath` (auto-détecté si vide, installations Windows et Steam/Proton). Il tourne ensuite en tâche de fond : il écrit `Data.lua` quand le serveur change (puis `/reload` en jeu) et uploade les SavedVariables à chaque déconnexion//reload.

## État d'avancement

- [x] Étape 0 — dépôt, structure, plan fondateur
- [x] Étape 1 — addon, onglet Métiers sur données mock *(code livré — à valider en jeu sur le client Forever)*
- [x] Étape 2 — serveur (bot + API + SQLite) *(7 tests verts + smoke test curl — à connecter à un vrai bot Discord)*
- [x] Étape 3 — compagnon (down-sync + up-sync) *(boucle E2E validée en local : SavedVariables → upload → export → Data.lua)*
- [x] Étape 4 — commandes de craft *(E2E validé : commande en jeu → serveur → Data.lua ; toasts addon message à valider à deux comptes)*
- [ ] Étape 5 — onglet Membres / armurerie
