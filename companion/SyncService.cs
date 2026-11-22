using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GuildProfessions.Companion;

public sealed class SyncService : IDisposable
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	private readonly CompanionConfig _config;
	private readonly GuildStore _guilds;
	private readonly HttpClient _http;
	private readonly HashSet<string> _reportedUnregistered = new(StringComparer.OrdinalIgnoreCase);
	private readonly string _statePath;
	private readonly List<FileSystemWatcher> _watchers = [];
	private SyncState _state;
	private DateTime _lastUploadTrigger = DateTime.MinValue;
	// Une synchro à la fois : le poll et l'envoi déclenché par WoW peuvent se croiser.
	private readonly SemaphoreSlim _syncGate = new(1, 1);

	public SyncService(CompanionConfig config, GuildStore guilds, string stateDirectory)
	{
		_config = config;
		_guilds = guilds;
		_http = new HttpClient();
		_statePath = Path.Combine(stateDirectory, "companion-state.json");
		_state = File.Exists(_statePath)
			? JsonSerializer.Deserialize<SyncState>(File.ReadAllText(_statePath), JsonOptions) ?? new SyncState()
			: new SyncState();
	}

	private string AddonDataPath => Path.Combine(_config.WowPath, "Interface", "AddOns", AddonInstaller.AddonFolder, "Data.lua");

	private IEnumerable<string> SavedVariablesFiles()
	{
		var accountRoot = Path.Combine(_config.WowPath, "WTF", "Account");
		if (!Directory.Exists(accountRoot))
		{
			yield break;
		}
		foreach (var accountDir in Directory.EnumerateDirectories(accountRoot))
		{
			var file = Path.Combine(accountDir, "SavedVariables", "Guildkeep.lua");
			if (File.Exists(file))
			{
				yield return file;
			}
		}
	}

	private Uri ApiUri(GuildEntry guild, string relative) =>
		new(new Uri((guild.ApiBaseUrl.Length > 0 ? guild.ApiBaseUrl : _config.ApiBaseUrl).TrimEnd('/') + "/"), relative);

	// ---- Down-sync : serveur -> Data.lua (une section par guilde) ---------------

	public async Task DownSyncAsync(CancellationToken ct)
	{
		var guilds = _guilds.Load();
		if (guilds.Count == 0)
		{
			Log("Aucune guilde : lance le pack téléchargé depuis le lien de ta guilde.");
			return;
		}
		var exports = new List<ExportDto>();
		foreach (var guild in guilds)
		{
			try
			{
				using var request = new HttpRequestMessage(HttpMethod.Get, ApiUri(guild, "api/v1/export"));
				request.Headers.Add("X-Guild-Token", guild.Token);
				using var response = await _http.SendAsync(request, ct);
				if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
				{
					Log($"Guilde « {Display(guild)} » : lien révoqué ou guilde supprimée. Retélécharge son pack depuis le nouveau lien.");
					continue;
				}
				response.EnsureSuccessStatusCode();
				var export = await response.Content.ReadFromJsonAsync<ExportDto>(JsonOptions, ct)
					?? throw new InvalidOperationException("Export vide.");
				if (export.GuildName is { Length: > 0 } name)
				{
					_guilds.SetName(guild.Token, name);
				}
				exports.Add(export);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				// Une guilde injoignable ne doit pas effacer sa section : on réessaie au prochain cycle.
				Log($"Down-sync en échec pour « {Display(guild)} » : {exception.Message}");
				return;
			}
		}

		var versions = exports.ToDictionary(e => e.GuildName ?? "", e => e.Version);
		// Le contenu du fichier fait foi, pas la mémoire du compagnon : une mise à jour
		// de l'addon (CurseForge, zip) remet un Data.lua vide qu'il faut réécrire.
		if (DataLuaWriter.IsCurrent(AddonDataPath, versions))
		{
			Log("Down-sync : rien de nouveau.");
			return;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(AddonDataPath)!);
		await File.WriteAllTextAsync(AddonDataPath, DataLuaWriter.Build(exports), ct);
		_state = _state with { GuildVersions = versions };
		SaveState();
		Log($"Down-sync : Data.lua écrit ({string.Join(", ", exports.Select(e => $"{e.GuildName} : {e.Characters.Count} personnages"))}). /reload en jeu pour charger.");
	}

	private static string Display(GuildEntry guild) => guild.Name.Length > 0 ? guild.Name : "guilde sans nom";

	// ---- Up-sync : SavedVariables -> serveur, guilde par guilde -------------------

	/// <summary>Envoie les SavedVariables modifiées ; true si au moins un envoi a eu lieu.</summary>
	public async Task<bool> UpSyncAsync(string reason, CancellationToken ct)
	{
		var sent = false;
		var guilds = _guilds.Load();
		if (guilds.Count == 0)
		{
			return false;
		}
		foreach (var file in SavedVariablesFiles())
		{
			UploadPayload? payload;
			try
			{
				payload = SavedVariablesReader.BuildPayload(await ReadWithRetryAsync(file, ct));
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				Log($"Lecture de {file} impossible : {exception.Message}");
				continue;
			}
			if (payload is null)
			{
				continue;
			}

			var uploads = UploadSplitter.Split(payload, guilds, out var unregistered);
			foreach (var name in unregistered.Where(_reportedUnregistered.Add))
			{
				Log($"Personnages de la guilde « {name} » non envoyés : cette guilde n'est pas inscrite sur ce poste " +
					"(son administrateur doit lancer /gkeep-setup, puis lance le pack depuis son lien).");
			}
			foreach (var upload in uploads)
			{
				sent |= await UploadAsync(file, upload, reason, ct);
			}
		}
		return sent;
	}

	private async Task<bool> UploadAsync(string file, UploadSplitter.GuildUpload upload, string reason, CancellationToken ct)
	{
		var guild = upload.Guild;
		try
		{
			var json = JsonSerializer.Serialize(upload.Payload, JsonOptions);
			var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
			var stateKey = $"{file}|{guild.Token}";
			if (_state.UploadHashes.GetValueOrDefault(stateKey) == hash)
			{
				return false;
			}

			using var request = new HttpRequestMessage(HttpMethod.Post, ApiUri(guild, "api/v1/upload"));
			if (guild.UploadToken.Length > 0)
			{
				request.Headers.Add("X-Upload-Token", guild.UploadToken);
			}
			else
			{
				request.Headers.Add("X-Guild-Token", guild.Token);
			}
			request.Content = new StringContent(json, Encoding.UTF8, "application/json");
			using var response = await _http.SendAsync(request, ct);
			response.EnsureSuccessStatusCode();
			var result = await response.Content.ReadFromJsonAsync<UploadResultDto>(JsonOptions, ct);

			_state.UploadHashes[stateKey] = hash;
			SaveState();
			Log($"Up-sync ({reason}) vers « {Display(guild)} » : {upload.Payload.Characters.Count} personnage(s).");
			foreach (var warning in result?.Warnings ?? [])
			{
				Log($"  ⚠ {warning}");
			}
			return true;
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			Log($"Up-sync en échec vers « {Display(guild)} » : {exception.Message}");
			return false;
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
		await SyncAsync("démarrage", alwaysDown: true, ct);

		using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, _config.PollMinutes)));
		while (await timer.WaitForNextTickAsync(ct))
		{
			await SyncAsync("poll périodique", alwaysDown: true, ct);
		}
	}

	/// <summary>
	/// Envoie d'abord ce que l'addon a relevé, puis rapatrie les données de la guilde :
	/// juste après un envoi, le Data.lua reflète déjà ce qui vient d'être relevé
	/// (au lieu d'attendre le poll suivant, jusqu'à 10 min plus tard).
	/// </summary>
	private async Task SyncAsync(string reason, bool alwaysDown, CancellationToken ct)
	{
		await _syncGate.WaitAsync(ct);
		try
		{
			var sent = await UpSyncAsync(reason, ct);
			if (sent || alwaysDown)
			{
				await DownSyncAsync(ct);
			}
		}
		finally
		{
			_syncGate.Release();
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
			var watcher = new FileSystemWatcher(savedVariablesDir, "Guildkeep.lua")
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
			await SyncAsync("fichier modifié", alwaysDown: false, ct);
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
		/// <summary>Version de l'export de chaque guilde écrite dans Data.lua.</summary>
		public Dictionary<string, long> GuildVersions { get; init; } = [];
		/// <summary>Empreinte du dernier envoi, par (fichier SavedVariables, guilde).</summary>
		public Dictionary<string, string> UploadHashes { get; init; } = [];
	}

	private sealed record UploadResultDto(bool Applied, long Version, List<string>? Warnings);
}
