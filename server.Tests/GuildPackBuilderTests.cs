using System.IO.Compression;
using System.Text;
using System.Text.Json;
using GuildProfessions.Server.Data;
using GuildProfessions.Server.Services;
using Xunit;

namespace GuildProfessions.Server.Tests;

public sealed class GuildPackBuilderTests
{
	private static byte[] Template()
	{
		using var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			void Add(string name, string content)
			{
				using var writer = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8);
				writer.Write(content);
			}
			Add("Guildkeep/config.json", "{\"apiBaseUrl\":\"x\"}");
			Add("Guildkeep/Guildkeep", "binaire");
			Add("Guildkeep/docs/config.json", "ne pas toucher");
		}
		return stream.ToArray();
	}

	private static string Read(byte[] zipBytes, string name)
	{
		using var zip = new ZipArchive(new MemoryStream(zipBytes));
		using var reader = new StreamReader(zip.GetEntry(name)!.Open());
		return reader.ReadToEnd();
	}

	[Fact]
	public void Pack_Should_CarryTheGuildsConfig_AndKeepEverythingElse()
	{
		var guild = new Guild { Name = "FORMER HOPE", Slug = "former-hope", ApiToken = "secret", InviteCode = "abc" };

		var pack = GuildPackBuilder.WithGuildConfig(Template(), GuildPackBuilder.GuildConfigJson(guild, "https://api.example"));

		using var config = JsonDocument.Parse(Read(pack, "Guildkeep/config.json"));
		Assert.Equal("secret", config.RootElement.GetProperty("guildToken").GetString());
		Assert.Equal("FORMER HOPE", config.RootElement.GetProperty("guildName").GetString());
		Assert.Equal("https://api.example", config.RootElement.GetProperty("apiBaseUrl").GetString());
		Assert.True(config.RootElement.GetProperty("installAddon").GetBoolean());
		Assert.Equal("binaire", Read(pack, "Guildkeep/Guildkeep"));
		Assert.Equal("ne pas toucher", Read(pack, "Guildkeep/docs/config.json"));
	}
}
