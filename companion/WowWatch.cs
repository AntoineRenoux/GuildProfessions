using System.Diagnostics;

namespace GuildProfessions.Companion;

/// <summary>
/// Veille du compagnon : il ne travaille que pendant que WoW tourne. Endormi,
/// il ne fait qu'un coup d'œil sur les processus à intervalle régulier (aucun
/// réseau, aucune surveillance de fichiers). Au lancement du jeu il s'éveille
/// (mise à jour, première synchro), synchronise pendant la partie, puis à la
/// fermeture du jeu fait un dernier envoi et se rendort.
/// </summary>
public sealed class WowWatch(Func<bool> isWowRunning, TimeSpan checkEvery, TimeSpan pollEvery, Func<DateTime>? clock = null)
{
	private readonly Func<DateTime> _now = clock ?? (() => DateTime.UtcNow);

	public static readonly TimeSpan DefaultCheck = TimeSpan.FromSeconds(20);

	public async Task RunAsync(
		Func<CancellationToken, Task> onStarted,
		Func<CancellationToken, Task> onStopped,
		Func<CancellationToken, Task> onPoll,
		CancellationToken ct)
	{
		var running = false;
		var nextPoll = DateTime.MaxValue;
		using var timer = new PeriodicTimer(checkEvery);
		do
		{
			var now = SafeIsRunning();
			if (now && !running)
			{
				running = true;
				await onStarted(ct);
				nextPoll = _now() + pollEvery;
			}
			else if (!now && running)
			{
				running = false;
				nextPoll = DateTime.MaxValue;
				await onStopped(ct);
			}
			else if (running && _now() >= nextPoll)
			{
				await onPoll(ct);
				nextPoll = _now() + pollEvery;
			}
		}
		while (await timer.WaitForNextTickAsync(ct));
	}

	// Une énumération des processus qui échoue ne doit ni réveiller ni endormir le compagnon à tort.
	private bool _last;

	private bool SafeIsRunning()
	{
		try
		{
			_last = isWowRunning();
		}
		catch
		{
			// On garde le dernier état connu.
		}
		return _last;
	}
}

/// <summary>Le client WoW de la branche suivie (ex. _forever_) est-il lancé ?</summary>
public static class WowProcess
{
	// Windows : Wow.exe et ses variantes ; macOS : « World of Warcraft » (le binaire dans l'app).
	private static readonly string[] Names = ["Wow", "WowClassic", "Wow-64", "WowT", "WowB", "World of Warcraft"];

	public static bool IsRunning(string wowPath)
	{
		foreach (var name in Names)
		{
			Process[] processes;
			try
			{
				processes = Process.GetProcessesByName(name);
			}
			catch
			{
				continue;
			}
			try
			{
				foreach (var process in processes)
				{
					string? exePath = null;
					try
					{
						exePath = process.MainModule?.FileName;
					}
					catch
					{
						// Chemin illisible (autre élévation) : on compte ce client.
					}
					if (IsSameBranch(exePath, wowPath))
					{
						return true;
					}
				}
			}
			finally
			{
				foreach (var process in processes)
				{
					process.Dispose();
				}
			}
		}
		return false;
	}

	/// <summary>
	/// Un client dont on ne lit pas le chemin compte ; sinon il doit être dans le
	/// dossier de la branche (un autre WoW lancé, Retail par exemple, ne réveille pas
	/// le compagnon de WoW: Forever).
	/// </summary>
	public static bool IsSameBranch(string? exePath, string wowPath)
	{
		if (string.IsNullOrEmpty(exePath) || string.IsNullOrEmpty(wowPath))
		{
			return true;
		}
		var branch = Path.GetFullPath(wowPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
		return Path.GetFullPath(exePath).StartsWith(branch, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
	}
}
