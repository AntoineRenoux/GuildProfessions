using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Data;

// Tables du module Artisanat : commandes, offres des artisans et catalogue des recettes.
public sealed partial class AppDbContext
{
	public DbSet<CraftOrder> CraftOrders => Set<CraftOrder>();
	public DbSet<CraftOffer> CraftOffers => Set<CraftOffer>();
	public DbSet<CatalogRecipe> CatalogRecipes => Set<CatalogRecipe>();

	private void ConfigureArtisanat(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<CraftOrder>().HasOne<Guild>().WithMany().HasForeignKey(o => o.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<CraftOrder>()
			.HasIndex(o => new { o.GuildId, o.ClientId })
			.IsUnique();
		modelBuilder.Entity<CraftOrder>().HasMany(o => o.Offers).WithOne(f => f.Order).HasForeignKey(f => f.OrderId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<CraftOffer>().HasIndex(f => new { f.OrderId, f.Crafter }).IsUnique();
		modelBuilder.Entity<CatalogRecipe>().HasOne<Guild>().WithMany().HasForeignKey(r => r.GuildId).OnDelete(DeleteBehavior.Cascade);
		modelBuilder.Entity<CatalogRecipe>().HasIndex(r => new { r.GuildId, r.SpellId }).IsUnique();

		modelBuilder.Entity<CraftOrder>().HasQueryFilter(o => o.GuildId == CurrentGuildId);
		modelBuilder.Entity<CraftOffer>().HasQueryFilter(f => f.Order.GuildId == CurrentGuildId);
		modelBuilder.Entity<CatalogRecipe>().HasQueryFilter(r => r.GuildId == CurrentGuildId);
	}
}
