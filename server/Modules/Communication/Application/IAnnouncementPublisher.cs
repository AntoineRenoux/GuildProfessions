namespace GuildProfessions.Server.Modules.Communication.Application;

/// <summary>
/// Publication d'une annonce hors de l'application (salon Discord de la
/// guilde). L'implémentation vit dans la présentation : l'application ne
/// connaît pas Discord. Une publication impossible ne doit jamais faire
/// échouer l'enregistrement de l'annonce.
/// </summary>
public interface IAnnouncementPublisher
{
	Task PublishAsync(string channelId, string title, string body, string author, CancellationToken ct = default);
}

/// <summary>Publication désactivée (tests, bot absent).</summary>
public sealed class NullAnnouncementPublisher : IAnnouncementPublisher
{
	public Task PublishAsync(string channelId, string title, string body, string author, CancellationToken ct = default) => Task.CompletedTask;
}
