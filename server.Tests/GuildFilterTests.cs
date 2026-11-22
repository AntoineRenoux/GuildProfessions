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

		var result = GuildFilter.Apply(payload, "Les Copains");

		Assert.Equal(["Asdubral"], result.Payload.Characters.Select(c => c.Name));
		Assert.Single(result.Warnings);
		Assert.Equal(["Reroll"], result.Departed);
	}

	[Fact]
	public void Apply_Should_CompareCaseInsensitiveAndTrimmed()
	{
		var payload = new UploadPayload([Character("Asdubral", "  les copains ")]);

		var result = GuildFilter.Apply(payload, "Les Copains");

		Assert.Single(result.Payload.Characters);
		Assert.Empty(result.Warnings);
	}

	[Fact]
	public void Apply_Should_KeepCharacterWithoutGuildInfo()
	{
		var result = GuildFilter.Apply(new UploadPayload([Character("SansInfo", null)]), "Les Copains");

		Assert.Single(result.Payload.Characters); // vieil addon sans info de guilde : toléré
		Assert.Empty(result.Departed);
	}

	[Fact]
	public void Apply_Should_ListDepartedCharacters_FromCompanion_ButNeverAKeptOne()
	{
		var payload = new UploadPayload([Character("Asdubral", "Les Copains")],
			DepartedCharacters: ["Mira", "Asdubral"]);

		var result = GuildFilter.Apply(payload, "Les Copains");

		Assert.Equal(["Mira"], result.Departed);
		Assert.Null(result.Payload.DepartedCharacters);
	}
}
