using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire;
using GuildProfessions.Server.Modules.Artisanat;
using GuildProfessions.Server.Modules.Evenements;
using GuildProfessions.Server.Modules.Presence;
using GuildProfessions.Server.Modules.Quetes;
using GuildProfessions.Server.Services;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server;

/// <summary>
/// Composition de l'application : noyau partagé, guildes, orchestrateurs
/// d'envoi et d'export, puis un appel par module. Utilisée par Program et
/// par les tests, pour vérifier le câblage réel.
/// </summary>
public static class GuildkeepServices
{
	public static IServiceCollection AddGuildkeep(this IServiceCollection services)
	{
		// Noyau partagé : guilde courante d'un traitement (requête HTTP ou interaction Discord), langue, version des données.
		services.AddScoped<GuildContext>();
		services.AddScoped<BotLocalizer>();
		services.AddScoped<IDataVersion, DataVersion>();

		// Guildes (inscription, liens d'installation) et orchestrateurs.
		services.AddScoped<GuildDirectory>();
		services.AddSingleton<SiteLinks>();
		services.AddSingleton<GuildPackBuilder>();
		services.AddScoped<UploadIngestion>();
		services.AddScoped<ExportBuilder>();

		services.AddAnnuaireModule();
		services.AddArtisanatModule();
		services.AddEvenementsModule();
		services.AddPresenceModule();
		services.AddQuetesModule();
		return services;
	}
}
