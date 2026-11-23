using GuildProfessions.Server.Modules.Communication.Application;
using GuildProfessions.Server.Modules.Communication.Presentation;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Communication;

/// <summary>Module Communication : annonces et modèles, charte de la guilde, objectifs et contributions.</summary>
public static class CommunicationModule
{
	public static IServiceCollection AddCommunicationModule(this IServiceCollection services)
	{
		services.AddScoped<CommunicationService>();
		services.AddSingleton<IAnnouncementPublisher, DiscordAnnouncementPublisher>();
		services.AddUploadContributor<CommunicationUploadContributor>();
		services.AddExportContributor<CommunicationExportContributor>();
		services.AddHomeContributor<CommunicationHomeCard>();
		return services;
	}
}
