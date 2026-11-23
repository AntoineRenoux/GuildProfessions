using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

// Tables du module Communication : annonces, modèles, charte, objectifs et réglages.
public sealed partial class AppDbContext
{
	public DbSet<Announcement> Announcements => Set<Announcement>();
	public DbSet<AnnouncementTemplate> AnnouncementTemplates => Set<AnnouncementTemplate>();
	public DbSet<HandbookSection> HandbookSections => Set<HandbookSection>();
	public DbSet<GuildGoal> GuildGoals => Set<GuildGoal>();
	public DbSet<GoalContribution> GoalContributions => Set<GoalContribution>();
	public DbSet<CommunicationSettings> CommunicationSettings => Set<CommunicationSettings>();

	private void ConfigureCommunication(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Announcement>().HasOne<Guild>().WithMany().HasForeignKey(a => a.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<Announcement>().HasIndex(a => new { a.GuildId, a.ClientId }).IsUnique();
		modelBuilder.Entity<Announcement>().HasIndex(a => new { a.GuildId, a.CreatedAt });
		modelBuilder.Entity<Announcement>().HasQueryFilter(a => a.GuildId == CurrentGuildId);

		modelBuilder.Entity<AnnouncementTemplate>().HasOne<Guild>().WithMany().HasForeignKey(t => t.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<AnnouncementTemplate>().HasIndex(t => new { t.GuildId, t.Key }).IsUnique();
		modelBuilder.Entity<AnnouncementTemplate>().HasQueryFilter(t => t.GuildId == CurrentGuildId);

		modelBuilder.Entity<HandbookSection>().HasOne<Guild>().WithMany().HasForeignKey(h => h.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<HandbookSection>().HasIndex(h => new { h.GuildId, h.Key }).IsUnique();
		modelBuilder.Entity<HandbookSection>().HasQueryFilter(h => h.GuildId == CurrentGuildId);

		modelBuilder.Entity<GuildGoal>().HasOne<Guild>().WithMany().HasForeignKey(g => g.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<GuildGoal>().HasIndex(g => new { g.GuildId, g.ClientId }).IsUnique();
		modelBuilder.Entity<GuildGoal>().HasMany(g => g.Contributions).WithOne(c => c.Goal).HasForeignKey(c => c.GoalId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<GuildGoal>().HasQueryFilter(g => g.GuildId == CurrentGuildId);
		modelBuilder.Entity<GoalContribution>().HasIndex(c => new { c.GoalId, c.ClientId }).IsUnique();
		modelBuilder.Entity<GoalContribution>().HasQueryFilter(c => c.Goal.GuildId == CurrentGuildId);

		modelBuilder.Entity<CommunicationSettings>().HasOne<Guild>().WithMany().HasForeignKey(s => s.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<CommunicationSettings>().HasIndex(s => s.GuildId).IsUnique();
		modelBuilder.Entity<CommunicationSettings>().HasQueryFilter(s => s.GuildId == CurrentGuildId);
	}
}
