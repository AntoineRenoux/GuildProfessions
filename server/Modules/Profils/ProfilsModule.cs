using GuildProfessions.Server.Modules.Profils.Application;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Profils;

/// <summary>Module Profils : profils des joueurs (principal, rerolls, rôles, disponibilités), talents, arrivées et départs, accueil des nouveaux.</summary>
public static class ProfilsModule
{
	public static IServiceCollection AddProfilsModule(this IServiceCollection services)
	{
		services.AddScoped<ProfileService>();
		services.AddUploadContributor<ProfilsUploadContributor>();
		services.AddExportContributor<ProfilsExportContributor>();
		return services;
	}
}
