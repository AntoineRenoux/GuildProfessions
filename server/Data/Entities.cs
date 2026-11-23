namespace GuildProfessions.Server.Data;

// Entités partagées par tous les modules : la guilde, unité d'isolation.

public enum DataSource
{
	/// <summary>Déclaré à la main via le bot Discord.</summary>
	Discord = 0,

	/// <summary>Scanné en jeu par l'addon (prioritaire : donnée exacte).</summary>
	Addon = 1,
}

/// <summary>
/// Une guilde inscrite : l'unité d'isolation de toutes les données. Créée par
/// l'assistant /setup sur Discord (ou reprise de la config pour la guilde
/// historique).
/// </summary>
public sealed class Guild
{
	public int Id { get; set; }

	/// <summary>Nom exact de la guilde en jeu : filtre des rerolls et clé de répartition côté compagnon.</summary>
	public required string Name { get; set; }

	/// <summary>Identifiant lisible et unique de la guilde (index unique ; aucune route ne l'utilise encore).</summary>
	public required string Slug { get; set; }

	/// <summary>Serveur Discord rattaché (null tant que /setup n'a pas été fait sur Discord).</summary>
	public string? DiscordServerId { get; set; }

	/// <summary>Token de l'API embarqué dans le pack de la guilde (régénérable par l'admin).</summary>
	public required string ApiToken { get; set; }

	/// <summary>Code public du lien d'installation partagé aux joueurs (régénéré avec le token).</summary>
	public required string InviteCode { get; set; }

	/// <summary>Le bot renomme les membres avec leur personnage principal (choix de l'admin, non par défaut).</summary>
	public bool SyncNicknames { get; set; }

	/// <summary>Salon où annoncer les nouvelles commandes de craft (optionnel).</summary>
	public string? OrdersChannelId { get; set; }

	/// <summary>Langue de la guilde (« fr » ou « en ») : messages privés et réponses hors interaction.</summary>
	public string Locale { get; set; } = "fr";

	/// <summary>Compteur de version de l'export de cette guilde.</summary>
	public long DataVersion { get; set; }

	public DateTime CreatedUtc { get; set; }
	public string? CreatedByDiscordId { get; set; }

	/// <summary>Date à laquelle le bot a été retiré du serveur (suppression différée des données).</summary>
	public DateTime? BotRemovedUtc { get; set; }
}

/// <summary>Donnée appartenant à une guilde : isolée par le filtre global d'AppDbContext.</summary>
public interface IGuildOwned
{
	int GuildId { get; set; }
}
