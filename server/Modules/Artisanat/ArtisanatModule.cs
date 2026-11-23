using GuildProfessions.Server.Modules.Artisanat.Application;
using GuildProfessions.Server.Modules.Artisanat.Infrastructure;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Artisanat;

/// <summary>Module Artisanat : commandes de craft, offres des artisans et catalogue des recettes.</summary>
public static class ArtisanatModule
{
	public static IServiceCollection AddArtisanatModule(this IServiceCollection services)
	{
		services.AddScoped<CraftOrderService>();
		services.AddScoped<CatalogService>();
		services.AddSingleton<ICraftOrderNotifier, DiscordCraftOrderNotifier>();
		services.AddUploadContributor<ArtisanatUploadContributor>();
		services.AddExportContributor<ArtisanatExportContributor>();
		services.AddHomeContributor<ArtisanatHomeCard>();
		return services;
	}
}
