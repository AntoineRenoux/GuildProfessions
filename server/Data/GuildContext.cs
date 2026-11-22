namespace GuildProfessions.Server.Data;

/// <summary>
/// Guilde courante d'une requête (API : token ; bot : serveur Discord), posée
/// une fois au début du traitement. AppDbContext s'en sert pour filtrer toutes
/// les lectures et estampiller toutes les écritures. Sans guilde courante,
/// les données des guildes sont invisibles : l'isolation est fermée par défaut.
/// </summary>
public sealed class GuildContext
{
	public Guild? Guild { get; private set; }

	public int? GuildId => Guild?.Id;

	public void Use(Guild guild)
	{
		if (Guild is not null && Guild.Id != guild.Id)
		{
			throw new InvalidOperationException("La guilde courante est déjà définie pour ce traitement.");
		}
		Guild = guild;
	}

	public Guild Required => Guild ?? throw new InvalidOperationException("Aucune guilde courante pour ce traitement.");
}
