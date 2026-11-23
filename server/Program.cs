using Discord.Interactions;
using Discord.WebSocket;
using GuildProfessions.Server;
using GuildProfessions.Server.Api;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Discord;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Config locale gitignorée (token du bot de test, etc.) : dernière ajoutée,
// elle gagne sur tout en dev ; absente sur le VPS où l'environnement règne.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

builder.Services.AddDbContext<AppDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

// Noyau partagé, guildes, orchestrateurs et un enregistrement par module (Modules/{Module}/{Module}Module.cs).
builder.Services.AddGuildkeep();
builder.Services.AddSingleton<NicknameSync>();

builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
	GatewayIntents = Discord.GatewayIntents.Guilds,
	LogLevel = Discord.LogSeverity.Debug,
}));
builder.Services.AddSingleton(provider =>
	new InteractionService(provider.GetRequiredService<DiscordSocketClient>(), BotInteractionConfig.Create()));
builder.Services.AddHostedService<DiscordBotService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	try
	{
		var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;
		database.Migrate();
		// Journal WAL (réglage persistant du fichier) : les lectures ne bloquent plus
		// les écritures, et deux traitements simultanés (envoi, commande Discord)
		// ne finissent plus en « base verrouillée » au bout du délai d'attente.
		database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
	}
	catch (Exception exception)
	{
		// Une seule base pour tous les modules : sans son schéma à jour, rien ne peut tourner.
		// On le dit clairement plutôt que de servir des données incohérentes.
		app.Logger.LogCritical(exception, "Migration de la base impossible : le serveur ne démarre pas. Restaurer la sauvegarde ou corriger la migration en cause.");
		throw;
	}
}

// Landing page (wwwroot) + téléchargements (volume /app/wwwroot/downloads sur le VPS).
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapApiEndpoints();

app.Run();

// Rend Program visible aux tests d'intégration (WebApplicationFactory).
public partial class Program;
