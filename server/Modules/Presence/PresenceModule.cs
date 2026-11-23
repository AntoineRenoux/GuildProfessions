using GuildProfessions.Server.Modules.Presence.Application;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Presence;

/// <summary>Module Présence : raids réellement joués, butin reçu et corrections d'officiers.</summary>
public static class PresenceModule
{
	public static IServiceCollection AddPresenceModule(this IServiceCollection services)
	{
		services.AddScoped<AttendanceService>();
		services.AddUploadContributor<PresenceUploadContributor>();
		services.AddExportContributor<PresenceExportContributor>();
		services.AddHomeContributor<PresenceHomeCard>();
		return services;
	}
}
