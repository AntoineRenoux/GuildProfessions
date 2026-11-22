using GuildProfessions.Server.Data;
using GuildProfessions.Server.Discord;
using GuildProfessions.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GuildProfessions.Server.Tests;

/// <summary>
/// /gkeep lier répondait « ce serveur n'utilise pas encore Guildkeep » juste après
/// /gkeep-setup : Discord.Net ouvrait un scope neuf par commande, sans la guilde
/// posée par DiscordBotService. Les commandes doivent tourner dans notre scope.
/// </summary>
public sealed class BotScopeTests
{
	[Fact]
	public void Les_commandes_tournent_dans_le_scope_de_l_interaction() =>
		Assert.False(BotInteractionConfig.Create().AutoServiceScopes);

	[Fact]
	public async Task La_precondition_voit_la_guilde_posee_dans_le_scope()
	{
		var services = new ServiceCollection().AddScoped<GuildContext>().AddScoped<BotLocalizer>().BuildServiceProvider();
		await using var scope = services.CreateAsyncScope();
		var precondition = new RequireRegisteredGuildAttribute();

		var avant = await precondition.CheckRequirementsAsync(null!, null!, scope.ServiceProvider);
		scope.ServiceProvider.GetRequiredService<GuildContext>().Use(new Guild { Id = 1, Name = "Former Hope", Slug = "former-hope", ApiToken = "jeton-de-test", InviteCode = "codetest" });
		var apres = await precondition.CheckRequirementsAsync(null!, null!, scope.ServiceProvider);

		Assert.False(avant.IsSuccess);
		Assert.True(apres.IsSuccess);
	}
}
