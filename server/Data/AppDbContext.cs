using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

/// <summary>
/// Multi-guildes : chaque lecture des données d'une guilde est filtrée sur la
/// guilde courante (GuildContext), et chaque écriture est estampillée avec
/// elle. Une requête « système » (retrouver une guilde par token, tâches de
/// fond) passe par la table Guilds, non filtrée, ou par IgnoreQueryFilters().
/// </summary>
public sealed partial class AppDbContext(DbContextOptions<AppDbContext> options, GuildContext? guildContext = null) : DbContext(options)
{
	public DbSet<Guild> Guilds => Set<Guild>();

	// -1 : aucune guilde ne porte cet id, donc rien de visible sans guilde courante.
	private int CurrentGuildId => guildContext?.GuildId ?? -1;

	/// <summary>
	/// Le modèle est déclaré par module (fichiers partiels AppDbContext.{Module}.cs
	/// dans Modules/{Module}/Infrastructure) : chaque module décrit ses tables,
	/// ses index et son filtre d'isolation. Un seul fichier SQLite et une seule
	/// chaîne de migrations restent partagés, l'historique existant est conservé.
	/// Exception connue à l'indépendance des modules : une erreur de
	/// configuration dans l'un des fichiers partiels empêche de construire le
	/// modèle, donc tous les modules (et Program refuse de démarrer si la
	/// migration échoue). Compromis assumé d'une base unique ;
	/// MigrationTests construit le modèle et le compare à l'historique, pour
	/// qu'une telle erreur casse les tests plutôt que la production.
	/// </summary>
	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Guild>().HasIndex(g => g.Slug).IsUnique();
		modelBuilder.Entity<Guild>().HasIndex(g => g.ApiToken).IsUnique();
		modelBuilder.Entity<Guild>().HasIndex(g => g.InviteCode).IsUnique();
		modelBuilder.Entity<Guild>().HasIndex(g => g.DiscordServerId).IsUnique();

		ConfigureAnnuaire(modelBuilder);
		ConfigureArtisanat(modelBuilder);
		ConfigureEvenements(modelBuilder);
		ConfigurePresence(modelBuilder);
		ConfigureQuetes(modelBuilder);
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
