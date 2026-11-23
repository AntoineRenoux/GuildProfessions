using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

// Tables du module Annuaire : membres, personnages, métiers et recettes connues.
public sealed partial class AppDbContext
{
	public DbSet<Member> Members => Set<Member>();
	public DbSet<Character> Characters => Set<Character>();
	public DbSet<Profession> Professions => Set<Profession>();
	public DbSet<Recipe> Recipes => Set<Recipe>();

	private void ConfigureAnnuaire(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Member>().HasOne<Guild>().WithMany().HasForeignKey(m => m.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<Character>().HasOne<Guild>().WithMany().HasForeignKey(c => c.GuildId).OnDelete(DeleteBehavior.Cascade);

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

		// Isolation : filtres globaux, évalués à chaque requête avec la guilde courante.
		modelBuilder.Entity<Member>().HasQueryFilter(m => m.GuildId == CurrentGuildId);
		modelBuilder.Entity<Character>().HasQueryFilter(c => c.GuildId == CurrentGuildId);
		modelBuilder.Entity<Profession>().HasQueryFilter(p => p.Character.GuildId == CurrentGuildId);
		modelBuilder.Entity<Recipe>().HasQueryFilter(r => r.Profession.Character.GuildId == CurrentGuildId);
	}
}
