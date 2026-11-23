using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

// Tables du module Profils : profils, talents, versions de l'addon, arrivées et départs, accueil.
public sealed partial class AppDbContext
{
	public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();
	public DbSet<CharacterTalents> CharacterTalents => Set<CharacterTalents>();
	public DbSet<AddonVersionReport> AddonVersionReports => Set<AddonVersionReport>();
	public DbSet<RosterChange> RosterChanges => Set<RosterChange>();
	public DbSet<OnboardingStep> OnboardingSteps => Set<OnboardingStep>();

	private void ConfigureProfils(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<PlayerProfile>().HasOne<Guild>().WithMany().HasForeignKey(p => p.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<PlayerProfile>().HasIndex(p => new { p.GuildId, p.Main }).IsUnique();
		modelBuilder.Entity<PlayerProfile>().HasQueryFilter(p => p.GuildId == CurrentGuildId);

		modelBuilder.Entity<CharacterTalents>().HasOne<Guild>().WithMany().HasForeignKey(t => t.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<CharacterTalents>().HasIndex(t => new { t.GuildId, t.Character }).IsUnique();
		modelBuilder.Entity<CharacterTalents>().HasQueryFilter(t => t.GuildId == CurrentGuildId);

		modelBuilder.Entity<AddonVersionReport>().HasOne<Guild>().WithMany().HasForeignKey(v => v.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<AddonVersionReport>().HasIndex(v => new { v.GuildId, v.Character }).IsUnique();
		modelBuilder.Entity<AddonVersionReport>().HasQueryFilter(v => v.GuildId == CurrentGuildId);

		modelBuilder.Entity<RosterChange>().HasOne<Guild>().WithMany().HasForeignKey(r => r.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<RosterChange>().HasIndex(r => new { r.GuildId, r.At });
		modelBuilder.Entity<RosterChange>().HasQueryFilter(r => r.GuildId == CurrentGuildId);

		modelBuilder.Entity<OnboardingStep>().HasOne<Guild>().WithMany().HasForeignKey(o => o.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<OnboardingStep>().HasIndex(o => new { o.GuildId, o.Character, o.Step }).IsUnique();
		modelBuilder.Entity<OnboardingStep>().HasQueryFilter(o => o.GuildId == CurrentGuildId);
	}
}
