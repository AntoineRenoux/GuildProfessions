using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace GuildProfessions.Companion;

/// <summary>
/// Démarrage automatique avec Windows : clé HKCU\...\Run (par utilisateur,
/// sans droits admin). Au boot, l'exe est relancé avec --background et masque
/// sa console. Sous Linux, no-op (service systemd utilisateur à la main).
/// </summary>
public static class AutoStart
{
	private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
	private const string ValueName = "GuildProfessionsCompanion";

	public static void Sync(bool enabled)
	{
		if (!OperatingSystem.IsWindows())
		{
			return;
		}
		try
		{
			using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
			if (runKey is null)
			{
				return;
			}
			if (enabled && Environment.ProcessPath is { } exePath)
			{
				var command = $"\"{exePath}\" --background";
				if (!string.Equals(runKey.GetValue(ValueName) as string, command, StringComparison.OrdinalIgnoreCase))
				{
					runKey.SetValue(ValueName, command);
					SyncService.Log("Démarrage automatique avec Windows activé (autoStart: false dans config.json pour le retirer).");
				}
			}
			else if (!enabled && runKey.GetValue(ValueName) is not null)
			{
				runKey.DeleteValue(ValueName, throwOnMissingValue: false);
				SyncService.Log("Démarrage automatique avec Windows désactivé.");
			}
		}
		catch (Exception exception)
		{
			SyncService.Log($"Impossible de régler le démarrage automatique : {exception.Message}");
		}
	}

	// Masque la console quand on est relancé par le démarrage de Windows.
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
