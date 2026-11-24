using Avalonia;

namespace GuildProfessions.Companion;

/// <summary>
/// Point d'entrée. Par défaut, le compagnon vit dans une icône de notification,
/// sans console. --console garde l'ancien mode texte (diagnostic, Linux sans
/// bureau) ; c'est aussi le repli quand l'icône ne peut pas être affichée.
/// --background : lancé à l'ouverture de session, rien à faire avant WoW.
/// </summary>
public static class Program
{
	[STAThread]
	public static int Main(string[] args)
	{
		var console = args.Contains("--console");
		using var engine = new CompanionEngine(args);
		if (engine.PrepareAsync().GetAwaiter().GetResult() is { } exitCode)
		{
			return exitCode;
		}
		if (!console)
		{
			try
			{
				AppBuilder.Configure(() => new TrayApp(engine))
					.UsePlatformDetect()
					.With(new MacOSPlatformOptions { ShowInDock = false })
					.StartWithClassicDesktopLifetime(args, Avalonia.Controls.ShutdownMode.OnExplicitShutdown);
				return 0;
			}
			catch (Exception exception)
			{
				// Pas de bureau (serveur Linux, session SSH) : on continue en mode texte.
				SyncService.Log($"Icône de notification indisponible ({exception.Message}) : mode console.");
			}
		}
		return RunConsole(engine);
	}

	private static int RunConsole(CompanionEngine engine)
	{
		if (engine.Background)
		{
			AutoStart.HideConsoleWindow();
		}
		using var cancellation = new CancellationTokenSource();
		Console.CancelKeyPress += (_, eventArgs) =>
		{
			eventArgs.Cancel = true;
			cancellation.Cancel();
			SyncService.Log("Arrêt demandé...");
		};
		try
		{
			engine.RunAsync(cancellation.Token).GetAwaiter().GetResult();
		}
		catch (OperationCanceledException)
		{
			// Arrêt normal via Ctrl+C.
		}
		SyncService.Log("Compagnon arrêté.");
		return 0;
	}
}
