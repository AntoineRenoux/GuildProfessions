using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

// Table du module Quêtes : journal de quêtes partageables de chaque personnage.
public sealed partial class AppDbContext
{
	public DbSet<CharacterQuestLog> CharacterQuestLogs => Set<CharacterQuestLog>();

	private void ConfigureQuetes(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<CharacterQuestLog>().HasOne<Guild>().WithMany().HasForeignKey(q => q.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<CharacterQuestLog>().HasIndex(q => new { q.GuildId, q.Character }).IsUnique();
		modelBuilder.Entity<CharacterQuestLog>().HasQueryFilter(q => q.GuildId == CurrentGuildId);
	}
}
