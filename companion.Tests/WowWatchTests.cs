using GuildProfessions.Companion;
using Xunit;

namespace GuildProfessions.Companion.Tests;

/// <summary>Veille : le compagnon ne travaille que pendant que WoW tourne.</summary>
public sealed class WowWatchTests
{
	/// <summary>Fait tourner la veille sur une suite d'états de WoW (un par coup d'œil) et note ce qu'elle fait.</summary>
	private static async Task<List<string>> RunAsync(bool[] states, TimeSpan pollEvery, bool throwOnce = false)
	{
		var events = new List<string>();
		var index = 0;
		var thrown = false;
		var clock = new DateTime(2026, 11, 24, 20, 0, 0, DateTimeKind.Utc);
		using var cts = new CancellationTokenSource();
		bool IsRunning()
		{
			if (throwOnce && !thrown && index == 1)
			{
				thrown = true;
				index++;
				throw new InvalidOperationException("énumération impossible");
			}
			if (index >= states.Length)
			{
				cts.Cancel();
				return states[^1];
			}
			clock = clock.AddMinutes(1); // chaque coup d'œil vaut une minute
			return states[index++];
		}
		var watch = new WowWatch(IsRunning, TimeSpan.FromMilliseconds(5), pollEvery, () => clock);
		try
		{
			await watch.RunAsync(
				_ => { events.Add("réveil"); return Task.CompletedTask; },
				_ => { events.Add("veille"); return Task.CompletedTask; },
				_ => { events.Add("poll"); return Task.CompletedTask; },
				cts.Token);
		}
		catch (OperationCanceledException)
		{
		}
		return events;
	}

	[Fact]
	public async Task NoWow_Should_DoNothing()
	{
		var events = await RunAsync([false, false, false, false], TimeSpan.FromMinutes(10));
		Assert.Empty(events);
	}

	[Fact]
	public async Task WowSession_Should_WakeThenSleep_Once()
	{
		var events = await RunAsync([false, true, true, false, false], TimeSpan.FromMinutes(10));
		Assert.Equal(["réveil", "veille"], events);
	}

	[Fact]
	public async Task LongSession_Should_PollAtTheConfiguredInterval()
	{
		// 7 minutes de jeu avec un poll toutes les 3 minutes : deux polls, puis la veille.
		var events = await RunAsync([true, true, true, true, true, true, true, false], TimeSpan.FromMinutes(3));
		Assert.Equal(["réveil", "poll", "poll", "veille"], events);
	}

	[Fact]
	public async Task ProcessListFailure_Should_KeepTheLastKnownState()
	{
		// Le coup d'œil qui échoue pendant la partie ne doit pas endormir le compagnon.
		var events = await RunAsync([true, true, true, false], TimeSpan.FromMinutes(30), throwOnce: true);
		Assert.Equal(["réveil", "veille"], events);
	}

	[Theory]
	[InlineData("/Applications/World of Warcraft/_forever_/World of Warcraft.app/Contents/MacOS/World of Warcraft", "/Applications/World of Warcraft/_forever_", true)]
	[InlineData("/Applications/World of Warcraft/_retail_/World of Warcraft.app/Contents/MacOS/World of Warcraft", "/Applications/World of Warcraft/_forever_", false)]
	[InlineData("/Applications/World of Warcraft/_forever_extra/Wow", "/Applications/World of Warcraft/_forever_", false)]
	[InlineData(null, "/Applications/World of Warcraft/_forever_", true)]
	public void IsSameBranch_Should_OnlyCountTheFollowedClient(string? exePath, string wowPath, bool expected) =>
		Assert.Equal(expected, WowProcess.IsSameBranch(exePath, wowPath));
}
