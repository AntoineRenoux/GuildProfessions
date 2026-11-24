using GuildProfessions.Companion;
using Xunit;

namespace GuildProfessions.Companion.Tests;

/// <summary>Icône de notification : état affiché, journal en fichier, config à côté de l'app, lancement à l'ouverture de session.</summary>
public sealed class TrayCompanionTests
{
	[Fact]
	public void Status_Should_DescribeSleep_Sync_AndProblems()
	{
		var status = new CompanionStatus();
		status.Set(CompanionState.Sleeping);
		Assert.Equal("En veille : se réveille quand tu lances WoW", status.Summary());

		status.Set(CompanionState.Syncing);
		status.Synced(new DateTime(2026, 11, 24, 21, 5, 0), CompanionState.Syncing);
		Assert.Equal("WoW lancé : synchronisation active · dernière synchro à 21:05", status.Summary());

		status.Fail("lien de « Former Hope » révoqué, retélécharge le pack de la guilde");
		Assert.StartsWith("À régler : lien de « Former Hope » révoqué", status.Summary());
		Assert.True(status.ToolTip().Length <= 120, "infobulle courte (Windows coupe à 127)");

		// Une synchro réussie règle le problème et revient à l'état courant.
		status.Synced(new DateTime(2026, 11, 24, 21, 10, 0), CompanionState.Sleeping);
		Assert.Equal(CompanionState.Sleeping, status.State);
		Assert.Null(status.Problem);
	}

	[Fact]
	public void Status_Should_NotifyChanges()
	{
		var status = new CompanionStatus();
		var changes = 0;
		status.Changed += () => changes++;
		status.Set(CompanionState.Sleeping);
		status.Fail("x");
		status.Synced(DateTime.Now, CompanionState.Syncing);
		Assert.Equal(3, changes);
	}

	[Fact]
	public void Log_Should_RotateBeyondTheLimit()
	{
		var dir = Directory.CreateTempSubdirectory("gk-log-").FullName;
		try
		{
			var path = Path.Combine(dir, "companion.log");
			File.WriteAllText(path, new string('x', 100));
			CompanionLog.Rotate(path, maxBytes: 50);
			Assert.False(File.Exists(path));
			Assert.Equal(100, new FileInfo(Path.Combine(dir, "companion.old.log")).Length);

			File.WriteAllText(path, "court");
			CompanionLog.Rotate(path, maxBytes: 50);
			Assert.True(File.Exists(path), "sous la limite, le journal reste");
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Theory]
	[InlineData("/Users/a/Downloads/Guildkeep/Guildkeep.app/Contents/MacOS/", "/Users/a/Downloads/Guildkeep")]
	[InlineData("/Users/a/Downloads/Guildkeep/", "/Users/a/Downloads/Guildkeep")]
	[InlineData("/opt/guildkeep", "/opt/guildkeep")]
	public void ConfigDirectory_Should_SitNextToTheMacApp(string baseDirectory, string expected)
	{
		if (Path.DirectorySeparatorChar != '/')
		{
			return; // chemins macOS
		}
		Assert.Equal(expected, CompanionEngine.ConfigDirectory(baseDirectory));
	}

	[Fact]
	public void MacLaunchAgent_Should_StartAsleepAtLogin()
	{
		var plist = AutoStart.LaunchAgentPlist("/Users/a/Downloads/Guildkeep/Guildkeep.app/Contents/MacOS/GuildkeepCompanion");
		Assert.Contains("<string>com.guildkeep.companion</string>", plist);
		Assert.Contains("<string>/Users/a/Downloads/Guildkeep/Guildkeep.app/Contents/MacOS/GuildkeepCompanion</string>", plist);
		Assert.Contains("<string>--background</string>", plist);
		Assert.Contains("<key>RunAtLoad</key>", plist);
		Assert.StartsWith("<?xml", plist);
		Assert.Contains("&amp;", AutoStart.LaunchAgentPlist("/Users/a&b/GuildkeepCompanion"));
	}

	[Fact]
	public void LinuxAutostart_Should_StartAsleepAtLogin()
	{
		var entry = AutoStart.DesktopEntry("/home/a/Guildkeep/GuildkeepCompanion");
		Assert.StartsWith("[Desktop Entry]", entry);
		Assert.Contains("Exec=\"/home/a/Guildkeep/GuildkeepCompanion\" --background", entry);
		Assert.Contains("Terminal=false", entry);
	}
}
