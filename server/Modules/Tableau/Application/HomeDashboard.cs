using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Tableau.Application;

/// <summary>Cartes du tableau de bord, et modules dont la carte n'a pas pu être construite.</summary>
public sealed record HomeView(IReadOnlyList<HomeCard> Cards, IReadOnlyList<string> FailedModules);

/// <summary>
/// Tableau de bord : une carte par module, construite par le module lui-même
/// (IHomeContributor). Ce module ne connaît aucun autre module : il assemble.
/// Un module en panne (construction ou carte) est journalisé et n'a pas de
/// carte ; les autres s'affichent normalement.
/// </summary>
public sealed class HomeDashboard(IServiceProvider services, IEnumerable<HomeContributorType> contributorTypes, ILogger<HomeDashboard> logger)
{
	public async Task<HomeView> BuildAsync(HomeViewer viewer, CancellationToken ct = default)
	{
		var failed = new List<string>();
		var contributors = new List<IHomeContributor>();
		foreach (var registration in contributorTypes)
		{
			try
			{
				contributors.Add((IHomeContributor)services.GetRequiredService(registration.Type));
			}
			catch (Exception exception) when (!ct.IsCancellationRequested)
			{
				logger.LogError(exception, "Tableau : la carte {Contributor} ne peut pas être construite.", registration.Type.Name);
				failed.Add(registration.Type.Name);
			}
		}
		var cards = new List<HomeCard>();
		foreach (var contributor in contributors.OrderBy(c => c.Order))
		{
			try
			{
				if (await contributor.BuildAsync(viewer, ct) is { } card)
				{
					cards.Add(card);
				}
			}
			catch (Exception exception) when (!ct.IsCancellationRequested)
			{
				logger.LogError(exception, "Tableau : la carte du module {Module} est en échec.", contributor.Module);
				failed.Add(contributor.Module);
			}
		}
		return new HomeView(cards, failed);
	}
}
