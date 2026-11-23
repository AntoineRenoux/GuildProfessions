using GuildProfessions.Server.Modules.Quetes.Application;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Quetes;

/// <summary>Module Quêtes : quêtes élites, de donjon et de raid partagées entre membres.</summary>
public static class QuetesModule
{
	public static IServiceCollection AddQuetesModule(this IServiceCollection services)
	{
		services.AddScoped<QuestService>();
		services.AddUploadContributor<QuetesUploadContributor>();
		services.AddExportContributor<QuetesExportContributor>();
		return services;
	}
}
