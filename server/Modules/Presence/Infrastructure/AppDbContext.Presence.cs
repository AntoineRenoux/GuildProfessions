using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

// Tables du module Présence : raids relevés en jeu, participants, butin et corrections d'officiers.
public sealed partial class AppDbContext
{
	public DbSet<RaidSession> RaidSessions => Set<RaidSession>();
	public DbSet<RaidParticipant> RaidParticipants => Set<RaidParticipant>();
	public DbSet<RaidLoot> RaidLoot => Set<RaidLoot>();
	public DbSet<AttendanceCorrection> AttendanceCorrections => Set<AttendanceCorrection>();

	private void ConfigurePresence(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<RaidSession>().HasOne<Guild>().WithMany().HasForeignKey(s => s.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<RaidSession>().HasIndex(s => new { s.GuildId, s.Key }).IsUnique();
		modelBuilder.Entity<RaidSession>().HasIndex(s => new { s.GuildId, s.StartedAt });
		// Lien vers l'événement du soir : simple clé, mise à null si l'événement disparaît.
		modelBuilder.Entity<RaidSession>().HasOne<GuildEvent>().WithMany().HasForeignKey(s => s.EventId).OnDelete(DeleteBehavior.SetNull);
		modelBuilder.Entity<RaidSession>().HasMany(s => s.Participants).WithOne(p => p.Session).HasForeignKey(p => p.SessionId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<RaidSession>().HasMany(s => s.Loot).WithOne(l => l.Session).HasForeignKey(l => l.SessionId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<RaidSession>().HasMany(s => s.Corrections).WithOne(c => c.Session).HasForeignKey(c => c.SessionId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<RaidParticipant>().HasIndex(p => new { p.SessionId, p.Character }).IsUnique();
		modelBuilder.Entity<RaidLoot>().HasIndex(l => new { l.SessionId, l.Character, l.ItemId, l.Ordinal }).IsUnique();
		modelBuilder.Entity<AttendanceCorrection>().HasIndex(c => new { c.SessionId, c.Character }).IsUnique();

		modelBuilder.Entity<RaidSession>().HasQueryFilter(s => s.GuildId == CurrentGuildId);
		modelBuilder.Entity<RaidParticipant>().HasQueryFilter(p => p.Session.GuildId == CurrentGuildId);
		modelBuilder.Entity<RaidLoot>().HasQueryFilter(l => l.Session.GuildId == CurrentGuildId);
		modelBuilder.Entity<AttendanceCorrection>().HasQueryFilter(c => c.Session.GuildId == CurrentGuildId);
	}
}
