using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace GuildProfessions.Server.Services;

/// <summary>Notification Discord (DM) — no-op quand le bot n'est pas connecté.</summary>
public interface ICraftOrderNotifier
{
	Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId);
	Task NotifyStatusChangedAsync(CraftOrder order, string? requesterDiscordId);
}

public sealed class NullCraftOrderNotifier : ICraftOrderNotifier
{
	public Task NotifyNewOrderAsync(CraftOrder order, string? crafterDiscordId) => Task.CompletedTask;
	public Task NotifyStatusChangedAsync(CraftOrder order, string? requesterDiscordId) => Task.CompletedTask;
}

public sealed class CraftOrderService(AppDbContext db, RosterService roster, ICraftOrderNotifier notifier)
{
	// Transitions permises : Open -> Accepted -> Done ; annulation tant que non terminé.
	private static readonly Dictionary<CraftOrderStatus, CraftOrderStatus[]> AllowedTransitions = new()
	{
		[CraftOrderStatus.Open] = [CraftOrderStatus.Accepted, CraftOrderStatus.Done, CraftOrderStatus.Cancelled],
		[CraftOrderStatus.Accepted] = [CraftOrderStatus.Done, CraftOrderStatus.Cancelled],
		[CraftOrderStatus.Done] = [],
		[CraftOrderStatus.Cancelled] = [],
	};

	public async Task<OperationResult> CreateFromDiscordAsync(
		string discordId, string discordName, string crafterCharacter, string item,
		int quantity, string? note, CancellationToken ct = default)
	{
		var member = await roster.GetOrCreateMemberAsync(discordId, discordName, ct);
		var requester = await db.Characters
			.Where(c => c.MemberId == member.Id)
			.OrderBy(c => c.Id)
			.Select(c => c.Name)
			.FirstOrDefaultAsync(ct) ?? discordName;

		var order = new CraftOrder
		{
			RequesterCharacter = requester,
			CrafterCharacter = crafterCharacter,
			Item = item,
			Quantity = Math.Max(1, quantity),
			Note = note,
			Status = CraftOrderStatus.Open,
			CreatedUtc = DateTime.UtcNow,
			UpdatedUtc = DateTime.UtcNow,
			Source = DataSource.Discord,
		};
		db.CraftOrders.Add(order);
		await roster.BumpVersionAndSaveAsync(ct);

		await notifier.NotifyNewOrderAsync(order, await FindDiscordIdAsync(crafterCharacter, ct));
		return new OperationResult(true, $"Commande **#{order.Id}** créée : {order.Quantity}× {item} chez **{crafterCharacter}**.");
	}

	public async Task<OperationResult> UpdateStatusFromDiscordAsync(
		string discordId, int orderId, CraftOrderStatus newStatus, CancellationToken ct = default)
	{
		var order = await db.CraftOrders.SingleOrDefaultAsync(o => o.Id == orderId, ct);
		if (order is null)
		{
			return new OperationResult(false, $"Commande #{orderId} introuvable.");
		}

		var memberCharacters = await MemberCharacterNamesAsync(discordId, ct);
		var isCrafter = memberCharacters.Contains(order.CrafterCharacter);
		var isRequester = memberCharacters.Contains(order.RequesterCharacter) ||
			await db.Members.AnyAsync(m => m.DiscordId == discordId && m.DiscordName == order.RequesterCharacter, ct);

		var allowed = newStatus switch
		{
			CraftOrderStatus.Accepted or CraftOrderStatus.Done => isCrafter,
			CraftOrderStatus.Cancelled => isCrafter || isRequester,
			_ => false,
		};
		if (!allowed)
		{
			return new OperationResult(false, $"Seul l'artisan{(newStatus == CraftOrderStatus.Cancelled ? " ou le demandeur" : "")} peut faire ça sur la #{orderId}.");
		}
		if (!AllowedTransitions[order.Status].Contains(newStatus))
		{
			return new OperationResult(false, $"La commande #{orderId} est déjà « {Label(order.Status)} ».");
		}

		order.Status = newStatus;
		order.UpdatedUtc = DateTime.UtcNow;
		await roster.BumpVersionAndSaveAsync(ct);

		await notifier.NotifyStatusChangedAsync(order, await FindDiscordIdAsync(order.RequesterCharacter, ct));
		return new OperationResult(true, $"Commande **#{order.Id}** ({order.Item}) → {Label(newStatus)}.");
	}

	/// <summary>
	/// Commandes créées ou statuées en jeu, remontées par le compagnon.
	/// member null = upload partagé : l'identité est alors « les personnages
	/// scannés dans ce payload » — on ne peut agir que pour les persos de son
	/// propre compte WoW, puisque ce sont eux qu'on uploade.
	/// </summary>
	public async Task<List<string>> ApplyUploadAsync(Member? member, UploadPayload payload, CancellationToken ct = default)
	{
		var warnings = new List<string>();
		var memberCharacters = member is not null
			? await db.Characters
				.Where(c => c.MemberId == member.Id)
				.Select(c => c.Name)
				.ToHashSetAsync(ct)
			: payload.Characters.Select(c => c.Name).ToHashSet();
		var changed = false;

		foreach (var uploaded in payload.Orders ?? [])
		{
			if (await db.CraftOrders.AnyAsync(o => o.ClientId == uploaded.ClientId, ct))
			{
				continue; // déjà synchronisée
			}
			if (!memberCharacters.Contains(uploaded.Requester))
			{
				warnings.Add($"Commande de {uploaded.Requester} ignorée : personnage non lié à ce compte.");
				continue;
			}
			var order = new CraftOrder
			{
				ClientId = uploaded.ClientId,
				RequesterCharacter = uploaded.Requester,
				CrafterCharacter = uploaded.Crafter,
				Item = uploaded.Item,
				Quantity = Math.Max(1, uploaded.Quantity),
				Note = uploaded.Note,
				Status = CraftOrderStatus.Open,
				CreatedUtc = DateTime.UtcNow,
				UpdatedUtc = DateTime.UtcNow,
				Source = DataSource.Addon,
			};
			db.CraftOrders.Add(order);
			changed = true;
			await db.SaveChangesAsync(ct);
			await notifier.NotifyNewOrderAsync(order, await FindDiscordIdAsync(order.CrafterCharacter, ct));
		}

		foreach (var action in payload.OrderActions ?? [])
		{
			var order = action.ServerId is not null
				? await db.CraftOrders.SingleOrDefaultAsync(o => o.Id == action.ServerId, ct)
				: await db.CraftOrders.SingleOrDefaultAsync(o => o.ClientId == action.ClientId, ct);
			if (order is null)
			{
				continue;
			}
			if (!Enum.TryParse<CraftOrderStatus>(action.Status, true, out var newStatus) ||
				!AllowedTransitions[order.Status].Contains(newStatus))
			{
				continue;
			}
			var isCrafter = memberCharacters.Contains(order.CrafterCharacter);
			var isRequester = memberCharacters.Contains(order.RequesterCharacter);
			var permitted = newStatus switch
			{
				CraftOrderStatus.Accepted or CraftOrderStatus.Done => isCrafter,
				CraftOrderStatus.Cancelled => isCrafter || isRequester,
				_ => false,
			};
			if (!permitted)
			{
				warnings.Add($"Action « {action.Status} » sur #{order.Id} ignorée : personnages non liés à ce compte.");
				continue;
			}
			order.Status = newStatus;
			order.UpdatedUtc = DateTime.UtcNow;
			changed = true;
			await db.SaveChangesAsync(ct);
			await notifier.NotifyStatusChangedAsync(order, await FindDiscordIdAsync(order.RequesterCharacter, ct));
		}

		if (changed)
		{
			await roster.BumpVersionAndSaveAsync(ct);
		}
		return warnings;
	}

	public static string Label(CraftOrderStatus status) => status switch
	{
		CraftOrderStatus.Open => "ouverte",
		CraftOrderStatus.Accepted => "acceptée",
		CraftOrderStatus.Done => "terminée",
		CraftOrderStatus.Cancelled => "annulée",
		_ => status.ToString(),
	};

	private async Task<HashSet<string>> MemberCharacterNamesAsync(string discordId, CancellationToken ct)
	{
		return await db.Characters
			.Where(c => c.Member!.DiscordId == discordId)
			.Select(c => c.Name)
			.ToHashSetAsync(ct);
	}

	private async Task<string?> FindDiscordIdAsync(string characterName, CancellationToken ct)
	{
		return await db.Characters
			.Where(c => c.Name == characterName && c.Member != null)
			.Select(c => c.Member!.DiscordId)
			.SingleOrDefaultAsync(ct);
	}
}
