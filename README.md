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
- **Serveur** : `docker compose up -d` sur le VPS (token bot Discord en variable d'environnement).

## État d'avancement

- [x] Étape 0 — dépôt, structure, plan fondateur
- [x] Étape 1 — addon, onglet Métiers sur données mock *(code livré — à valider en jeu sur le client Forever)*
- [ ] Étape 2 — serveur (bot + API + SQLite)
- [ ] Étape 3 — compagnon (down-sync puis up-sync)
- [ ] Étape 4 — commandes de craft
- [ ] Étape 5 — onglet Membres / armurerie
