using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;

namespace GuildProfessions.Server.Api;

public static class Endpoints
{
	// Envoyer des messages, intégrer des liens, joindre des fichiers, gérer les pseudos.
	private const long InvitePermissions = 134268928;

	private static IResult Manifest(IWebHostEnvironment env, string file)
	{
		var path = Path.Combine(env.WebRootPath ?? "wwwroot", "downloads", file);
		return File.Exists(path) ? Results.File(path, "application/json") : Results.NotFound();
	}

	private static IResult JoinPage(IWebHostEnvironment env) =>
		Results.File(Path.Combine(env.WebRootPath ?? "wwwroot", "rejoindre.html"), "text/html; charset=utf-8");

	public static void MapApiEndpoints(this WebApplication app)
	{
		app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

		// Manifestes de version (écrits à côté des zips par scripts/package-companion.sh) :
		// publics, ils ne contiennent que versions, URLs et empreintes. Une seule lignée :
		// - guildkeep/manifest : lu par les compagnons Guildkeep ;
		// - companion/manifest : le pont pour les installations d'avant le renommage
		//   (exécutable sous l'ancien nom, sans entrée addon), qui passent à Guildkeep.
		app.MapGet("/api/v1/companion/manifest", (IWebHostEnvironment env) => Manifest(env, "companion-manifest.json"));
		app.MapGet("/api/v1/guildkeep/manifest", (IWebHostEnvironment env) => Manifest(env, "guildkeep-manifest.json"));

		// Infos publiques du site : lien d'invitation du bot (identifiant d'application
		// en config, sinon celui du bot connecté).
		app.MapGet("/api/v1/site", (IConfiguration config, global::Discord.WebSocket.DiscordSocketClient client) =>
		{
			var applicationId = config["Discord:ApplicationId"] is { Length: > 0 } id ? id : client.CurrentUser?.Id.ToString();
			return Results.Ok(new
			{
				inviteUrl = applicationId is null ? null :
					$"https://discord.com/oauth2/authorize?client_id={applicationId}&scope=bot+applications.commands&permissions={InvitePermissions}",
			});
		});

		// Page d'installation d'une guilde : une seule page statique, qui lit le code dans l'adresse.
		app.MapGet("/rejoindre/{code}", (IWebHostEnvironment env) => JoinPage(env));
		app.MapGet("/join/{code}", (IWebHostEnvironment env) => JoinPage(env));

		// Lien d'installation d'une guilde (/rejoindre/{code}) : de quoi afficher la page.
		app.MapGet("/api/v1/join/{code}", async (string code, GuildDirectory guilds, GuildPackBuilder packs, CancellationToken ct) =>
		{
			var guild = await guilds.FindByInviteCodeAsync(code, ct);
			if (guild is null)
			{
				return Results.NotFound();
			}
			var platforms = GuildPackBuilder.Platforms
				.Where(p => packs.TemplatePath(p.Key) is not null)
				.Select(p => new { id = p.Key, label = p.Value, url = $"/api/v1/pack/{code}/{p.Key}" });
			return Results.Ok(new { name = guild.Name, locale = guild.Locale, platforms });
		});

		// Pack tout-en-un de la guilde, son config.json réglé à la volée (token compris).
		app.MapGet("/api/v1/pack/{code}/{platform}", async (string code, string platform, GuildDirectory guilds,
			GuildPackBuilder packs, CancellationToken ct) =>
		{
			var guild = await guilds.FindByInviteCodeAsync(code, ct);
			var template = packs.TemplatePath(platform);
			if (guild is null || template is null)
			{
				return Results.NotFound();
			}
			return Results.File(packs.Build(guild, template), "application/zip", GuildPackBuilder.FileName(guild, platform));
		});

		// Le token de guilde désigne la guilde : l'export ne contient que ses données.
		app.MapGet("/api/v1/export", async (HttpRequest request, GuildDirectory guilds, GuildContext guildContext,
			ExportBuilder exportBuilder, CancellationToken ct) =>
		{
			var guild = await guilds.FindByApiTokenAsync(request.Headers["X-Guild-Token"].ToString(), ct);
			if (guild is null)
			{
				return Results.Unauthorized();
			}
			guildContext.Use(guild);
			return Results.Ok(await exportBuilder.BuildAsync(ct));
		});

		app.MapPost("/api/v1/upload", async (HttpRequest request, UploadPayload payload, GuildDirectory guilds,
			GuildContext guildContext, BotLocalizer localizer, UploadIngestion ingestion, CancellationToken ct) =>
		{
			// Deux modes : token personnel (un membre, donc sa guilde) ou token
			// de guilde partagé (compagnon zéro-config, member = null).
			Member? member = null;
			Guild? guild;
			var uploadToken = request.Headers["X-Upload-Token"].ToString();
			if (!string.IsNullOrEmpty(uploadToken))
			{
				var personal = await guilds.FindByUploadTokenAsync(uploadToken, ct);
				if (personal is null)
				{
					return Results.Unauthorized();
				}
				(guild, member) = personal.Value;
			}
			else
			{
				guild = await guilds.FindByApiTokenAsync(request.Headers["X-Guild-Token"].ToString(), ct);
				if (guild is null)
				{
					return Results.Unauthorized();
				}
			}
			guildContext.Use(guild);
			localizer.Language = BotLocalizer.FromLocale(guild.Locale);
			return Results.Ok(await ingestion.IngestAsync(member, payload, ct));
		});
	}
}
