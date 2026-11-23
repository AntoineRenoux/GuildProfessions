using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Modules.Annuaire.Contracts;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Modules.Artisanat.Application;

/// <summary>
/// Commandes de craft. Deux façons de commander :
/// - directement à un artisan (il accepte ou refuse) ;
/// - par une demande ouverte à tous : les artisans proposent, le demandeur
///   retient une offre. Une demande sans artisan expire au bout de 5 jours.
/// Puis l'artisan signale « prêt à échanger » ou « envoyé », et le demandeur
/// confirme la réception. L'échange et le courrier restent des gestes du jeu.
/// </summary>
public sealed class CraftOrderService(AppDbContext db, IAnnuaire annuaire, IDataVersion dataVersion, ICraftOrderNotifier notifier,
	GuildContext guildContext, BotLocalizer localizer)
{
	private BotLocalizer L => localizer;

	public static readonly TimeSpan OpenLifetime = TimeSpan.FromDays(5);

	// Messages privés et annonces : dans la langue de la guilde (celle du destinataire est inconnue).
	private BotLanguage GuildLanguage => BotLocalizer.FromLocale(guildContext.Guild?.Locale);

	private static readonly CraftOrderStatus[] Active =
		[CraftOrderStatus.Open, CraftOrderStatus.Accepted, CraftOrderStatus.Ready, CraftOrderStatus.Mailed];

	public static bool IsActive(CraftOrderStatus status) => Active.Contains(status);

	private enum Party { Requester, Crafter }

	// Qui peut faire passer une commande d'un état à l'autre.
	private static readonly Dictionary<(CraftOrderStatus From, CraftOrderStatus To), Party[]> Transitions = new()
	{
		[(CraftOrderStatus.Open, CraftOrderStatus.Accepted)] = [Party.Crafter, Party.Requester],
		[(CraftOrderStatus.Open, CraftOrderStatus.Cancelled)] = [Party.Requester, Party.Crafter],
		[(CraftOrderStatus.Accepted, CraftOrderStatus.Ready)] = [Party.Crafter],
		[(CraftOrderStatus.Accepted, CraftOrderStatus.Mailed)] = [Party.Crafter],
		[(CraftOrderStatus.Accepted, CraftOrderStatus.Done)] = [Party.Requester],
		[(CraftOrderStatus.Accepted, CraftOrderStatus.Cancelled)] = [Party.Requester, Party.Crafter],
		[(CraftOrderStatus.Ready, CraftOrderStatus.Mailed)] = [Party.Crafter],
		[(CraftOrderStatus.Ready, CraftOrderStatus.Done)] = [Party.Requester],
		[(CraftOrderStatus.Ready, CraftOrderStatus.Cancelled)] = [Party.Requester, Party.Crafter],
		[(CraftOrderStatus.Mailed, CraftOrderStatus.Done)] = [Party.Requester],
	};

	/// <summary>
	/// Vérifie une transition demandée par un joueur. Pour « accepted » : un
	/// artisan désigné accepte, ou le demandeur retient l'offre de <paramref name="chosenCrafter"/>.
	/// Renvoie null si c'est permis, sinon la raison.
	/// </summary>
	private string? Refusal(CraftOrder order, CraftOrderStatus to, IReadOnlySet<string> identity, string? chosenCrafter)
	{
		if (!Transitions.TryGetValue((order.Status, to), out var parties))
		{
			return L.T($"La commande #{order.Id} est « {Label(order.Status, L)} » : impossible de la passer à « {Label(to, L)} ».",
				$"Order #{order.Id} is \"{Label(order.Status, L)}\": it can't become \"{Label(to, L)}\".");
		}
		var isRequester = identity.Contains(order.RequesterCharacter);
		var isCrafter = order.CrafterCharacter is not null && identity.Contains(order.CrafterCharacter);
		if (to == CraftOrderStatus.Accepted)
		{
			if (isCrafter)
			{
				return null;
			}
			if (isRequester && chosenCrafter is not null &&
				order.Offers.Any(f => !f.Withdrawn && string.Equals(f.Crafter, chosenCrafter, StringComparison.OrdinalIgnoreCase)))
			{
				return null;
			}
			return isRequester
				? L.T("Choisis une offre d'artisan pour cette demande.", "Pick a crafter's offer for this request.")
				: L.T($"Seul l'artisan désigné accepte la commande #{order.Id} ; sinon, fais une offre.",
					$"Only the chosen crafter accepts order #{order.Id}; otherwise, make an offer.");
		}
		if ((parties.Contains(Party.Requester) && isRequester) || (parties.Contains(Party.Crafter) && isCrafter))
		{
			return null;
		}
		return parties.Contains(Party.Requester) && !parties.Contains(Party.Crafter)
			? L.T($"Seul le demandeur peut faire ça sur la #{order.Id}.", $"Only the requester can do this on #{order.Id}.")
			: L.T($"Seul l'artisan peut faire ça sur la #{order.Id}.", $"Only the crafter can do this on #{order.Id}.");
	}

	/// <summary>
	/// Applique une transition sans l'enregistrer : l'appelant enregistre (avec
	/// l'incrément de version) puis envoie <paramref name="afterCommit"/>.
	/// </summary>
	private void ApplyTransition(CraftOrder order, CraftOrderStatus to, string? chosenCrafter, bool byRequester,
		ICollection<Func<Task>> afterCommit)
	{
		if (to == CraftOrderStatus.Accepted && byRequester && chosenCrafter is not null)
		{
			order.CrafterCharacter = order.Offers.First(f => !f.Withdrawn && string.Equals(f.Crafter, chosenCrafter, StringComparison.OrdinalIgnoreCase)).Crafter;
		}
		order.Status = to;
		order.UpdatedUtc = DateTime.UtcNow;
		// On prévient l'autre partie, une fois l'écriture validée.
		if (byRequester)
		{
			afterCommit.Add(async () => await notifier.NotifyCrafterAsync(order,
				await FindDiscordIdAsync(order.CrafterCharacter, CancellationToken.None), GuildLanguage));
		}
		else
		{
			afterCommit.Add(async () => await notifier.NotifyStatusChangedAsync(order,
				await FindDiscordIdAsync(order.RequesterCharacter, CancellationToken.None), GuildLanguage));
		}
	}

	/// <summary>
	/// Les demandes restées ouvertes trop longtemps expirent. Appelé avant chaque
	/// écriture ; une lecture (export, listes) calcule l'état sans rien écrire.
	/// </summary>
	public async Task ExpireStaleAsync(CancellationToken ct = default)
	{
		var now = DateTime.UtcNow;
		var stale = await db.CraftOrders
			.Where(o => o.Status == CraftOrderStatus.Open && o.ExpiresUtc != null && o.ExpiresUtc < now)
			.ToListAsync(ct);
		if (stale.Count == 0)
		{
			return;
		}
		foreach (var order in stale)
		{
			order.Status = CraftOrderStatus.Expired;
			order.UpdatedUtc = now;
		}
		await dataVersion.SaveAndBumpAsync(ct);
	}

	// ---- Discord ------------------------------------------------------------------------------

	public async Task<Result> CreateFromDiscordAsync(
		string discordId, string discordName, string? crafterCharacter, string item,
		int quantity, string? note, int? recipeId = null, CancellationToken ct = default)
	{
		// Le principal du membre, sinon son premier personnage lié, sinon son nom Discord.
		var actor = await annuaire.EnsureDiscordActorAsync(discordId, discordName, ct);
		var requester = actor.Main?.Name ?? discordName;
		recipeId ??= await FindRecipeIdAsync(item, ct);

		var order = NewOrder(requester, string.IsNullOrWhiteSpace(crafterCharacter) ? null : crafterCharacter.Trim(),
			item, quantity, note, recipeId, DataSource.Discord, null);
		db.CraftOrders.Add(order);
		await dataVersion.SaveAndBumpAsync(ct);
		await AnnounceAsync(order, ct);

		return Result.Success(order.CrafterCharacter is null
			? L.T($"Demande **#{order.Id}** publiée : {order.Quantity}× {item}. Les artisans de la guilde peuvent proposer de la faire pendant 5 jours.",
				$"Request **#{order.Id}** posted: {order.Quantity}× {item}. Guild crafters can offer to make it for 5 days.")
			: L.T($"Commande **#{order.Id}** créée : {order.Quantity}× {item} chez **{order.CrafterCharacter}**.",
				$"Order **#{order.Id}** created: {order.Quantity}× {item} from **{order.CrafterCharacter}**."));
	}

	public async Task<Result> UpdateStatusFromDiscordAsync(
		string discordId, int orderId, CraftOrderStatus newStatus, string? chosenCrafter = null, CancellationToken ct = default)
	{
		await ExpireStaleAsync(ct);
		var order = await db.CraftOrders.Include(o => o.Offers).SingleOrDefaultAsync(o => o.Id == orderId, ct);
		if (order is null)
		{
			return Result.NotFound(L.T($"Commande #{orderId} introuvable.", $"Order #{orderId} not found."));
		}
		var actor = await annuaire.DiscordActorAsync(discordId, false, ct);
		var identity = actor.Characters.Select(c => c.Name).ToHashSet();
		// Commande passée depuis Discord par un membre sans personnage lié : son nom Discord fait foi.
		if (actor.DiscordName is not null && actor.DiscordName == order.RequesterCharacter)
		{
			identity.Add(order.RequesterCharacter);
		}
		if (Refusal(order, newStatus, identity, chosenCrafter) is { } refusal)
		{
			return Result.Forbidden(refusal);
		}
		var byRequester = identity.Contains(order.RequesterCharacter) &&
			!(order.CrafterCharacter is not null && identity.Contains(order.CrafterCharacter) && newStatus != CraftOrderStatus.Done);
		var notifications = new List<Func<Task>>();
		ApplyTransition(order, newStatus, chosenCrafter, byRequester, notifications);
		// Changement d'état et version dans la même transaction, puis les messages privés.
		await dataVersion.SaveAndBumpAsync(ct);
		foreach (var send in notifications)
		{
			await send();
		}
		return Result.Success(L.T($"Commande **#{order.Id}** ({order.Item}) : {Label(newStatus, L)}" +
			(newStatus == CraftOrderStatus.Accepted ? $", artisan **{order.CrafterCharacter}**." : "."),
			$"Order **#{order.Id}** ({order.Item}): {Label(newStatus, L)}" +
			(newStatus == CraftOrderStatus.Accepted ? $", crafter **{order.CrafterCharacter}**." : ".")));
	}

	public async Task<Result> OfferFromDiscordAsync(string discordId, int orderId, bool withdraw, string? note,
		string? character, CancellationToken ct = default)
	{
		await ExpireStaleAsync(ct);
		var order = await db.CraftOrders.Include(o => o.Offers).SingleOrDefaultAsync(o => o.Id == orderId, ct);
		if (order is null)
		{
			return Result.NotFound(L.T($"Commande #{orderId} introuvable.", $"Order #{orderId} not found."));
		}
		var crafter = (await annuaire.DiscordActorAsync(discordId, false, ct)).Choose(character);
		if (crafter is null)
		{
			return Result.Invalid(L.T("Aucun personnage lié à ton compte (ou pas celui-là) : utilise `/gkeep lier`.",
				"No linked character (or not that one): use `/gkeep link`."));
		}
		if (ApplyOffer(order, crafter.Name, note, withdraw) is { } refusal)
		{
			return Result.Conflict(refusal);
		}
		await dataVersion.SaveAndBumpAsync(ct);
		if (!withdraw)
		{
			await notifier.NotifyOfferAsync(order, order.Offers.Single(f => f.Crafter == crafter.Name),
				await FindDiscordIdAsync(order.RequesterCharacter, ct), GuildLanguage);
		}
		return Result.Success(withdraw
			? L.T($"Offre de **{crafter.Name}** retirée de la #{order.Id}.", $"**{crafter.Name}**'s offer withdrawn from #{order.Id}.")
			: L.T($"**{crafter.Name}** propose de faire la #{order.Id} ({order.Quantity}× {order.Item}). {order.RequesterCharacter} est prévenu.",
				$"**{crafter.Name}** offers to make #{order.Id} ({order.Quantity}× {order.Item}). {order.RequesterCharacter} was notified."));
	}

	// ---- Upload (jeu) ------------------------------------------------------------------------

	/// <summary>
	/// Commandes, offres et changements d'état décidés en jeu, remontés par le
	/// compagnon. member null = upload partagé : l'identité est « les
	/// personnages de cet envoi ». Les messages Discord sont mis en attente dans
	/// <paramref name="afterCommit"/> : ils ne partent qu'une fois l'envoi enregistré.
	/// </summary>
	public async Task<List<string>> ApplyUploadAsync(UploadContext context, ICollection<Func<Task>> afterCommit, CancellationToken ct = default)
	{
		var payload = context.Payload;
		var warnings = new List<string>();
		if (payload.Orders is not { Count: > 0 } && payload.OrderActions is not { Count: > 0 } && payload.Offers is not { Count: > 0 })
		{
			return warnings;
		}
		await ExpireStaleAsync(ct);
		var identity = (await annuaire.UploadIdentityAsync(context, ct)).Characters;
		var changed = false;

		foreach (var uploaded in payload.Orders ?? [])
		{
			if (await db.CraftOrders.AnyAsync(o => o.ClientId == uploaded.ClientId, ct))
			{
				continue; // déjà synchronisée
			}
			if (!identity.Contains(uploaded.Requester))
			{
				warnings.Add(L.T($"Commande de {uploaded.Requester} ignorée : personnage non lié à ce compte.", $"Order from {uploaded.Requester} ignored: character not linked to this account."));
				continue;
			}
			var order = NewOrder(uploaded.Requester, string.IsNullOrWhiteSpace(uploaded.Crafter) ? null : uploaded.Crafter,
				uploaded.Item, uploaded.Quantity, uploaded.Note, uploaded.RecipeId ?? await FindRecipeIdAsync(uploaded.Item, ct),
				DataSource.Addon, uploaded.ClientId);
			db.CraftOrders.Add(order);
			changed = true;
			await db.SaveChangesAsync(ct);
			afterCommit.Add(() => AnnounceAsync(order, CancellationToken.None));
		}

		foreach (var offer in payload.Offers ?? [])
		{
			var order = await FindOrderAsync(offer.OrderId, offer.OrderClientId, ct);
			if (order is null)
			{
				continue;
			}
			if (!identity.Contains(offer.Crafter))
			{
				warnings.Add(L.T($"Offre de {offer.Crafter} ignorée : personnage non lié à cet envoi.", $"Offer from {offer.Crafter} ignored: character not part of this upload."));
				continue;
			}
			var known = order.Offers.SingleOrDefault(f => f.Crafter == offer.Crafter);
			if (known is not null && known.Withdrawn == offer.Withdrawn)
			{
				continue; // rien de neuf
			}
			if (ApplyOffer(order, offer.Crafter, offer.Note, offer.Withdrawn) is null)
			{
				changed = true;
				await db.SaveChangesAsync(ct);
				if (!offer.Withdrawn)
				{
					var made = order.Offers.Single(f => f.Crafter == offer.Crafter);
					afterCommit.Add(async () => await notifier.NotifyOfferAsync(order, made,
						await FindDiscordIdAsync(order.RequesterCharacter, CancellationToken.None), GuildLanguage));
				}
			}
		}

		foreach (var action in payload.OrderActions ?? [])
		{
			var order = await FindOrderAsync(action.ServerId, action.ClientId, ct);
			if (order is null || !Enum.TryParse<CraftOrderStatus>(action.Status, true, out var newStatus) || newStatus == order.Status)
			{
				continue;
			}
			if (Refusal(order, newStatus, identity, action.Crafter) is { } refusal)
			{
				// Une action déjà dépassée (commande finie entre-temps) n'est pas une erreur à signaler.
				if (IsActive(order.Status))
				{
					warnings.Add(refusal);
				}
				continue;
			}
			var byRequester = identity.Contains(order.RequesterCharacter) &&
				!(order.CrafterCharacter is not null && identity.Contains(order.CrafterCharacter) && newStatus != CraftOrderStatus.Done);
			ApplyTransition(order, newStatus, action.Crafter, byRequester, afterCommit);
			changed = true;
		}

		if (changed)
		{
			await dataVersion.SaveAndBumpAsync(ct);
		}
		return warnings;
	}

	private static CraftOrder NewOrder(string requester, string? crafter, string item, int quantity, string? note,
		int? recipeId, DataSource source, string? clientId) => new()
		{
			ClientId = clientId,
			RequesterCharacter = requester,
			CrafterCharacter = crafter,
			Item = Text.Clip(item, 100) ?? "",
			Quantity = Math.Clamp(quantity, 1, 1000),
			Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
			RecipeId = recipeId,
			Status = CraftOrderStatus.Open,
			CreatedUtc = DateTime.UtcNow,
			UpdatedUtc = DateTime.UtcNow,
			// Seule une demande ouverte à tous expire ; une commande directe attend son artisan.
			ExpiresUtc = crafter is null ? DateTime.UtcNow.Add(OpenLifetime) : null,
			Source = source,
		};

	/// <summary>Pose ou retire une offre ; null si c'est fait, sinon la raison du refus.</summary>
	private string? ApplyOffer(CraftOrder order, string crafter, string? note, bool withdraw)
	{
		if (order.Status != CraftOrderStatus.Open)
		{
			return L.T($"La #{order.Id} n'attend plus d'offre (« {Label(order.Status, L)} »).", $"#{order.Id} no longer takes offers (\"{Label(order.Status, L)}\").");
		}
		if (crafter == order.RequesterCharacter)
		{
			return L.T("On ne fait pas d'offre sur sa propre demande.", "You can't offer on your own request.");
		}
		var existing = order.Offers.SingleOrDefault(f => f.Crafter == crafter);
		if (withdraw)
		{
			if (existing is null || existing.Withdrawn)
			{
				return L.T("Aucune offre à retirer.", "No offer to withdraw.");
			}
			existing.Withdrawn = true;
			existing.UpdatedUtc = DateTime.UtcNow;
			return null;
		}
		if (existing is null)
		{
			existing = new CraftOffer { Crafter = crafter, CreatedUtc = DateTime.UtcNow };
			order.Offers.Add(existing);
		}
		existing.Withdrawn = false;
		existing.Note = Text.Clip(note, 120) ?? existing.Note;
		existing.UpdatedUtc = DateTime.UtcNow;
		return null;
	}

	// Nouvelle commande : l'artisan désigné en message privé, sinon ceux qui connaissent la recette ; plus le salon des commandes.
	private async Task AnnounceAsync(CraftOrder order, CancellationToken ct)
	{
		if (order.CrafterCharacter is not null)
		{
			await notifier.NotifyNewOrderAsync(order, await FindDiscordIdAsync(order.CrafterCharacter, ct), guildContext.Guild?.OrdersChannelId, GuildLanguage);
			return;
		}
		await notifier.NotifyNewOrderAsync(order, null, guildContext.Guild?.OrdersChannelId, GuildLanguage);
		if (order.RecipeId is { } recipeId)
		{
			var crafters = await annuaire.DiscordIdsKnowingRecipeAsync(recipeId, order.RequesterCharacter, 10, ct);
			if (crafters.Count > 0)
			{
				await notifier.NotifyOpenRequestAsync(order, crafters, GuildLanguage);
			}
		}
	}

	private async Task<CraftOrder?> FindOrderAsync(int? serverId, string? clientId, CancellationToken ct) =>
		serverId is not null
			? await db.CraftOrders.Include(o => o.Offers).SingleOrDefaultAsync(o => o.Id == serverId, ct)
			: clientId is null ? null
			: db.CraftOrders.Local.FirstOrDefault(o => o.ClientId == clientId) ??
				await db.CraftOrders.Include(o => o.Offers).SingleOrDefaultAsync(o => o.ClientId == clientId, ct);

	/// <summary>Recette du catalogue ou de l'annuaire portant exactement ce nom.</summary>
	private async Task<int?> FindRecipeIdAsync(string item, CancellationToken ct)
	{
		var name = item.Trim().ToLower();
		var fromCatalog = await db.CatalogRecipes.Where(r => r.Name.ToLower() == name).Select(r => (int?)r.SpellId).FirstOrDefaultAsync(ct);
		return fromCatalog ?? await annuaire.RecipeIdByNameAsync(item, ct);
	}

	// ---- Lecture ------------------------------------------------------------------------------

	/// <summary>
	/// Commandes actives, plus l'historique récent ; offres comprises. Lecture
	/// sans écriture : une demande périmée est exportée « expirée » sans être
	/// modifiée (la prochaine écriture l'enregistrera).
	/// </summary>
	public async Task<List<ExportOrderDto>> BuildExportAsync(CancellationToken ct = default)
	{
		var now = DateTime.UtcNow;
		var historyThreshold = now.AddDays(-7);
		var orders = await db.CraftOrders
			.AsNoTracking()
			.Include(o => o.Offers)
			.Where(o => Active.Contains(o.Status) || o.UpdatedUtc >= historyThreshold)
			.OrderBy(o => o.Id)
			.ToListAsync(ct);
		return orders.Select(o => new ExportOrderDto(
			o.Id, o.ClientId, o.RequesterCharacter, o.CrafterCharacter, o.Item, o.Quantity, o.Note,
			EffectiveStatus(o, now).ToString().ToLower(), o.CreatedUtc.ToString("yyyy-MM-dd HH:mm"), o.RecipeId,
			o.ExpiresUtc?.ToString("yyyy-MM-dd HH:mm"),
			o.Offers.Where(f => !f.Withdrawn).OrderBy(f => f.CreatedUtc)
				.Select(f => new ExportOfferDto(f.Crafter, f.Note, f.CreatedUtc.ToString("yyyy-MM-dd HH:mm"))).ToList()))
			.ToList();
	}

	/// <summary>État à afficher : une demande ouverte dont le délai est passé est expirée, même si ce n'est pas encore écrit.</summary>
	public static CraftOrderStatus EffectiveStatus(CraftOrder order, DateTime now) =>
		order.Status == CraftOrderStatus.Open && order.ExpiresUtc is { } expires && expires < now ? CraftOrderStatus.Expired : order.Status;

	/// <summary>Recettes connues des personnages d'un membre Discord (filtre « ce que je peux faire »).</summary>
	public async Task<(HashSet<string> Characters, IReadOnlySet<int> Recipes, IReadOnlySet<string> RecipeNames)> MemberCraftingAsync(string discordId, CancellationToken ct = default)
	{
		var characters = (await annuaire.CharacterNamesOfMemberAsync(discordId, ct)).ToHashSet();
		var recipes = await annuaire.MemberRecipesAsync(discordId, ct);
		return (characters, recipes.SpellIds, recipes.LowerNames);
	}

	/// <summary>Commandes encore en cours (ouvertes, acceptées, prêtes, envoyées), offres comprises, sans les périmées.</summary>
	public async Task<List<CraftOrder>> ActiveAsync(CancellationToken ct = default)
	{
		var now = DateTime.UtcNow;
		var orders = await db.CraftOrders.AsNoTracking().Include(o => o.Offers)
			.Where(o => Active.Contains(o.Status))
			.OrderBy(o => o.Id)
			.ToListAsync(ct);
		return orders.Where(o => IsActive(EffectiveStatus(o, now))).ToList();
	}

	/// <summary>Une commande et ses offres, état périmé calculé à la lecture.</summary>
	public async Task<CraftOrder?> GetAsync(int id, CancellationToken ct = default)
	{
		var order = await db.CraftOrders.AsNoTracking().Include(o => o.Offers).SingleOrDefaultAsync(o => o.Id == id, ct);
		if (order is not null)
		{
			order.Status = EffectiveStatus(order, DateTime.UtcNow);
		}
		return order;
	}

	/// <summary>Personnages liés à un membre Discord (suggestions « mes commandes » en premier).</summary>
	public Task<IReadOnlyList<string>> MemberCharactersAsync(string discordId, CancellationToken ct = default) =>
		annuaire.CharacterNamesOfMemberAsync(discordId, ct);

	/// <summary>Offres en cours sur une commande (suggestions sur Discord).</summary>
	public async Task<List<CraftOffer>> OpenOffersAsync(int orderId, CancellationToken ct = default) =>
		await db.CraftOffers.AsNoTracking()
			.Where(f => f.OrderId == orderId && !f.Withdrawn)
			.OrderBy(f => f.CreatedUtc)
			.Take(25)
			.ToListAsync(ct);

	public static string Label(CraftOrderStatus status, BotLocalizer l)
	{
		return status switch
		{
			CraftOrderStatus.Open => l.T("ouverte", "open"),
			CraftOrderStatus.Accepted => l.T("acceptée", "accepted"),
			CraftOrderStatus.Ready => l.T("prête à échanger", "ready to trade"),
			CraftOrderStatus.Mailed => l.T("envoyée par courrier", "mailed"),
			CraftOrderStatus.Done => l.T("reçue", "received"),
			CraftOrderStatus.Cancelled => l.T("annulée", "cancelled"),
			CraftOrderStatus.Expired => l.T("expirée", "expired"),
			_ => status.ToString(),
		};
	}

	private Task<string?> FindDiscordIdAsync(string? characterName, CancellationToken ct) =>
		annuaire.DiscordIdOfCharacterAsync(characterName, ct);
}
