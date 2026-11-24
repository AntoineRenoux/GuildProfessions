using System.Runtime.InteropServices;
using System.Security;
using Microsoft.Win32;

namespace GuildProfessions.Companion;

/// <summary>
/// Lancement à l'ouverture de session, en veille (--background) :
/// - Windows : clé HKCU\...\Run (par utilisateur, sans droits admin) ;
/// - macOS : agent de lancement ~/Library/LaunchAgents/com.guildkeep.companion.plist ;
/// - Linux : ~/.config/autostart/guildkeep-companion.desktop (bureaux XDG).
/// Le choix vient de config.json (autoStart) et du menu de l'icône.
/// </summary>
public static class AutoStart
{
	private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "GuildkeepCompanion";
	public const string MacLabel = "com.guildkeep.companion";

	public static string MacAgentPath =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents", MacLabel + ".plist");

	public static string LinuxEntryPath =>
		Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } xdg
			? xdg
			: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "autostart", "guildkeep-companion.desktop");

	public static void Sync(bool enabled)
	{
		if (Environment.ProcessPath is not { } exePath)
		{
			return;
		}
		try
		{
			if (OperatingSystem.IsWindows())
			{
				SyncWindows(enabled, exePath);
			}
			else if (OperatingSystem.IsMacOS())
			{
				SyncFile(enabled, MacAgentPath, LaunchAgentPlist(exePath));
			}
			else if (OperatingSystem.IsLinux())
			{
				SyncFile(enabled, LinuxEntryPath, DesktopEntry(exePath));
			}
		}
		catch (Exception exception)
		{
			SyncService.Log($"Impossible de régler le lancement à l'ouverture de session : {exception.Message}");
		}
	}

	private static void SyncFile(bool enabled, string path, string content)
	{
		if (enabled)
		{
			if (File.Exists(path) && File.ReadAllText(path) == content)
			{
				return;
			}
			Directory.CreateDirectory(Path.GetDirectoryName(path)!);
			File.WriteAllText(path, content);
			SyncService.Log("Lancement à l'ouverture de session activé (en veille jusqu'au lancement de WoW).");
		}
		else if (File.Exists(path))
		{
			File.Delete(path);
			SyncService.Log("Lancement à l'ouverture de session désactivé.");
		}
	}

	/// <summary>Agent macOS : lance le compagnon en veille à l'ouverture de session.</summary>
	public static string LaunchAgentPlist(string exePath) => $"""
		<?xml version="1.0" encoding="UTF-8"?>
		<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
		<plist version="1.0">
		<dict>
			<key>Label</key>
			<string>{MacLabel}</string>
			<key>ProgramArguments</key>
			<array>
				<string>{SecurityElement.Escape(exePath)}</string>
				<string>--background</string>
			</array>
			<key>RunAtLoad</key>
			<true/>
			<key>ProcessType</key>
			<string>Background</string>
		</dict>
		</plist>

		""";

	/// <summary>Entrée XDG : lance le compagnon en veille à l'ouverture de session.</summary>
	public static string DesktopEntry(string exePath) => $"""
		[Desktop Entry]
		Type=Application
		Name=Guildkeep Companion
		Comment=Synchronise ta guilde avec l'addon Guildkeep pendant que tu joues
		Exec="{exePath.Replace("\"", "\\\"")}" --background
		Terminal=false
		X-GNOME-Autostart-enabled=true

		""";

	private static void SyncWindows(bool enabled, string exePath)
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}
		using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
		if (runKey is null)
		{
			return;
		}
		if (enabled)
		{
			var command = $"\"{exePath}\" --background";
			if (!string.Equals(runKey.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
			{
				runKey.SetValue(ValueName, command);
				SyncService.Log("Lancement à l'ouverture de session activé (en veille jusqu'au lancement de WoW).");
			}
		}
		else if (runKey.GetValue(ValueName) is not null)
		{
			runKey.DeleteValue(ValueName, throwOnMissingValue: false);
			SyncService.Log("Lancement à l'ouverture de session désactivé.");
		}
	}

	// Mode console (--console) relancé au démarrage de Windows : masque la console.
	public static void HideConsoleWindow()
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}
		var window = GetConsoleWindow();
		if (window != IntPtr.Zero)
		{
			ShowWindow(window, 0); // SW_HIDE
		}
	}

	[DllImport("kernel32.dll")]
	private static extern IntPtr GetConsoleWindow();

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
