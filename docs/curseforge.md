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
- **Summary (une phrase, différente de la description) :** Guild management for WoW: Forever: events and raid sign-ups, crafter directory, craft orders and members, synced in game and on Discord.
- **Catégorie principale :** Guild — secondaires : Professions, Chat & Communication
- **Avatar :** `branding/guildkeep-icon-400.png` (400×400, PNG, pas de WebP)
- **Licence :** à choisir (le dépôt n'en a pas encore)
- **Canal de contact recommandé :** tickets GitHub (https://github.com/AntoineRenoux/GuildProfessions/issues)

## Description (Markdown)

```markdown
**Guildkeep** turns your guild window into a guild office: guild events with raid sign-ups and rosters, a searchable directory of who can craft what, craft orders between members, and a sheet for every member. Free, no account, no password.

## What it adds in game

**A Guildkeep tab in the guild window**, also opened with `/gkeep` (`/gkeep events`, `/gkeep presence` and `/gkeep quests` go straight there). Six tabs:

**Events**
- Plan raids, dungeons, PvP or any outing, in a list or a month calendar.
- Sign up in one click with your role (tank, healer, melee or ranged DPS), spec and a note: attending, tentative or not attending.
- Set a target composition; the event shows confirmed players and sign-ups per role against it.
- The organizer and officers build the roster: confirm, standby, bench. Invite every confirmed player online in one click (the raid is formed for you) or copy the roster for Discord.
- Soft reserves: one item per priority (high, normal, low) by shift-clicking it, with conflicts and a warning on gear your class cannot use. Officers enter reserves for players without the addon, hard-reserve items and lock reserves (only confirmed players then count), with an optional switch to master looter.
- Raid night: assign the raid lead, summoner, consumables and repair bot duties, check a pre-raid list (missing roles, unplaced or tentative players, reserve conflicts, empty duties) and copy the whole night for Discord.
- Changes reach guildmates who are online right away, and everyone else through the guild's sync.

**Attendance**
- Attendance is recorded in game while you raid: who from the guild is actually there and online, minute by minute. Signing up never counts as attending; a raid counts once 5 guild members are in it together.
- Epic and better loot received by guild members is recorded from the loot messages.
- Attendance rate over 30, 60 and 90 days for every raider, compared with the previous 30 days, plus each player's raids and loot.
- Officers correct attendance (present, absent, excused) and void loot (disenchanted, mistakes); corrections stay separate from what was recorded.

**Quests**
- Every elite, dungeon and raid quest in members' quest logs, grouped by zone or dungeon, with who has it, who is online and whose objectives are done.
- "Only my quests" filter; quests you have already turned in are hidden.
- Whisper or invite the others in one click, and share the quest with your group through the game's quest sharing.

**Professions**
- Every member's professions with their level and rank, coloured by progress.
- Search any recipe by name ("flask", "robe"): only the crafters who know it stay in the list.
- Filter by profession and by minimum skill level, sort by name, profession, level or number of recipes.
- "Voir (N)" (View) opens a crafter's full recipe list, searchable; shift-click links a recipe in chat.

**Orders**
- Ask every crafter of the guild at once: pick the recipe from the guild catalog, set a quantity and a note. Crafters who know the recipe are notified; open requests expire after five days.
- Crafters offer to make it (with a note: price, delay…); your request title turns green when offers arrive, and you pick one. You can still order directly from a given crafter.
- Order a recipe from a crafter in two clicks: the order window lists every reagent to send, how many you already carry, and which ones another member can craft for you (with the full reagent tree).
- Delivery tracking: the crafter marks the order ready to trade or mailed, you confirm receipt. Trading and mailing stay manual, in game.
- Filters: everything, to craft for me, my requests, requests I can make, my offers.
- The guild recipe catalog grows as members open their profession windows (every recipe shown, learned or not), Cooking included.

**Members**
- Every member's characters with class, level and who is online.
- A character sheet with the gear scanned at their last logout, item quality colours and a 3D preview.

**Your own data is read automatically**: professions and gear when you log in, recipes the first time you open each profession window (a chat message confirms how many were read). Characters outside your guild are never scanned.

## Sharing with your guild

An addon cannot reach the internet, so guild data travels in one of two ways:

- **Copy and paste, no extra software.** Export your data from the addon window and send it with the Guildkeep Discord bot (`/gkeep import`); get the guild's data back with `/gkeep export` and paste it into the addon (Import button).
- **Automatic sync.** Your guild admin sets Guildkeep up on Discord with `/gkeep-setup` and shares the guild's install guide. A small companion app then syncs everything in the background while you play, the way well-known companion apps for other addons do. It never launches or changes the game, and only reads and writes the Guildkeep addon's own files.

On Discord, every command lives under `/gkeep` (in English or French, following each user's Discord language): events (list, details and roster, sign up, and for organizers and officers create, edit, cancel and place players), loot reserves, support duties and the pre-raid checklist, attendance rates and loot history, shared quests, who can craft a recipe, a character's recipes, the whole directory, the recipe catalog, craft orders and open requests with offers and delivery steps, with direct-message notifications.

## Good to know

- Built for **WoW: Forever** (interface 16001).
- **The in-game interface is in French for now**; an English translation is planned. The Discord bot already speaks English and French.
- Each guild's data is isolated from other guilds. Guildkeep never asks for a password, an email or a Battle.net account.
- Coming next: announcements, guild handbook and goals.
- Bug reports and suggestions: GitHub issues, link below.

---

**Français.** Guildkeep ajoute un onglet à la fenêtre de guilde : les événements et inscriptions aux raids (rôles, composition, roster), l'annuaire des artisans et de leurs recettes, les commandes de craft entre membres (avec les composants à envoyer) et la fiche de chaque membre. Partage avec la guilde par le bot Discord, en copier-coller ou automatiquement avec l'app compagnon. Gratuit, sans compte.

---

Website and setup guide: https://guildkeep.warpvault.com · Source code and issues: https://github.com/AntoineRenoux/GuildProfessions · Privacy: https://guildkeep.warpvault.com/confidentialite.html

*World of Warcraft is a trademark of Blizzard Entertainment. Guildkeep is not affiliated with or endorsed by Blizzard Entertainment.*
```
