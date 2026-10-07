using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace GuildProfessions.Server.Services;

/// <summary>
/// Chaînes d'import/export partagées avec l'addon : "GP1!" + base64(compression(json)).
/// Miroir de addon/GuildProfessions/Serialize.lua (C_EncodingUtil côté client).
/// Le format de compression du client pouvant varier, le décodage tente
/// zlib, deflate brut et gzip.
/// </summary>
public static class StringCodec
{
	public const string Prefix = "GP1!";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public static string Encode<T>(T value)
	{
		var json = JsonSerializer.SerializeToUtf8Bytes(value, Json);
		using var output = new MemoryStream();
		using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
		{
			zlib.Write(json);
		}
		return Prefix + Convert.ToBase64String(output.ToArray());
	}

	public static T? Decode<T>(string text)
	{
		var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
		if (!cleaned.StartsWith(Prefix, StringComparison.Ordinal))
		{
			throw new FormatException("Préfixe GP1! absent : ce n'est pas une chaîne GuildProfessions.");
		}
		var compressed = Convert.FromBase64String(cleaned[Prefix.Length..]);

		foreach (var decompress in (Func<byte[], Stream>[])
		[
			bytes => new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress),
			bytes => new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress),
			bytes => new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress),
		])
		{
			try
			{
				using var stream = decompress(compressed);
				using var reader = new StreamReader(stream, Encoding.UTF8);
				return JsonSerializer.Deserialize<T>(reader.ReadToEnd(), Json);
			}
			catch (InvalidDataException)
			{
				// Mauvais format de compression : on tente le suivant.
			}
		}
		throw new FormatException("Chaîne invalide ou incomplète (décompression impossible).");
	}
}
