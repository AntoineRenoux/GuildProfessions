using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;

namespace GuildProfessions.Server.Api;

public static class Endpoints
{
	public static void MapApiEndpoints(this WebApplication app)
	{
		app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

		// Manifeste de version du compagnon (écrit par scripts/package-companion.sh
		// à côté des zips) : public, il ne contient que versions, URLs et empreintes.
		app.MapGet("/api/v1/companion/manifest", (IWebHostEnvironment env) =>
		{
			var path = Path.Combine(env.WebRootPath ?? "wwwroot", "downloads", "companion-manifest.json");
			return File.Exists(path)
				? Results.File(path, "application/json")
				: Results.NotFound();
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
