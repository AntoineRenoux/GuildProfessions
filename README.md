# Guildkeep

L'annuaire des artisans et les commandes de craft d'une guilde **WoW: Forever**, en jeu et sur Discord.
Ouvert à toutes les guildes : un administrateur de serveur Discord crée la sienne en deux minutes.

| Composant | Rôle |
|---|---|
| [`addon/Guildkeep/`](addon/Guildkeep/) | Addon Lua (TOC 16001) : fenêtre native à trois onglets (Métiers, Membres, Commandes), ouverte par `/gkeep` ou par l'onglet ajouté à la fenêtre de guilde. Choisit les données de la guilde du personnage connecté. |
| [`server/`](server/) | Bot Discord (Discord.Net) + API HTTP (ASP.NET minimal) + SQLite, partagés par toutes les guildes, déployés sur VPS |
| [`companion/`](companion/) | Compagnon .NET sur le PC du joueur : écrit `Data.lua` (serveur → jeu), envoie les `SavedVariables` (jeu → serveur), installe et met à jour l'addon |

Un addon WoW n'a accès ni au réseau ni au disque : le compagnon fait le pont, sur le modèle de GatherMate2 et du WeakAuras Companion.

## Parcours d'une guilde

1. Un administrateur invite le bot sur son serveur Discord et lance **`/gkeep-setup`** (réservé au droit « Gérer le serveur ») : nom exact de la guilde en jeu, deux options (renommage des pseudos, salon des annonces), récapitulatif, création.
2. Il partage le **lien d'installation** donné par le bot : chaque joueur y télécharge le pack de la guilde, déjà configuré (`/api/v1/pack/{code}/{système}`).
3. Le joueur lance `Guildkeep` : addon installé, synchronisation automatique. Personnages dans plusieurs guildes : il lance le pack de chacune, un seul compagnon les synchronise toutes.
4. Sur Discord, tout est sous **`/gkeep`** (`/gkeep help`), en français ou en anglais selon la langue de chacun.

## Isolation des guildes

Une seule base : chaque donnée porte l'identifiant de sa guilde, et `AppDbContext` filtre toutes les lectures sur la guilde courante (fermé par défaut) et estampille toutes les écritures. La guilde courante vient du token pour l'API, du serveur Discord pour le bot. Tests dédiés : `server.Tests/GuildIsolationTests.cs`.

## Dev local

```bash
# SDK .NET 10 requis
dotnet test GuildProfessions.slnx
dotnet run --project server                # API sur http://localhost:5000, bot off sans Discord:Token
dotnet dotnet-ef migrations add <Nom> --project server/GuildProfessions.Server.csproj
```

Pour essayer le bot : un bot de test dans `server/appsettings.Local.json` (gitignoré, modèle : `appsettings.Local.json.example`), avec `Discord:GuildIds` = l'ID du serveur de test (commandes enregistrées immédiatement).

## Production (VPS)

`cp .env.example .env`, remplir, puis `docker compose up -d --build`. En production `DISCORD_GUILD_IDS` reste vide : commandes globales, disponibles sur tout serveur qui invite le bot.

Création du bot (une fois) : [discord.com/developers/applications](https://discord.com/developers/applications) → New Application → onglet **Bot** → Reset Token (= `DISCORD_TOKEN`), et cocher **Public Bot**. Lien d'invitation :
`https://discord.com/oauth2/authorize?client_id=<APPLICATION_ID>&scope=bot+applications.commands&permissions=134268928`
(envoyer des messages, intégrer des liens, joindre des fichiers, gérer les pseudos). Au-delà de 100 serveurs, Discord exige la vérification du bot.

## Publier une version

```bash
scripts/package-companion.sh --deploy
```

Commiter, puis lancer cette commande. La version vaut `1.0.<nombre de commits>`. Le script publie l'addon, l'exécutable du compagnon (mise à jour automatique) et les packs modèles, que le serveur personnalise pour chaque guilde. Les compagnons installés se mettent à jour au démarrage ou sous 6 h, addon compris.

Mac : le compagnon n'est pas notarisé, macOS demande une autorisation au premier lancement (expliquée dans le pack et sur le site).
