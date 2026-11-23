using GuildProfessions.Server.Data;
using GuildProfessions.Server.Shared;

namespace GuildProfessions.Server.Modules.Artisanat.Application;

/// <summary>
/// Notifications Discord du module Artisanat (messages privés, salon des
/// commandes). Implémentée dans l'infrastructure du module ; chaque méthode
/// doit être fournie explicitement.
/// </summary>
public interface ICraftOrderNotifier
{
	/// <param name="announceChannelId">Salon Discord où annoncer la commande (option de la guilde).</param>
	Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId, string? announceChannelId = null, BotLanguage language = BotLanguage.French);

	/// <summary>Le demandeur apprend un changement fait par l'artisan (accepté, prêt, envoyé, annulé).</summary>
	Task NotifyStatusChangedAsync(CraftOrder order, string? requesterDiscordId, BotLanguage language = BotLanguage.French);

	/// <summary>Le demandeur reçoit une proposition d'artisan.</summary>
	Task NotifyOfferAsync(CraftOrder order, CraftOffer offer, string? requesterDiscordId, BotLanguage language = BotLanguage.French);

	/// <summary>L'artisan apprend un changement fait par le demandeur (offre retenue, réception confirmée, annulation).</summary>
	Task NotifyCrafterAsync(CraftOrder order, string? crafterDiscordId, BotLanguage language = BotLanguage.French);

	/// <summary>Demande ouverte : les artisans qui connaissent la recette sont prévenus.</summary>
	Task NotifyOpenRequestAsync(CraftOrder order, IReadOnlyList<string> crafterDiscordIds, BotLanguage language = BotLanguage.French);
}
