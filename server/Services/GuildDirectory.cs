using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GuildProfessions.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Opérations sur les guildes elles-mêmes, avant qu'une guilde courante soit
/// connue : retrouver la guilde d'un token ou d'un serveur Discord, en créer
/// une, régénérer ses secrets. La table Guilds n'est pas filtrée.
/// </summary>
public sealed class GuildDirectory(AppDbContext db)
{
	public async Task<Guild?> FindByApiTokenAsync(string? token, CancellationToken ct = default) =>
		string.IsNullOrWhiteSpace(token) ? null : await db.Guilds.SingleOrDefaultAsync(g => g.ApiToken == token, ct);

	/// <summary>Guilde d'un token personnel (/token sur Discord) : celle du membre qui le possède.</summary>
	public async Task<(Guild Guild, Member Member)?> FindByUploadTokenAsync(string? token, CancellationToken ct = default)
	{
		if (string.IsNullOrWhiteSpace(token))
		{
			return null;
		}
		var member = await db.Members.IgnoreQueryFilters().SingleOrDefaultAsync(m => m.UploadToken == token, ct);
		if (member is null)
		{
			return null;
		}
		var guild = await db.Guilds.SingleAsync(g => g.Id == member.GuildId, ct);
		return (guild, member);
	}

	public async Task<Guild?> FindByDiscordServerAsync(ulong? serverId, CancellationToken ct = default)
	{
		if (serverId is null)
		{
			return null;
		}
		var id = serverId.Value.ToString(CultureInfo.InvariantCulture);
		return await db.Guilds.SingleOrDefaultAsync(g => g.DiscordServerId == id, ct);
	}

	public async Task<Guild?> FindByInviteCodeAsync(string? code, CancellationToken ct = default) =>
		string.IsNullOrWhiteSpace(code) ? null : await db.Guilds.SingleOrDefaultAsync(g => g.InviteCode == code, ct);

	public async Task<Guild?> FindBySlugAsync(string? slug, CancellationToken ct = default) =>
		string.IsNullOrWhiteSpace(slug) ? null : await db.Guilds.SingleOrDefaultAsync(g => g.Slug == slug, ct);

	public async Task<Guild> CreateAsync(string name, string? discordServerId, string? createdByDiscordId,
		bool syncNicknames, string? ordersChannelId, CancellationToken ct = default)
	{
		var guild = new Guild
		{
			Name = name.Trim(),
			Slug = await UniqueSlugAsync(name, ct),
			DiscordServerId = discordServerId,
			ApiToken = NewApiToken(),
			InviteCode = NewInviteCode(),
			SyncNicknames = syncNicknames,
			OrdersChannelId = ordersChannelId,
			CreatedUtc = DateTime.UtcNow,
			CreatedByDiscordId = createdByDiscordId,
		};
		db.Guilds.Add(guild);
		await db.SaveChangesAsync(ct);
		return guild;
	}

	/// <summary>Nouveau token et nouveau lien d'installation : les anciens packs cessent de fonctionner.</summary>
	public async Task RegenerateSecretsAsync(Guild guild, CancellationToken ct = default)
	{
		guild.ApiToken = NewApiToken();
		guild.InviteCode = NewInviteCode();
		await db.SaveChangesAsync(ct);
	}

	/// <summary>Supprime la guilde et, en cascade, toutes ses données.</summary>
	public async Task DeleteAsync(Guild guild, CancellationToken ct = default)
	{
		db.Guilds.Remove(guild);
		await db.SaveChangesAsync(ct);
	}

	public static string NewApiToken() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

	// Code court et lisible pour un lien partagé (sans 0/O/1/l ambigus).
	public static string NewInviteCode()
	{
		const string alphabet = "abcdefghijkmnpqrstuvwxyz23456789";
		return string.Create(10, alphabet, (span, chars) =>
		{
			for (var i = 0; i < span.Length; i++)
			{
				span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
			}
		});
	}

	public static string Slugify(string name)
	{
		var decomposed = name.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
		var builder = new StringBuilder();
		foreach (var c in decomposed)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
			{
				continue;
			}
			if (char.IsAsciiLetterOrDigit(c))
			{
				builder.Append(c);
			}
			else if (builder.Length > 0 && builder[^1] != '-')
			{
				builder.Append('-');
			}
		}
		var slug = builder.ToString().Trim('-');
		if (slug.Length > 40)
		{
			slug = slug[..40].TrimEnd('-');
		}
		return slug.Length == 0 ? "guilde" : slug;
	}

	private async Task<string> UniqueSlugAsync(string name, CancellationToken ct)
	{
		var baseSlug = Slugify(name);
		var slug = baseSlug;
		for (var suffix = 2; await db.Guilds.AnyAsync(g => g.Slug == slug, ct); suffix++)
		{
			slug = $"{baseSlug}-{suffix}";
		}
		return slug;
	}
}
