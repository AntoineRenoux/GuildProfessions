using GuildProfessions.Server.Data;
using GuildProfessions.Server.Shared;
using GuildProfessions.Server.Shared.Modules;

namespace GuildProfessions.Server.Modules.Artisanat.Application;

/// <summary>Carte du tableau de bord : tes demandes, ce que tu dois fabriquer, ce que tu pourrais fabriquer.</summary>
public sealed class ArtisanatHomeCard(CraftOrderService orders, BotLocalizer l) : IHomeContributor
{
	public string Module => "Artisanat";

	public int Order => 30;

	public async Task<HomeCard?> BuildAsync(HomeViewer viewer, CancellationToken ct)
	{
		var (characters, recipes, recipeNames) = await orders.MemberCraftingAsync(viewer.DiscordId, ct);
		var active = await orders.ActiveAsync(ct);
		var requests = active.Count(o => characters.Contains(o.RequesterCharacter));
		var toCraft = active.Count(o => o.CrafterCharacter is { } c && characters.Contains(c) &&
			o.Status is CraftOrderStatus.Accepted or CraftOrderStatus.Ready);
		var canMake = active.Count(o => o.Status == CraftOrderStatus.Open && o.CrafterCharacter is null &&
			!characters.Contains(o.RequesterCharacter) &&
			((o.RecipeId is { } id && recipes.Contains(id)) || recipeNames.Contains(o.Item.ToLowerInvariant())));
		if (characters.Count == 0 && active.Count == 0)
		{
			return null;
		}
		var lines = new List<string>
		{
			l.T($"Tes commandes en cours : **{requests}**", $"Your active orders: **{requests}**"),
			l.T($"À fabriquer pour les autres : **{toCraft}**", $"To craft for others: **{toCraft}**"),
			l.T($"Demandes ouvertes que tu sais faire : **{canMake}**", $"Open requests you can make: **{canMake}**"),
		};
		return new HomeCard(l.T("Commandes de craft", "Craft orders"), lines, l.T("/gkeep commande liste", "/gkeep order list"));
	}
}
