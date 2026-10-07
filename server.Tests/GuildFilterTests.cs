using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Services;
using Xunit;

namespace GuildProfessions.Server.Tests;

public sealed class GuildFilterTests
{
	private static UploadCharacter Character(string name, string? guild) =>
		new(name, null, null, null, null, null, [], guild);

	[Fact]
	public void Apply_Should_RejectCharacterFromAnotherGuild_WithWarning()
	{
		var payload = new UploadPayload([
			Character("Asdubral", "Les Copains"),
			Character("Reroll", "Autre Guilde"),
		]);

		var (filtered, warnings) = GuildFilter.Apply(payload, "Les Copains");

		Assert.Equal(["Asdubral"], filtered.Characters.Select(c => c.Name));
		Assert.Single(warnings);
	}

	[Fact]
	public void Apply_Should_CompareCaseInsensitiveAndTrimmed()
	{
		var payload = new UploadPayload([Character("Asdubral", "  les copains ")]);

		var (filtered, warnings) = GuildFilter.Apply(payload, "Les Copains");

		Assert.Single(filtered.Characters);
		Assert.Empty(warnings);
	}

	[Fact]
	public void Apply_Should_KeepAll_WhenNoGuildConfigured_OrNoGuildInfo()
	{
		var payload = new UploadPayload([Character("SansInfo", null), Character("Autre", "X")]);

		var (noConfig, _) = GuildFilter.Apply(payload, "");
		var (withConfig, _) = GuildFilter.Apply(new UploadPayload([Character("SansInfo", null)]), "Les Copains");

		Assert.Equal(2, noConfig.Characters.Count);
		Assert.Single(withConfig.Characters); // vieil addon sans info de guilde : toléré
	}
}
