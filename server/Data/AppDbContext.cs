using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
	public DbSet<Member> Members => Set<Member>();
	public DbSet<Character> Characters => Set<Character>();
	public DbSet<Profession> Professions => Set<Profession>();
	public DbSet<Recipe> Recipes => Set<Recipe>();
	public DbSet<CraftOrder> CraftOrders => Set<CraftOrder>();
	public DbSet<Meta> Metas => Set<Meta>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Member>()
			.HasIndex(m => m.DiscordId)
			.IsUnique();

		modelBuilder.Entity<Character>()
			.HasIndex(c => c.Name)
			.IsUnique();

		modelBuilder.Entity<Character>()
			.HasOne(c => c.Member)
			.WithMany(m => m.Characters)
			.HasForeignKey(c => c.MemberId)
			.OnDelete(DeleteBehavior.SetNull);

		modelBuilder.Entity<Profession>()
			.HasIndex(p => new { p.CharacterId, p.Name })
			.IsUnique();

		modelBuilder.Entity<Profession>()
			.HasMany(p => p.Recipes)
			.WithOne(r => r.Profession)
			.HasForeignKey(r => r.ProfessionId)
			.OnDelete(DeleteBehavior.Cascade);

		modelBuilder.Entity<Recipe>()
			.HasIndex(r => new { r.ProfessionId, r.SpellId })
			.IsUnique();

		modelBuilder.Entity<CraftOrder>()
			.HasIndex(o => o.ClientId)
			.IsUnique();

		modelBuilder.Entity<Meta>()
			.HasKey(m => m.Key);
	}
}
