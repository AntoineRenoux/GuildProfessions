using GuildProfessions.Server.Data;

namespace GuildProfessions.Server.Tests;

/// <summary>Crée une guilde de test et en fait la guilde courante.</summary>
internal static class TestGuilds
{
	public static Guild Create(AppDbContext db, string name, string slug)
	{
		var guild = new Guild
		{
			Name = name,
			Slug = slug,
			ApiToken = "token-" + slug,
			InviteCode = "invite-" + slug,
			CreatedUtc = DateTime.UtcNow,
		};
		db.Guilds.Add(guild);
		db.SaveChanges();
		return guild;
	}
}
