using GuildProfessions.Server.Modules.Evenements.Application;
using GuildProfessions.Server.Modules.Evenements.Contracts;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Evenements;

/// <summary>Module Événements : agenda, inscriptions, roster et soirée de raid (réservations, soutien).</summary>
public static class EvenementsModule
{
	public static IServiceCollection AddEvenementsModule(this IServiceCollection services)
	{
		services.AddScoped<EventService>();
		services.AddScoped<IEvenements, EvenementsQueries>();
		services.AddUploadContributor<EvenementsUploadContributor>();
		services.AddExportContributor<EvenementsExportContributor>();
		return services;
	}
}
