using System.Diagnostics;
using System.Text;
using Microsoft.Win32;

namespace GuildProfessions.Companion;

/// <summary>
/// Localisation du dossier de branche WoW (celui qui contient Interface/ et
/// WTF/), en cascade — du plus fiable au plus spéculatif :
/// 1. Registre Windows (chemin écrit par l'installateur Blizzard)
/// 2. product.db de l'agent Battle.net (tous les produits installés)
/// 3. Processus Wow*.exe en cours d'exécution
/// 4. Chemins conventionnels + balayage des lecteurs fixes
/// 5. Préfixes Steam/Proton (Linux)
/// Dans chaque installation trouvée, la branche Forever est choisie par sa
/// version (FindForeverBranch), pas par le nom de son dossier.
/// Un échec total → wowPath manuel dans config.json.
/// </summary>
public static class WowLocator
{
	// Repli quand le launcher n'a pas laissé de .build.info : noms de dossiers
	// propres à Forever uniquement. Classic Era (_classic_era_) et Retail sont
	// volontairement absents : y installer l'addon serait pire qu'échouer.
	private static readonly string[] ForeverFolderNames = ["_forever_", "_classic_beta_", "_forever_beta_"];

	private static readonly string[] TestProductMarkers = ["beta", "ptr", "alpha", "test"];

	public static (string Path, string Source)? Detect()
	{
		foreach (var (candidate, source) in CandidateBases())
		{
			// Le candidat peut être une base (contenant les branches) ou un dossier
			// de branche (registre, exe en cours) : on remonte alors à sa base pour
			// choisir la branche Forever, qui n'est pas forcément celle-là.
			var baseDir = IsBranchDirectory(candidate) && Path.GetDirectoryName(candidate) is { } parent
				? parent
				: candidate;
			if (FindForeverBranch(baseDir) is { } branch)
			{
				return (branch, source);
			}
		}
		return null;
	}

	/// <summary>
	/// Dossier de la branche WoW: Forever dans une installation WoW. Le nom du
	/// dossier n'est pas fiable (« _classic_beta_ » pendant la bêta, inconnu à
	/// la sortie) : on lit le .build.info du launcher (version + code produit
	/// de chaque branche) et le .flavor.info de chaque dossier (code produit).
	/// </summary>
	public static string? FindForeverBranch(string baseDir)
	{
		if (!Directory.Exists(baseDir))
		{
			return null;
		}

		var products = ForeverProducts(Path.Combine(baseDir, ".build.info"));
		if (products.Count > 0)
		{
			var folders = Directory.EnumerateDirectories(baseDir, "_*_")
				.Select(dir => (Dir: dir, Product: ReadFlavor(dir)))
				.Where(f => f.Product is not null)
				.ToList();
			foreach (var product in products)
			{
				var match = folders.FirstOrDefault(f => string.Equals(f.Product, product, StringComparison.OrdinalIgnoreCase));
				if (match.Dir is not null)
				{
					return match.Dir;
				}
			}
		}

		foreach (var name in ForeverFolderNames)
		{
			var candidate = Path.Combine(baseDir, name);
			if (IsBranchDirectory(candidate))
			{
				return candidate;
			}
		}
		return null;
	}

	/// <summary>
	/// Codes produit des branches Forever d'après le .build.info : client en
	/// 1.60 ou plus (Classic Era est en 1.15), ou produit nommé « forever ».
	/// La version de sortie passe avant les bêtas et PTR.
	/// </summary>
	public static List<string> ForeverProducts(string buildInfoPath)
	{
		if (!File.Exists(buildInfoPath))
		{
			return [];
		}
		string[] lines;
		try
		{
			lines = File.ReadAllLines(buildInfoPath);
		}
		catch (IOException)
		{
			return [];
		}
		if (lines.Length < 2)
		{
			return [];
		}

		// En-tête : « Branch!STRING:0|Active!DEC:1|...|Version!STRING:0|...|Product!STRING:0 »
		var headers = lines[0].Split('|').Select(h => h.Split('!')[0].Trim()).ToList();
		var versionIndex = headers.IndexOf("Version");
		var productIndex = headers.IndexOf("Product");
		if (versionIndex < 0 || productIndex < 0)
		{
			return [];
		}

		var products = new List<string>();
		foreach (var line in lines.Skip(1))
		{
			var fields = line.Split('|');
			if (fields.Length <= Math.Max(versionIndex, productIndex))
			{
				continue;
			}
			var product = fields[productIndex].Trim();
			if (product.Length > 0 && IsForeverVersion(fields[versionIndex], product) && !products.Contains(product))
			{
				products.Add(product);
			}
		}
		return products
			.OrderBy(p => TestProductMarkers.Any(marker => p.Contains(marker, StringComparison.OrdinalIgnoreCase)))
			.ToList();
	}

	private static bool IsForeverVersion(string version, string product)
	{
		if (product.Contains("forever", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		var parts = version.Trim().Split('.');
		return parts.Length >= 2
			&& int.TryParse(parts[0], out var major) && major == 1
			&& int.TryParse(parts[1], out var minor) && minor >= 60;
	}

	// .flavor.info : « Product Flavor!STRING:0 » puis le code produit.
	private static string? ReadFlavor(string branchDir)
	{
		var path = Path.Combine(branchDir, ".flavor.info");
		try
		{
			return File.Exists(path)
				? File.ReadAllLines(path).Select(l => l.Trim()).LastOrDefault(l => l.Length > 0 && !l.Contains('!'))
				: null;
		}
		catch (IOException)
		{
			return null;
		}
	}

	private static bool IsBranchDirectory(string path) =>
		Directory.Exists(Path.Combine(path, "Interface")) || Directory.Exists(Path.Combine(path, "WTF"));

	private static IEnumerable<(string BaseDir, string Source)> CandidateBases()
	{
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		IEnumerable<(string, string)> Tag(IEnumerable<string> paths, string source)
		{
			foreach (var path in paths)
			{
				var normalized = path.TrimEnd('/', '\\');
				if (normalized.Length > 0 && seen.Add(normalized))
				{
					yield return (normalized, source);
				}
			}
		}

		foreach (var entry in Tag(FromRegistry(), "registre Windows")) yield return entry;
		foreach (var entry in Tag(FromBattleNetProductDb(), "Battle.net product.db")) yield return entry;
		foreach (var entry in Tag(FromRunningProcess(), "processus Wow en cours")) yield return entry;
		foreach (var entry in Tag(ConventionalWindowsPaths(), "chemin conventionnel")) yield return entry;
		foreach (var entry in Tag(FromSteamProton(), "préfixe Steam/Proton")) yield return entry;
	}

	// 1. HKLM\SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft : InstallPath
	private static IEnumerable<string> FromRegistry()
	{
		if (!OperatingSystem.IsWindows())
		{
			yield break;
		}
		string[] keyPaths =
		[
			@"SOFTWARE\WOW6432Node\Blizzard Entertainment\World of Warcraft",
			@"SOFTWARE\Blizzard Entertainment\World of Warcraft",
		];
		foreach (var keyPath in keyPaths)
		{
			string? installPath = null;
			try
			{
				using var key = Registry.LocalMachine.OpenSubKey(keyPath);
				installPath = key?.GetValue("InstallPath") as string;
			}
			catch
			{
				// Accès registre refusé : on passe au candidat suivant.
			}
			if (!string.IsNullOrEmpty(installPath))
			{
				yield return installPath;
				// InstallPath pointe parfois sur une branche : sa base aussi est candidate.
				var parent = Path.GetDirectoryName(installPath.TrimEnd('/', '\\'));
				if (parent is not null)
				{
					yield return parent;
				}
			}
		}
	}

	// 2. C:\ProgramData\Battle.net\Agent\product.db — protobuf, mais les
	// chemins y sont en ASCII lisible : extraction tolérante de chaînes.
	private static IEnumerable<string> FromBattleNetProductDb()
	{
		if (!OperatingSystem.IsWindows())
		{
			return [];
		}
		var productDb = Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
			"Battle.net", "Agent", "product.db");
		try
		{
			return File.Exists(productDb) ? ExtractWowPaths(File.ReadAllBytes(productDb)) : [];
		}
		catch
		{
			return [];
		}
	}

	/// <summary>Extrait d'un blob binaire les chemins ASCII contenant « World of Warcraft ».</summary>
	public static List<string> ExtractWowPaths(byte[] blob)
	{
		var paths = new List<string>();
		var current = new StringBuilder();
		void Flush()
		{
			if (current.Length >= 8)
			{
				var text = current.ToString();
				// Un chemin « X:/... » ou « X:\... » mentionnant WoW.
				if (text.Length > 3 && char.IsLetter(text[0]) && text[1] == ':' && (text[2] == '/' || text[2] == '\\') &&
					text.Contains("World of Warcraft", StringComparison.OrdinalIgnoreCase))
				{
					paths.Add(text.Replace('/', Path.DirectorySeparatorChar));
				}
			}
			current.Clear();
		}
		foreach (var b in blob)
		{
			if (b is >= 0x20 and <= 0x7E)
			{
				current.Append((char)b);
			}
			else
			{
				Flush();
			}
		}
		Flush();
		return paths;
	}

	// 3. Un client WoW en train de tourner donne son propre chemin.
	private static IEnumerable<string> FromRunningProcess()
	{
		string[] processNames = ["Wow", "WowClassic", "Wow-64", "WowT", "WowB"];
		var results = new List<string>();
		foreach (var name in processNames)
		{
			try
			{
				foreach (var process in Process.GetProcessesByName(name))
				{
					try
					{
						var exePath = process.MainModule?.FileName;
						var branchDir = exePath is null ? null : Path.GetDirectoryName(exePath);
						if (branchDir is not null)
						{
							results.Add(branchDir);
						}
					}
					catch
					{
						// Process d'une autre élévation/bitness : illisible, tant pis.
					}
					finally
					{
						process.Dispose();
					}
				}
			}
			catch
			{
				// Énumération indisponible sur cette plateforme.
			}
		}
		return results;
	}

	// 4. Installation par défaut + balayage des lecteurs fixes sur des motifs courants.
	private static IEnumerable<string> ConventionalWindowsPaths()
	{
		if (!OperatingSystem.IsWindows())
		{
			yield break;
		}
		yield return @"C:\Program Files (x86)\World of Warcraft";

		string[] patterns =
		[
			@"World of Warcraft",
			@"Games\World of Warcraft",
			@"Jeux\World of Warcraft",
			@"Blizzard\World of Warcraft",
			@"Program Files (x86)\World of Warcraft",
		];
		foreach (var drive in SafeFixedDrives())
		{
			foreach (var pattern in patterns)
			{
				yield return Path.Combine(drive, pattern);
			}
		}
	}

	private static List<string> SafeFixedDrives()
	{
		var drives = new List<string>();
		try
		{
			foreach (var drive in DriveInfo.GetDrives())
			{
				if (drive.DriveType == DriveType.Fixed && drive.IsReady)
				{
					drives.Add(drive.RootDirectory.FullName);
				}
			}
		}
		catch
		{
			// Peu importe : les autres sources restent.
		}
		return drives;
	}

	// 5. macOS : installation Battle.net standard.
	// 6. Linux : WoW sous Steam/Proton (et Lutris-GE au passage).
	private static IEnumerable<string> FromSteamProton()
	{
		if (OperatingSystem.IsWindows())
		{
			yield break;
		}
		if (OperatingSystem.IsMacOS())
		{
			yield return "/Applications/World of Warcraft";
			yield break;
		}
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string[] roots =
		[
			Path.Combine(home, ".local/share/Steam/steamapps/compatdata"),
			Path.Combine(home, ".steam/steam/steamapps/compatdata"),
			Path.Combine(home, "Games"), // Lutris
		];
		foreach (var root in roots)
		{
			if (!Directory.Exists(root))
			{
				continue;
			}
			IEnumerable<string> prefixes;
			try
			{
				prefixes = Directory.EnumerateDirectories(root);
			}
			catch
			{
				continue;
			}
			foreach (var prefix in prefixes)
			{
				var wowBase = Path.Combine(prefix, "pfx/drive_c/Program Files (x86)/World of Warcraft");
				if (Directory.Exists(wowBase))
				{
					yield return wowBase;
				}
			}
		}
	}
}
