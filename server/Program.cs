using Discord.Interactions;
using Discord.WebSocket;
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

builder.Services.AddScoped<RosterService>();
builder.Services.AddScoped<ExportBuilder>();
builder.Services.AddScoped<CraftOrderService>();
builder.Services.AddSingleton<ICraftOrderNotifier, DiscordCraftOrderNotifier>();

builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
	GatewayIntents = Discord.GatewayIntents.Guilds,
	LogLevel = Discord.LogSeverity.Debug,
}));
builder.Services.AddSingleton(provider =>
	new InteractionService(provider.GetRequiredService<DiscordSocketClient>(), new InteractionServiceConfig
	{
		LogLevel = Discord.LogSeverity.Debug,
		// Exécution inline : les exceptions remontent dans notre await au
		// lieu d'être avalées par le fire-and-forget interne de RunMode.Async.
		DefaultRunMode = RunMode.Sync,
		ThrowOnError = true,
	}));
builder.Services.AddHostedService<DiscordBotService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	db.Database.Migrate();
}

// Landing page (wwwroot) + téléchargements (volume /app/wwwroot/downloads sur le VPS).
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapApiEndpoints();

app.Run();

// Rend Program visible aux tests d'intégration (WebApplicationFactory).
public partial class Program;
