namespace GuildProfessions.Server.Shared.Modules;

/// <summary>Type d'un contributeur à l'envoi, enregistré par son module (résolu à la demande par l'orchestrateur).</summary>
public sealed record UploadContributorType(Type Type);

/// <summary>Type d'un contributeur à l'export, enregistré par son module (résolu à la demande par l'orchestrateur).</summary>
public sealed record ExportContributorType(Type Type);

/// <summary>Type d'une carte du tableau de bord, enregistrée par son module.</summary>
public sealed record HomeContributorType(Type Type);

/// <summary>
/// Enregistrement commun aux modules : leurs contributeurs à l'envoi, à
/// l'export et au tableau de bord. Chaque contributeur est aussi connu par son type : les
/// orchestrateurs le construisent un par un, dans leur propre try, si bien
/// qu'un enregistrement manquant ou un constructeur en échec dans un module
/// n'empêche pas les autres d'être appliqués.
/// </summary>
public static class ModuleRegistration
{
	public static IServiceCollection AddUploadContributor<T>(this IServiceCollection services)
		where T : class, IUploadContributor
	{
		services.AddScoped<T>();
		services.AddScoped<IUploadContributor>(provider => provider.GetRequiredService<T>());
		services.AddSingleton(new UploadContributorType(typeof(T)));
		return services;
	}

	public static IServiceCollection AddExportContributor<T>(this IServiceCollection services)
		where T : class, IExportContributor
	{
		services.AddScoped<T>();
		services.AddScoped<IExportContributor>(provider => provider.GetRequiredService<T>());
		services.AddSingleton(new ExportContributorType(typeof(T)));
		return services;
	}

	public static IServiceCollection AddHomeContributor<T>(this IServiceCollection services)
		where T : class, IHomeContributor
	{
		services.AddScoped<T>();
		services.AddSingleton(new HomeContributorType(typeof(T)));
		return services;
	}
}
