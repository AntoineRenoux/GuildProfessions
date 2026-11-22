# Fiche CurseForge de Guildkeep

Textes à coller dans la console auteur CurseForge (https://authors.curseforge.com),
projet WoW, saveur **WoW: Forever**. Les publications suivantes se font avec
`scripts/package-companion.sh --deploy --curseforge` (le journal des commits de
l'addon sert de changelog, exigé par CurseForge pour chaque fichier).

## Champs du projet

Règles suivies : [moderation policies CurseForge](https://support.curseforge.com/support/solutions/articles/9000197279-moderation-policies)
— anglais d'abord, description fonctionnelle, aucun lien de téléchargement externe,
site personnel en bas de page, avatar 400×400, journal de modifications à chaque fichier.

- **Name :** Guildkeep (en anglais, sans version ni nom de jeu)
- **Summary (une phrase, différente de la description) :** Guild management for WoW: Forever: crafter directory, craft orders and members, synced in game and on Discord.
- **Catégorie principale :** Guild — secondaires : Professions, Chat & Communication
- **Avatar :** `branding/guildkeep-icon-400.png` (400×400, PNG, pas de WebP)
- **Licence :** à choisir (le dépôt n'en a pas encore)
- **Canal de contact recommandé :** tickets GitHub (https://github.com/AntoineRenoux/GuildProfessions/issues)

## Description (Markdown)

```markdown
**Guildkeep** turns your guild window into a guild office: a searchable directory of who can craft what, craft orders between members, and a sheet for every member. Free, no account, no password.

## What it adds in game

**A Guildkeep tab in the guild window**, also opened with `/gkeep`. Three tabs:

**Professions**
- Every member's professions with their level and rank, coloured by progress.
- Search any recipe by name ("flask", "robe"): only the crafters who know it stay in the list.
- Filter by profession and by minimum skill level, sort by name, profession, level or number of recipes.
- "Voir (N)" (View) opens a crafter's full recipe list, searchable; shift-click links a recipe in chat.

**Orders**
- Order a recipe from a crafter in two clicks, with a quantity.
- The order window lists every reagent to send, how many you already carry, and which ones another member can craft for you (with the full reagent tree).
- Track orders you placed and orders you must craft: accept, mark done, cancel.

**Members**
- Every member's characters with class, level and who is online.
- A character sheet with the gear scanned at their last logout, item quality colours and a 3D preview.

**Your own data is read automatically**: professions and gear when you log in, recipes the first time you open each profession window (a chat message confirms how many were read). Characters outside your guild are never scanned.

## Sharing with your guild

An addon cannot reach the internet, so guild data travels in one of two ways:

- **Copy and paste, no extra software.** Export your data from the addon window and send it with the Guildkeep Discord bot (`/gkeep import`); get the guild's data back with `/gkeep export` and paste it into the addon (Import button).
- **Automatic sync.** Your guild admin sets Guildkeep up on Discord with `/gkeep-setup` and shares the guild's install guide. A small companion app then syncs everything in the background while you play, the way well-known companion apps for other addons do. It never launches or changes the game, and only reads and writes the Guildkeep addon's own files.

On Discord, every command lives under `/gkeep` (in English or French, following each user's Discord language): who can craft a recipe, a character's recipes, the whole directory, craft orders with direct-message notifications.

## Good to know

- Built for **WoW: Forever** (interface 16001).
- **The in-game interface is in French for now**; an English translation is planned. The Discord bot already speaks English and French.
- Each guild's data is isolated from other guilds. Guildkeep never asks for a password, an email or a Battle.net account.
- Coming next: raid organisation (sign-ups, roster by role, attendance).
- Bug reports and suggestions: GitHub issues, link below.

---

**Français.** Guildkeep ajoute un onglet à la fenêtre de guilde : l'annuaire des artisans et de leurs recettes, les commandes de craft entre membres (avec les composants à envoyer) et la fiche de chaque membre. Partage avec la guilde par le bot Discord, en copier-coller ou automatiquement avec l'app compagnon. Gratuit, sans compte.

---

Website and setup guide: https://guildkeep.warpvault.com · Source code and issues: https://github.com/AntoineRenoux/GuildProfessions · Privacy: https://guildkeep.warpvault.com/confidentialite.html

*World of Warcraft is a trademark of Blizzard Entertainment. Guildkeep is not affiliated with or endorsed by Blizzard Entertainment.*
```
