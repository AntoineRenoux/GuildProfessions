namespace GuildProfessions.Companion;

/// <summary>
/// Journal du compagnon. Sans console (icône de notification), il est écrit
/// dans un fichier, ouvert depuis le menu de l'icône : companion.log dans le
/// dossier du compagnon, l'ancien gardé en companion.old.log au-delà de 512 Ko.
/// En mode console (--console), il est aussi affiché à l'écran.
/// </summary>
public static class CompanionLog
{
	public const long MaxBytes = 512 * 1024;
	private static readonly object Gate = new();

	public static string Directory { get; set; } = GuildStore.DefaultDirectory;

	public static string FilePath => Path.Combine(Directory, "companion.log");

	public static bool ToConsole { get; set; } = true;

	public static void Write(string message)
	{
		var line = $"[{DateTime.Now:HH:mm:ss}] {message}";
		if (ToConsole)
		{
			Console.WriteLine(line);
		}
		lock (Gate)
		{
			try
			{
				System.IO.Directory.CreateDirectory(Directory);
				Rotate(FilePath, MaxBytes);
				File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}");
			}
			catch (IOException)
			{
				// Journal indisponible (disque plein, droits) : le compagnon continue sans.
			}
			catch (UnauthorizedAccessException)
			{
			}
		}
	}

	/// <summary>Au-delà de maxBytes, le journal devient l'ancien journal et repart à zéro.</summary>
	public static void Rotate(string path, long maxBytes)
	{
		var file = new FileInfo(path);
		if (!file.Exists || file.Length < maxBytes)
		{
			return;
		}
		var old = Path.Combine(file.DirectoryName!, Path.GetFileNameWithoutExtension(path) + ".old" + file.Extension);
		File.Move(path, old, overwrite: true);
	}
}
