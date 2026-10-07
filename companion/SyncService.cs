using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuildProfessions.Companion;

public sealed class SyncService : IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly CompanionConfig _config;
	private readonly HttpClient _http;
	private readonly string _statePath;
	private readonly List<FileSystemWatcher> _watchers = [];
	private SyncState _state;
	private DateTime _lastUploadTrigger = DateTime.MinValue;

	public SyncService(CompanionConfig config, string stateDirectory)
	{
		_config = config;
		_http = new HttpClient { BaseAddress = new Uri(config.ApiBaseUrl.TrimEnd('/') + "/") };
		_statePath = Path.Combine(stateDirectory, "companion-state.json");
		_state = File.Exists(_statePath)
			? JsonSerializer.Deserialize<SyncState>(File.ReadAllText(_statePath), JsonOptions) ?? new SyncState()
			: new SyncState();
	}

	private string AddonDataPath => Path.Combine(_config.WowPath, "Interface", "AddOns", "GuildProfessions", "Data.lua");

	private IEnumerable<string> SavedVariablesFiles()
	{
		var accountRoot = Path.Combine(_config.WowPath, "WTF", "Account");
		if (!Directory.Exists(accountRoot))
		{
			yield break;
		}
		foreach (var accountDir in Directory.EnumerateDirectories(accountRoot))
		{
			var file = Path.Combine(accountDir, "SavedVariables", "GuildProfessions.lua");
			if (File.Exists(file))
			{
				yield return file;
			}
		}
	}

	// ---- Down-sync : serveur -> Data.lua ------------------------------------

	public async Task DownSyncAsync(CancellationToken ct)
	{
		if (string.IsNullOrEmpty(_config.GuildToken))
		{
			Log("Down-sync ignoré : guildToken absent de config.json.");
			return;
		}
		try
		{
			using var request = new HttpRequestMessage(HttpMethod.Get, "api/v1/export");
			request.Headers.Add("X-Guild-Token", _config.GuildToken);
			using var response = await _http.SendAsync(request, ct);
			response.EnsureSuccessStatusCode();
			var export = await response.Content.ReadFromJsonAsync<ExportDto>(JsonOptions, ct)
				?? throw new InvalidOperationException("Export vide.");

			if (export.Version == _state.LastVersion && File.Exists(AddonDataPath))
			{
				Log($"Down-sync : version {export.Version} inchangée, rien à écrire.");
				return;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(AddonDataPath)!);
			await File.WriteAllTextAsync(AddonDataPath, DataLuaWriter.Build(export), ct);
			_state = _state with { LastVersion = export.Version };
			SaveState();
			Log($"Down-sync : Data.lua écrit (version {export.Version}, {export.Characters.Count} personnages). /reload en jeu pour charger.");
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			Log($"Down-sync en échec : {exception.Message}");
		}
	}

	// ---- Up-sync : SavedVariables -> serveur -----------------------------------

	public async Task UpSyncAsync(string reason, CancellationToken ct)
	{
		if (string.IsNullOrEmpty(_config.UploadToken) && string.IsNullOrEmpty(_config.GuildToken))
		{
			Log("Up-sync ignoré : aucun token (guildToken ou uploadToken) dans config.json.");
			return;
		}
		foreach (var file in SavedVariablesFiles())
		{
			try
			{
				var content = await ReadWithRetryAsync(file, ct);
				var payload = SavedVariablesReader.BuildPayload(content);
				if (payload is null)
				{
					continue;
				}

				var json = JsonSerializer.Serialize(payload, JsonOptions);
				var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
				if (_state.UploadHashes.GetValueOrDefault(file) == hash)
				{
					continue;
				}

				using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/upload");
				if (!string.IsNullOrEmpty(_config.UploadToken))
				{
					request.Headers.Add("X-Upload-Token", _config.UploadToken);
				}
				else
				{
					request.Headers.Add("X-Guild-Token", _config.GuildToken);
				}
				request.Content = new StringContent(json, Encoding.UTF8, "application/json");
				using var response = await _http.SendAsync(request, ct);
				response.EnsureSuccessStatusCode();

				_state.UploadHashes[file] = hash;
				SaveState();
				Log($"Up-sync ({reason}) : {payload.Characters.Count} personnage(s) envoyés depuis {Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file)))}.");
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				Log($"Up-sync en échec pour {file} : {exception.Message}");
			}
		}
	}

	private static async Task<string> ReadWithRetryAsync(string path, CancellationToken ct)
	{
		// WoW peut tenir le fichier quelques instants à la déconnexion.
		for (var attempt = 1; ; attempt++)
		{
			try
			{
				return await File.ReadAllTextAsync(path, ct);
			}
			catch (IOException) when (attempt < 5)
			{
				await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
			}
		}
	}

	// ---- Boucle principale -------------------------------------------------------

	public async Task RunAsync(CancellationToken ct)
	{
		StartWatchers(ct);
		await UpSyncAsync("démarrage", ct);
		await DownSyncAsync(ct);

		using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _config.PollMinutes)));
		while (await timer.WaitForNextTickAsync(ct))
		{
			await DownSyncAsync(ct);
			await UpSyncAsync("poll périodique", ct);
		}
	}

	private void StartWatchers(CancellationToken ct)
	{
		var accountRoot = Path.Combine(_config.WowPath, "WTF", "Account");
		if (!Directory.Exists(accountRoot))
		{
			Log($"Dossier {accountRoot} introuvable : up-sync au poll uniquement.");
			return;
		}
		foreach (var accountDir in Directory.EnumerateDirectories(accountRoot))
		{
			var savedVariablesDir = Path.Combine(accountDir, "SavedVariables");
			if (!Directory.Exists(savedVariablesDir))
			{
				continue;
			}
			var watcher = new FileSystemWatcher(savedVariablesDir, "GuildProfessions.lua")
			{
				NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
				EnableRaisingEvents = true,
			};
			FileSystemEventHandler handler = (_, _) => OnSavedVariablesChanged(ct);
			watcher.Changed += handler;
			watcher.Created += handler;
			watcher.Renamed += (_, _) => OnSavedVariablesChanged(ct);
			_watchers.Add(watcher);
		}
		Log($"{_watchers.Count} dossier(s) SavedVariables surveillé(s).");
	}

	private void OnSavedVariablesChanged(CancellationToken ct)
	{
		// Débounce : WoW déclenche plusieurs événements par écriture.
		var now = DateTime.UtcNow;
		if (now - _lastUploadTrigger < TimeSpan.FromSeconds(5))
		{
			return;
		}
		_lastUploadTrigger = now;
		_ = Task.Run(async () =>
		{
			await Task.Delay(TimeSpan.FromSeconds(3), ct);
			await UpSyncAsync("fichier modifié", ct);
		}, ct);
	}

	private void SaveState()
	{
		File.WriteAllText(_statePath, JsonSerializer.Serialize(_state, JsonOptions));
	}

	public static void Log(string message)
	{
		Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");
	}

	public void Dispose()
	{
		foreach (var watcher in _watchers)
		{
			watcher.Dispose();
		}
		_http.Dispose();
	}

	private sealed record SyncState
	{
		public long LastVersion { get; init; }
		public Dictionary<string, string> UploadHashes { get; init; } = [];
	}
}
