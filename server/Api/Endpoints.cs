using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Api;

public static class Endpoints
{
	public static void MapApiEndpoints(this WebApplication app)
	{
		app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

		app.MapGet("/api/v1/export", async (HttpRequest request, ExportBuilder exportBuilder, IConfiguration config, CancellationToken ct) =>
		{
			var expected = config["Api:GuildToken"];
			if (string.IsNullOrEmpty(expected) ||
				request.Headers["X-Guild-Token"].ToString() != expected)
			{
				return Results.Unauthorized();
			}
			return Results.Ok(await exportBuilder.BuildAsync(ct));
		});

		app.MapPost("/api/v1/upload", async (HttpRequest request, UploadPayload payload, AppDbContext db, RosterService roster, CraftOrderService craftOrders, CancellationToken ct) =>
		{
			var token = request.Headers["X-Upload-Token"].ToString();
			if (string.IsNullOrEmpty(token))
			{
				return Results.Unauthorized();
			}
			var member = await db.Members.SingleOrDefaultAsync(m => m.UploadToken == token, ct);
			if (member is null)
			{
				return Results.Unauthorized();
			}
			var result = await roster.ApplyUploadAsync(member, payload, ct);
			var orderWarnings = await craftOrders.ApplyUploadAsync(member, payload, ct);
			return Results.Ok(result with { Warnings = [.. result.Warnings, .. orderWarnings] });
		});
	}
}
