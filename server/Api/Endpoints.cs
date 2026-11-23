using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Services;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

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

		// Manifeste de version du compagnon (écrit à côté des zips par
		// scripts/package-companion.sh) : public, il ne contient que versions, URLs et empreintes.
		app.MapGet("/api/v1/companion/manifest", (IWebHostEnvironment env) => Manifest(env, "companion-manifest.json"));

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
		// Un module en échec n'empêche pas les autres d'arriver en jeu : 200 avec ses
		// sections à null, et l'en-tête X-Guildkeep-Failed-Modules pour le signaler.
		app.MapGet("/api/v1/export", async (HttpRequest request, GuildDirectory guilds, GuildContext guildContext,
			ExportBuilder exportBuilder, CancellationToken ct) =>
		{
			var guild = await guilds.FindByApiTokenAsync(request.Headers["X-Guild-Token"].ToString(), ct);
			if (guild is null)
			{
				return Results.Unauthorized();
			}
			guildContext.Use(guild);
			return ExportResult(await exportBuilder.BuildAsync(ct), request.HttpContext.Response);
		});

		app.MapPost("/api/v1/upload", async (HttpRequest request, UploadPayload payload, GuildDirectory guilds,
			GuildContext guildContext, BotLocalizer localizer, IServiceProvider services, UploadIngestion ingestion, CancellationToken ct) =>
		{
			// Deux modes : token personnel (un membre, donc sa guilde) ou token
			// de guilde partagé (compagnon zéro-config, pas de membre).
			int? memberId = null;
			Guild? guild;
			var uploadToken = request.Headers["X-Upload-Token"].ToString();
			if (!string.IsNullOrEmpty(uploadToken))
			{
				// Le token personnel est tenu par l'annuaire (socle d'identité), lu par son contrat.
				var owner = await services.GetRequiredService<IAnnuaire>().UploadTokenOwnerAsync(uploadToken, ct);
				guild = owner is null ? null : await guilds.FindByIdAsync(owner.GuildId, ct);
				if (owner is null || guild is null)
				{
					return Results.Unauthorized();
				}
				memberId = owner.MemberId;
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
			var source = memberId is null ? UploadSource.GuildToken : UploadSource.PersonalToken;
			var outcome = await ingestion.IngestAsync(memberId, payload, source, ct);
			// Un module en échec : 503 avec le même corps, pour que le compagnon ne
			// mémorise pas l'empreinte de l'envoi et le renvoie au prochain cycle.
			return outcome.Complete
				? Results.Ok(outcome.Result)
				: Results.Json(outcome.Result, statusCode: StatusCodes.Status503ServiceUnavailable);
		});
	}

	/// <summary>En-tête qui liste les modules absents d'un export partiel (noms encodés en URL, séparés par des virgules).</summary>
	public const string FailedModulesHeader = "X-Guildkeep-Failed-Modules";

	/// <summary>
	/// Réponse de GET /api/v1/export : toujours 200 avec la même forme JSON. Un
	/// export partiel (sections des modules en échec à null) est signalé par
	/// l'en-tête.
	/// </summary>
	public static IResult ExportResult(ExportBuild build, HttpResponse response)
	{
		if (!build.Complete)
		{
			// Encodé : les noms de modules sont accentués, une valeur d'en-tête doit rester ASCII.
			response.Headers[FailedModulesHeader] = string.Join(",", build.FailedModules.Select(Uri.EscapeDataString));
		}
		return Results.Ok(build.Export);
	}
}
