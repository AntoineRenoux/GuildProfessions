namespace GuildProfessions.Companion;

/// <summary>
/// Verrou « un seul compagnon par poste » : un fichier ouvert en exclusif
/// tant que le compagnon tourne (libéré par le système s'il plante).
/// </summary>
public static class SingleInstance
{
	public static IDisposable? TryAcquire(string directory)
	{
		Directory.CreateDirectory(directory);
		try
		{
			return new FileStream(Path.Combine(directory, "companion.lock"), FileMode.OpenOrCreate,
				FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
		}
		catch (IOException)
		{
			return null;
		}
	}
}
