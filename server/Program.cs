using Discord.Interactions;
using Discord.WebSocket;
using GuildProfessions.Server.Api;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Discord;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
	options.UseSqlite(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddScoped<RosterService>();
builder.Services.AddScoped<ExportBuilder>();
builder.Services.AddScoped<CraftOrderService>();
builder.Services.AddSingleton<ICraftOrderNotifier, DiscordCraftOrderNotifier>();

builder.Services.AddSingleton(new DiscordSocketClient(new DiscordSocketConfig
{
	GatewayIntents = Discord.GatewayIntents.Guilds,
}));
builder.Services.AddSingleton(provider =>
	new InteractionService(provider.GetRequiredService<DiscordSocketClient>()));
builder.Services.AddHostedService<DiscordBotService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
	var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
	db.Database.Migrate();
}

app.MapApiEndpoints();

app.Run();

// Rend Program visible aux tests d'intégration (WebApplicationFactory).
public partial class Program;
