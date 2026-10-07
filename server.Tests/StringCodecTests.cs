using GuildProfessions.Server.Contracts;
using GuildProfessions.Server.Services;
using Xunit;

namespace GuildProfessions.Server.Tests;

public sealed class StringCodecTests
{
	[Fact]
	public void EncodeDecode_Should_RoundTripUploadPayload()
	{
		var payload = new UploadPayload(
			[
				new UploadCharacter("Elaria", "MAGE", "Human", 1, 3, 60,
				[
					new UploadProfession("Couture", 300, 300, "2026-10-07 21:12",
						[new UploadRecipe(18560, "Étoffe lunaire")]),
				]),
			],
			Orders: [new UploadOrder("Elaria-1-a", "Elaria", "Thorgal", "Heaume", 1, null, null)],
			OrderActions: [new UploadOrderAction(17, null, "accepted")]);

		var encoded = StringCodec.Encode(payload);
		var decoded = StringCodec.Decode<UploadPayload>(encoded)!;

		Assert.StartsWith("GP1!", encoded);
		Assert.Equal("Étoffe lunaire", decoded.Characters[0].Professions[0].Recipes![0].Name);
		Assert.Equal("Elaria-1-a", decoded.Orders![0].ClientId);
		Assert.Equal(17, decoded.OrderActions![0].ServerId);
	}

	[Fact]
	public void Decode_Should_TolerateWhitespaceAndLineBreaks()
	{
		var encoded = StringCodec.Encode(new UploadPayload([]));
		var mangled = string.Join("\n", encoded.Chunk(20).Select(chunk => new string(chunk) + "  "));

		var decoded = StringCodec.Decode<UploadPayload>(mangled);

		Assert.NotNull(decoded);
	}

	[Fact]
	public void Decode_Should_Fail_WhenPrefixMissing()
	{
		Assert.Throws<FormatException>(() => StringCodec.Decode<UploadPayload>("pasunechaine"));
	}
}
