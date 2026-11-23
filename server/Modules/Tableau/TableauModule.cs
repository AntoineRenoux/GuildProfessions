using GuildProfessions.Server.Modules.Tableau.Application;

namespace GuildProfessions.Server.Modules.Tableau;

/// <summary>Module Tableau de bord : assemble les cartes que chaque module fournit (/gkeep tableau voir).</summary>
public static class TableauModule
{
	public static IServiceCollection AddTableauModule(this IServiceCollection services)
	{
		services.AddScoped<HomeDashboard>();
		return services;
	}
}
