using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

/// <summary>
/// Multi-guildes : chaque lecture des données d'une guilde est filtrée sur la
/// guilde courante (GuildContext), et chaque écriture est estampillée avec
/// elle. Une requête « système » (retrouver une guilde par token, tâches de
/// fond) passe par la table Guilds, non filtrée, ou par IgnoreQueryFilters().
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options, GuildContext? guildContext = null) : DbContext(options)
{
	public DbSet<Guild> Guilds => Set<Guild>();
	public DbSet<Member> Members => Set<Member>();
	public DbSet<Character> Characters => Set<Character>();
	public DbSet<Profession> Professions => Set<Profession>();
	public DbSet<Recipe> Recipes => Set<Recipe>();
	public DbSet<CraftOrder> CraftOrders => Set<CraftOrder>();

	// -1 : aucune guilde ne porte cet id, donc rien de visible sans guilde courante.
	private int CurrentGuildId => guildContext?.GuildId ?? -1;

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Guild>().HasIndex(g => g.Slug).IsUnique();
		modelBuilder.Entity<Guild>().HasIndex(g => g.ApiToken).IsUnique();
		modelBuilder.Entity<Guild>().HasIndex(g => g.InviteCode).IsUnique();
		modelBuilder.Entity<Guild>().HasIndex(g => g.DiscordServerId).IsUnique();

		modelBuilder.Entity<Member>().HasOne<Guild>().WithMany().HasForeignKey(m => m.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<Character>().HasOne<Guild>().WithMany().HasForeignKey(c => c.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<CraftOrder>().HasOne<Guild>().WithMany().HasForeignKey(o => o.GuildId).OnDelete(DeleteBehavior.Cascade);

		modelBuilder.Entity<Member>()
			.HasIndex(m => new { m.GuildId, m.DiscordId })
			.IsUnique();

		modelBuilder.Entity<Member>()
			.HasIndex(m => m.UploadToken)
			.IsUnique();

		modelBuilder.Entity<Character>()
			.HasIndex(c => new { c.GuildId, c.Name })
			.IsUnique();

		modelBuilder.Entity<Character>()
			.HasOne(c => c.Member)
			.WithMany(m => m.Characters)
			.HasForeignKey(c => c.MemberId)
			.OnDelete(DeleteBehavior.SetNull);

		modelBuilder.Entity<Member>()
			.HasOne(m => m.MainCharacter)
			.WithMany()
			.HasForeignKey(m => m.MainCharacterId)
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
			.HasIndex(o => new { o.GuildId, o.ClientId })
			.IsUnique();

		// Isolation : filtres globaux, évalués à chaque requête avec la guilde courante.
		modelBuilder.Entity<Member>().HasQueryFilter(m => m.GuildId == CurrentGuildId);
		modelBuilder.Entity<Character>().HasQueryFilter(c => c.GuildId == CurrentGuildId);
		modelBuilder.Entity<CraftOrder>().HasQueryFilter(o => o.GuildId == CurrentGuildId);
		modelBuilder.Entity<Profession>().HasQueryFilter(p => p.Character.GuildId == CurrentGuildId);
		modelBuilder.Entity<Recipe>().HasQueryFilter(r => r.Profession.Character.GuildId == CurrentGuildId);
	}

	public override int SaveChanges(bool acceptAllChangesOnSuccess)
	{
		StampGuild();
		return base.SaveChanges(acceptAllChangesOnSuccess);
	}

	public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
	{
		StampGuild();
		return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
	}

	// Toute nouvelle donnée reçoit la guilde courante ; une donnée d'une autre
	// guilde ne peut être ni créée ni modifiée dans ce traitement.
	private void StampGuild()
	{
		foreach (var entry in ChangeTracker.Entries<IGuildOwned>())
		{
			if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted))
			{
				continue;
			}
			var current = guildContext?.GuildId
				?? throw new InvalidOperationException("Écriture de données de guilde sans guilde courante.");
			if (entry.State == EntityState.Added && entry.Entity.GuildId == 0)
			{
				entry.Entity.GuildId = current;
			}
			else if (entry.Entity.GuildId != current)
			{
				throw new InvalidOperationException("Écriture refusée : la donnée appartient à une autre guilde.");
			}
		}
	}
}
