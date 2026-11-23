using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

// Tables du module Événements : événements, inscriptions et soirée de raid (réservations, soutien).
public sealed partial class AppDbContext
{
	public DbSet<GuildEvent> Events => Set<GuildEvent>();
	public DbSet<EventSignup> EventSignups => Set<EventSignup>();
	public DbSet<EventReserve> EventReserves => Set<EventReserve>();
	public DbSet<EventHardReserve> EventHardReserves => Set<EventHardReserve>();
	public DbSet<EventDuty> EventDuties => Set<EventDuty>();

	private void ConfigureEvenements(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<GuildEvent>().HasOne<Guild>().WithMany().HasForeignKey(e => e.GuildId).OnDelete(DeleteBehavior.Cascade);

		modelBuilder.Entity<GuildEvent>()
			.HasIndex(e => new { e.GuildId, e.ClientId })
			.IsUnique();
		modelBuilder.Entity<GuildEvent>()
			.HasIndex(e => new { e.GuildId, e.StartsAt });

		modelBuilder.Entity<GuildEvent>()
			.HasMany(e => e.Signups)
			.WithOne(s => s.Event)
			.HasForeignKey(s => s.EventId)
			.OnDelete(DeleteBehavior.Cascade);

		modelBuilder.Entity<EventSignup>()
			.HasIndex(s => new { s.EventId, s.Character })
			.IsUnique();

		modelBuilder.Entity<GuildEvent>().HasMany(e => e.Reserves).WithOne(r => r.Event).HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<GuildEvent>().HasMany(e => e.HardReserves).WithOne(r => r.Event).HasForeignKey(r => r.EventId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<GuildEvent>().HasMany(e => e.Duties).WithOne(d => d.Event).HasForeignKey(d => d.EventId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<EventReserve>().HasIndex(r => new { r.EventId, r.Character, r.Priority }).IsUnique();
		modelBuilder.Entity<EventHardReserve>().HasIndex(r => new { r.EventId, r.ItemId }).IsUnique();
		modelBuilder.Entity<EventDuty>().HasIndex(d => new { d.EventId, d.Duty }).IsUnique();

		modelBuilder.Entity<GuildEvent>().HasQueryFilter(e => e.GuildId == CurrentGuildId);
		modelBuilder.Entity<EventSignup>().HasQueryFilter(s => s.Event.GuildId == CurrentGuildId);
		modelBuilder.Entity<EventReserve>().HasQueryFilter(r => r.Event.GuildId == CurrentGuildId);
		modelBuilder.Entity<EventHardReserve>().HasQueryFilter(r => r.Event.GuildId == CurrentGuildId);
		modelBuilder.Entity<EventDuty>().HasQueryFilter(d => d.Event.GuildId == CurrentGuildId);
	}
}
