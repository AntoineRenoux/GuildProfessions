using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;

namespace GuildProfessions.Companion;

/// <summary>
/// Le compagnon côté joueur : une icône dans la zone de notification (Windows),
/// la barre des menus (macOS) ou la barre système (Linux), sans fenêtre ni
/// console. Son menu montre l'état (veille, synchro active, problème à régler)
/// et offre : synchroniser maintenant, ouvrir le journal, lancement à
/// l'ouverture de session, quitter.
/// </summary>
public sealed class TrayApp(CompanionEngine engine) : Application
{
	private readonly CancellationTokenSource _stop = new();
	private TrayIcon? _tray;
	private NativeMenuItem? _status;
	private NativeMenuItem? _autoStart;

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
			BuildTray(desktop);
			_ = Task.Run(() => RunEngineAsync(desktop));
		}
		base.OnFrameworkInitializationCompleted();
	}

	private void BuildTray(IClassicDesktopStyleApplicationLifetime desktop)
	{
		_status = new NativeMenuItem { Header = CompanionStatus.Shared.Summary(), IsEnabled = false };
		var syncNow = new NativeMenuItem { Header = "Synchroniser maintenant" };
		syncNow.Click += (_, _) => _ = Task.Run(() => engine.SyncNowAsync(_stop.Token));
		var openLog = new NativeMenuItem { Header = "Ouvrir le journal" };
		openLog.Click += (_, _) => Open(CompanionLog.FilePath);
		_autoStart = new NativeMenuItem
		{
			Header = "Lancer à l'ouverture de session",
			ToggleType = MenuItemToggleType.CheckBox,
			IsChecked = engine.Config.AutoStart,
		};
		_autoStart.Click += (_, _) =>
		{
			engine.SetAutoStart(!engine.Config.AutoStart);
			_autoStart.IsChecked = engine.Config.AutoStart;
		};
		var quit = new NativeMenuItem { Header = "Quitter Guildkeep" };
		quit.Click += async (_, _) => await QuitAsync(desktop);

		var menu = new NativeMenu();
		menu.Items.Add(_status);
		menu.Items.Add(new NativeMenuItemSeparator());
		menu.Items.Add(syncNow);
		menu.Items.Add(openLog);
		menu.Items.Add(_autoStart);
		menu.Items.Add(new NativeMenuItemSeparator());
		menu.Items.Add(quit);

		_tray = new TrayIcon
		{
			Icon = LoadIcon(),
			ToolTipText = CompanionStatus.Shared.ToolTip(),
			Menu = menu,
			IsVisible = true,
		};
		TrayIcon.SetIcons(this, [_tray]);
		CompanionStatus.Shared.Changed += () => Dispatcher.UIThread.Post(Refresh);
	}

	private void Refresh()
	{
		if (_status is not null)
		{
			_status.Header = CompanionStatus.Shared.Summary();
		}
		if (_tray is not null)
		{
			_tray.ToolTipText = CompanionStatus.Shared.ToolTip();
		}
	}

	private async Task RunEngineAsync(IClassicDesktopStyleApplicationLifetime desktop)
	{
		try
		{
			await engine.RunAsync(_stop.Token);
		}
		catch (OperationCanceledException)
		{
			// Arrêt demandé depuis le menu.
		}
		catch (Exception exception)
		{
			SyncService.Log($"Le compagnon s'est arrêté sur une erreur : {exception}");
			CompanionStatus.Shared.Fail("le compagnon s'est arrêté sur une erreur, voir le journal");
		}
	}

	private async Task QuitAsync(IClassicDesktopStyleApplicationLifetime desktop)
	{
		// En pleine partie : un dernier envoi avant de partir (limité à 15 s).
		if (CompanionStatus.Shared.State == CompanionState.Syncing)
		{
			using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
			try
			{
				await engine.SyncNowAsync(timeout.Token);
			}
			catch (OperationCanceledException)
			{
			}
		}
		SyncService.Log("Compagnon arrêté depuis son icône.");
		_stop.Cancel();
		if (_tray is not null)
		{
			_tray.IsVisible = false;
		}
		desktop.Shutdown();
	}

	private static WindowIcon LoadIcon()
	{
		using var stream = typeof(TrayApp).Assembly.GetManifestResourceStream("GuildProfessions.Companion.Assets.tray-icon.png")
			?? throw new InvalidOperationException("Icône du compagnon absente.");
		return new WindowIcon(stream);
	}

	/// <summary>Ouvre un fichier avec l'application par défaut du système.</summary>
	private static void Open(string path)
	{
		try
		{
			if (!File.Exists(path))
			{
				SyncService.Log("Journal ouvert depuis l'icône.");
			}
			if (OperatingSystem.IsMacOS())
			{
				Process.Start("open", ["-t", path]);
			}
			else if (OperatingSystem.IsLinux())
			{
				Process.Start("xdg-open", [path]);
			}
			else
			{
				Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
			}
		}
		catch (Exception exception)
		{
			SyncService.Log($"Impossible d'ouvrir {path} : {exception.Message}");
		}
	}
}
