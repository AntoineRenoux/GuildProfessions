namespace GuildProfessions.Server.Shared.Modules;

/// <summary>Qui regarde le tableau de bord sur Discord.</summary>
public sealed record HomeViewer(string DiscordId, bool IsServerAdmin);

/// <summary>Carte d'un module sur le tableau de bord : un titre, quelques lignes, la commande pour aller plus loin.</summary>
public sealed record HomeCard(string Title, IReadOnlyList<string> Lines, string? Command = null);

/// <summary>
/// Part d'un module dans le tableau de bord. Appelée isolément : un module en
/// panne n'a pas de carte, les autres s'affichent normalement. Null quand le
/// module n'a rien à montrer.
/// </summary>
public interface IHomeContributor
{
	string Module { get; }

	/// <summary>Place de la carte sur le tableau (les plus petits d'abord).</summary>
	int Order { get; }

	Task<HomeCard?> BuildAsync(HomeViewer viewer, CancellationToken ct);
}
