# GuildProfessions — Addon WoW: Forever + Bot Discord + API VPS

> Document fondateur du projet. Rédigé le 2026-10-07.

## Contexte

Objectif : une fenêtre « Guilde+ » en jeu (WoW: Forever) à **deux onglets** :
1. **Membres** — liste du roster ; au clic sur un membre, rendu 3D de son personnage avec son équipement (esprit « armurerie en ligne »).
2. **Métiers** *(le cœur du projet, priorité absolue)* — liste des métiers de tous les membres, filtrable par métier, **par niveau** et **par patron/recette connue**, avec la possibilité de **passer une commande de craft** à un joueur.

Les données transitent par un bot Discord + base sur le VPS, synchronisées **dans les deux sens** avec l'addon.

Contrainte fondatrice : un addon WoW ne peut ni faire de requêtes réseau ni lire/écrire des fichiers arbitraires. Les ponts retenus (modèle GatherMate2 / WeakAuras Companion) :
- **Entrée (serveur → jeu)** : l'app compagnon écrit `Data.lua` dans le dossier de l'addon ; chargé au login / `/reload`.
- **Sortie (jeu → serveur)** : l'addon écrit ses `SavedVariables` (flushées au logout / `/reload`) ; le compagnon les surveille et uploade.
- **Temps réel entre joueurs connectés** : addon messages (`C_ChatInfo.SendAddonMessage`, canal `GUILD`) — notification instantanée d'une commande de craft aux artisans en ligne.

Choix actés : app compagnon, stack .NET (Discord.Net + ASP.NET), sync bidirectionnelle.

**Cible client** : WoW: Forever = branche officielle Blizzard (4 nov. 2026), **API moderne** (UI Mainline ~12.1.5), TOC `16001` (jeu 1.60.1). Les « secret values » (addon disarmament) touchent le combat, pas les métiers/équipement/modèles.

**Battle.net API** : pas encore d'API Profile pour Forever. Sur retail, `GET /profile/wow/character/{realm}/{name}/professions` (token client credentials) renvoie métiers + niveaux + `known_recipes` — si un namespace `profile-forever-*` apparaît un jour, il remplacera l'upload compagnon côté montant. D'où l'abstraction `IProfessionSource` côté serveur (voir plus bas). OAuth authorization code + scope `wow.profile` (`oauth.battle.net/authorize|token`, `GET /profile/user/wow`) reste l'option propre pour un `/link` vérifié, le jour où Forever y figure.

## Structure du dépôt

```
GuildProfessions/
├── addon/GuildProfessions/        # l'addon Lua (copiable tel quel dans Interface/AddOns/)
├── companion/                     # app compagnon .NET (Windows, console/worker)
├── server/                        # bot Discord + API HTTP (.NET, déployé sur le VPS)
├── docker-compose.yml             # déploiement VPS (server + volume SQLite)
└── README.md
```

## Architecture

```
Discord ⇄ Bot (Discord.Net) ─┐
                             ├─ server/.NET + SQLite (VPS)
Addon ⇄ SavedVariables ⇄ Companion ⇄ API HTTP ─┘
Addon ⇐ Data.lua ⇐ Companion (down-sync)
Addon ⇄ Addon : addon messages GUILD (notifications temps réel, pas de persistance)
```

Flux down : bot/API → compagnon poll `GET /export` → écrit `Data.lua` → visible au prochain login / `/reload`.
Flux up : scan en jeu (métiers, recettes, équipement, commandes passées) → SavedVariables au logout / `/reload` → compagnon (FileSystemWatcher) → `POST /upload` → base → Discord.

## Composant 1 — `server/` : bot Discord + API (VPS)

Un seul processus .NET 10 (worker ASP.NET : bot + API HTTP), SQLite via EF Core.

**Modèle de données** :
- `members` : `id`, `discord_id` (unique), `discord_name`, `upload_token`
- `characters` : `id`, `member_id` (nullable), `name`, `class`, `race`, `gender`, `level`, `last_seen_utc`, `source`
- `professions` : `character_id`, `profession`, `skill_level`, `max_skill`, `note`, `updated_utc`, `source` (`addon` | `discord`, addon prioritaire car scanné)
- `recipes` : `profession_id`, `recipe_spell_id`, `recipe_name` (nom résolu côté serveur pour la recherche Discord ; l'addon, lui, résout en jeu)
- `equipment` : `character_id`, `slot` (1–19), `item_link` (le lien complet encode objet/enchant/gemmes), `updated_utc`
- `craft_orders` : `id`, `requester_character`, `crafter_character`, `recipe_spell_id`, `item_name`, `quantity`, `note`, `status` (`open` → `accepted` → `done` / `cancelled`), `created_utc`, `source` (`addon` | `discord`)

**Abstraction d'ingestion** : `IProfessionSource` (implémentations : `DiscordDeclarationSource`, `CompanionUploadSource` ; plus tard `BattleNetProfileSource`). La fusion par priorité de source vit dans un service unique, prêt à insérer `blizzard` entre `addon` et `discord`.

**Commandes slash du bot** :
- `/metier set|remove|list|who` — déclaration et consultation (who : `[métier] [niveau-min] [recette]`)
- `/craft order <artisan> <objet> [quantité] [note]` — passe une commande ; le bot DM/ping l'artisan
- `/craft list [mine|pour-moi]`, `/craft done <id>`, `/craft cancel <id>`
- `/link <personnage>`, `/token`

**API HTTP** (minimal API, JSON) :
- `GET /api/v1/export` — dataset complet versionné `{ version, generatedUtc, characters, professions+recipes, equipment, orders }` ; auth token guilde. Le compagnon ne réécrit `Data.lua` que si `version` change.
- `POST /api/v1/upload` — payload scanné (métiers, recettes, équipement, commandes créées/mises à jour en jeu) ; auth `upload_token` personnel, écriture limitée aux personnages du membre.

Déploiement : Dockerfile + compose, secrets par variables d'environnement.

## Composant 2 — `companion/` : app compagnon (.NET, PC du joueur)

Worker console .NET 10, publié single-file win-x64. `config.json` : `apiBaseUrl`, `guildToken`, `uploadToken`, `wowPath` (auto-détection).

- **Down-sync** : poll `GET /export` (démarrage + toutes les N min) → génère `Interface/AddOns/GuildProfessions/Data.lua` (`GuildProfessions_ServerData = { version, generatedUtc, characters = {...} }`). Recettes stockées en tableaux d'IDs numériques pour contenir la taille du fichier.
- **Up-sync** : `FileSystemWatcher` sur `WTF/Account/*/SavedVariables/GuildProfessions.lua` → parseur Lua minimal maison (pas de NLua, on n'exécute pas de code) → `POST /upload`.
- Idempotent, log fichier, retry si fichier verrouillé.

## Composant 3 — `addon/GuildProfessions/` : l'addon Lua

**TOC** : `## Interface: 16001`, `## SavedVariables: GuildProfessionsDB`, charge `Data.lua` (table vide par défaut — l'addon fonctionne sans compagnon).

**Scanners** (`Scanner.lua`) — tout écrit dans `GuildProfessionsDB.characters[nomPerso]` :
- *Identité* : `UnitRace`/`UnitSex`/`UnitClassBase`/`UnitLevel` au `PLAYER_LOGIN` (nécessaire au rendu armurerie des autres).
- *Métiers* : `GetProfessions()` + `GetProfessionInfo(i)` au login et sur `SKILL_LINES_CHANGED`.
- *Recettes* **(v1, core)** : sur `TRADE_SKILL_LIST_UPDATE` (fenêtre de métier ouverte), `C_TradeSkillUI.GetAllRecipeIDs()` + `C_TradeSkillUI.GetRecipeInfo(id).learned` → liste d'IDs de recettes connues. Limite assumée : la liste ne se rafraîchit que quand le joueur ouvre sa fenêtre de métier ; un libellé « recettes relevées le … » l'affiche.
- *Équipement* : `GetInventoryItemLink("player", slot)` pour les slots 1–19, au login et sur `PLAYER_EQUIPMENT_CHANGED`.

**Fenêtre principale** (`/gp`, frame déplaçable + `PanelTabButtonTemplate`) — données = fusion `GuildProfessions_ServerData` (roster complet) + `GuildProfessionsDB` (plus frais pour ses persos) :

- **Onglet 1 · Membres** : liste scrollable (nom, classe colorée, niveau, métiers en icônes, en-ligne/hors-ligne via roster guilde). Au clic, panneau de droite :
  - Rendu 3D : widget **`DressUpModel`** — `SetCustomRace(raceID, genre)` puis `TryOn(lien)` pour chaque pièce scannée ; `Undress()` d'abord. ⚠ Limite d'API assumée : les personnalisations (visage, coiffure, teint) d'un joueur **hors ligne** ne sont pas exposées à un addon — on affiche donc le modèle standard de sa race/genre portant son équipement exact, pas un clone parfait de l'armurerie.
  - Si le membre est **en ligne et à portée d'inspection**, bascule sur du vrai temps réel : `NotifyInspect(unit)` + `INSPECT_READY` + `SetUnit(unit)` → rendu fidèle, personnalisations comprises.
  - Colonne équipement textuelle à côté du modèle : liens d'objets cliquables (tooltip natif), ilvl moyen.
- **Onglet 2 · Métiers (prioritaire)** : colonne de filtres (métier, **niveau minimal** via slider/champ, **recherche de patron** — champ texte filtrant sur les noms de recettes, résolus en jeu par `C_TradeSkillUI.GetRecipeInfo` / cache `GetSpellInfo`), liste résultat (personnage, métier, barre de niveau, fraîcheur, note). Sur une ligne : bouton **« Commander »** → petite boîte (objet/recette, quantité, note) qui :
  1. enregistre la commande dans `GuildProfessionsDB.orders` (remontera au serveur au prochain logout / `/reload`),
  2. envoie un addon message `GUILD` (préfixe `GPROF`, `C_ChatInfo.RegisterAddonMessagePrefix`) — l'artisan en ligne équipé de l'addon reçoit un toast immédiat.

  Un panneau « Mes commandes » (reçues/passées) liste les ordres venus de `Data.lua` + les ordres locaux, avec boutons Accepter/Terminé (mêmes deux canaux).
- Bandeau discret commun : « données du JJ/MM HH:MM — lancez le compagnon pour mettre à jour » (`generatedUtc`).

⚠ Vigilances : Forever vient de sortir — numéro de TOC à suivre à chaque patch ; vérifier en jeu tôt le comportement réel de `DressUpModel:SetCustomRace` et des APIs `C_TradeSkillUI` sur ce client (l'API est « ~12.1.5 » mais le contenu est vanilla). Taille de `Data.lua` à surveiller (roster × recettes × équipement) — IDs numériques et pas de doublons de libellés.

## Ordre de réalisation (métiers d'abord)

0. **Initialisation** : dépôt `~/RiderProjects/GuildProfessions/` (`git init`, structure de dossiers ci-dessus), ce document en `PLAN.md` à la racine. ✔
1. **Addon, onglet Métiers, données mock** : TOC + `Data.lua` écrit à la main + scanners métiers/recettes + UI filtres niveau/patron → validable en jeu sans serveur. C'est le cœur et la zone d'inconnu API : on dérisque en premier.
2. **Serveur** : modèle EF + bot (`/metier`, `/link`, `/token`) + `GET /export` / `POST /upload` + Dockerfile.
3. **Compagnon** : down-sync puis up-sync. → Première boucle complète **métiers** de bout en bout.
4. **Commandes de craft** : table + commandes Discord `/craft`, ordres dans l'addon, addon messages temps réel.
5. **Onglet Membres / armurerie** : scan équipement + identité, rendu `DressUpModel`, inspection live en bonus.

## Vérification

- **Addon** : copie dans `Interface/AddOns/` du client Forever ; `/gp` → filtres niveau/patron corrects sur données mock ; après ouverture de la fenêtre de métier + `/reload`, `GuildProfessionsDB` contient bien métiers + IDs de recettes + équipement ; rendu `DressUpModel` vérifié sur 2–3 races/genres ; toast reçu par un second compte en ligne lors d'une commande.
- **Serveur** : tests xUnit sur la fusion par source et le cycle de vie des commandes (`open→accepted→done`) ; commandes testées sur un serveur Discord privé ; `curl` sur `/export` / `/upload`.
- **Compagnon** : `Data.lua` généré conforme (golden file) ; modification d'un faux `GuildProfessions.lua` → POST observé.
- **E2E** : `/metier set` Discord → visible dans `/gp` après `/reload` ; recette scannée en jeu → trouvable via `/metier who <recette>` sur Discord ; `/craft order` Discord → toast + liste en jeu ; « Terminé » en jeu → statut à jour sur Discord après logout.

## Références

- [Using OAuth — Battle.net Developer Portal](https://community.developer.battle.net/documentation/guides/using-oauth) — flows, `oauth.battle.net/authorize|token`, scopes (`wow.profile`, `openid`), tokens 24 h.
- [Profile APIs — World of Warcraft](https://community.developer.battle.net/documentation/world-of-warcraft/profile-apis) — Character Professions Summary (`known_recipes`), données rafraîchies à la déconnexion.
- [Warcraft Wiki — World of Warcraft API](https://warcraft.wiki.gg/wiki/World_of_Warcraft_API) — référence de l'API addon moderne (`C_TradeSkillUI`, `DressUpModel`, `C_ChatInfo`).
