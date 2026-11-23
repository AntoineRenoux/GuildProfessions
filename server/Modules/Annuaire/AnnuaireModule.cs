using GuildProfessions.Server.Modules.Annuaire.Application;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Annuaire;

/// <summary>Module Annuaire : membres Discord, personnages, métiers et recettes connues.</summary>
public static class AnnuaireModule
{
	public static IServiceCollection AddAnnuaireModule(this IServiceCollection services)
	{
		services.AddScoped<RosterService>();
		services.AddScoped<AnnuaireReader>();
		services.AddScoped<IAnnuaire, AnnuaireQueries>();
		services.AddUploadContributor<AnnuaireUploadContributor>();
		services.AddExportContributor<AnnuaireExportContributor>();
		return services;
	}
}
