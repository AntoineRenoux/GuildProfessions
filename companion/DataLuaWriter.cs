using System.Globalization;
using System.Text;

namespace GuildProfessions.Companion;

/// <summary>
/// Génère le Data.lua chargé par l'addon à partir de l'export du serveur.
/// Le format doit rester lisible par addon/GuildProfessions/Core.lua.
/// </summary>
public static class DataLuaWriter
{
	public static string Build(ExportDto export)
	{
		var builder = new StringBuilder();
		builder.AppendLine("-- Fichier généré par l'app compagnon GuildProfessions.");
		builder.AppendLine("-- Ne pas éditer : écrasé à chaque synchronisation.");
		builder.AppendLine("GuildProfessions_ServerData = {");
		builder.AppendLine($"\tversion = {export.Version},");
		builder.AppendLine($"\tgeneratedUtc = \"{export.GeneratedUtc:yyyy-MM-dd HH:mm} UTC\",");

		builder.AppendLine("\trecipeNames = {");
		foreach (var (spellId, name) in export.RecipeNames.OrderBy(entry => entry.Key))
		{
			builder.AppendLine($"\t\t[{spellId}] = {Quote(name)},");
		}
		builder.AppendLine("\t},");

		builder.AppendLine("\tcharacters = {");
		foreach (var character in export.Characters)
		{
			builder.AppendLine("\t\t{");
			builder.AppendLine($"\t\t\tname = {Quote(character.Name)},");
			AppendIfNotNull(builder, "classFile", character.ClassFile);
			AppendIfNotNull(builder, "raceFile", character.RaceFile);
			AppendIfNotNull(builder, "raceId", character.RaceId);
			AppendIfNotNull(builder, "gender", character.Gender);
			AppendIfNotNull(builder, "level", character.Level);
			builder.AppendLine("\t\t\tprofessions = {");
			foreach (var profession in character.Professions)
			{
				builder.Append("\t\t\t\t{ ");
				builder.Append($"name = {Quote(profession.Name)}, level = {profession.Level}, max = {profession.Max}, source = {Quote(profession.Source)}");
				if (profession.Note is not null)
				{
					builder.Append($", note = {Quote(profession.Note)}");
				}
				if (profession.ScannedAt is not null)
				{
					builder.Append($", scannedAt = {Quote(profession.ScannedAt)}");
				}
				if (profession.Recipes.Count > 0)
				{
					builder.Append($", recipes = {{ {string.Join(", ", profession.Recipes)} }}");
				}
				builder.AppendLine(" },");
			}
			builder.AppendLine("\t\t\t},");
			builder.AppendLine("\t\t},");
		}
		builder.AppendLine("\t},");

		builder.AppendLine("\torders = {");
		foreach (var order in export.Orders ?? [])
		{
			builder.Append("\t\t{ ");
			builder.Append($"id = {order.Id}, requester = {Quote(order.Requester)}, crafter = {Quote(order.Crafter)}, ");
			builder.Append($"item = {Quote(order.Item)}, qty = {order.Quantity}, status = {Quote(order.Status)}, createdUtc = {Quote(order.CreatedUtc)}");
			if (order.ClientId is not null)
			{
				builder.Append($", clientId = {Quote(order.ClientId)}");
			}
			if (order.Note is not null)
			{
				builder.Append($", note = {Quote(order.Note)}");
			}
			builder.AppendLine(" },");
		}
		builder.AppendLine("\t},");

		builder.AppendLine("}");
		return builder.ToString();
	}

	private static void AppendIfNotNull(StringBuilder builder, string key, string? value)
	{
		if (value is not null)
		{
			builder.AppendLine($"\t\t\t{key} = {Quote(value)},");
		}
	}

	private static void AppendIfNotNull(StringBuilder builder, string key, int? value)
	{
		if (value is not null)
		{
			builder.AppendLine($"\t\t\t{key} = {value.Value.ToString(CultureInfo.InvariantCulture)},");
		}
	}

	private static string Quote(string value)
	{
		var escaped = value
			.Replace("\\", "\\\\")
			.Replace("\"", "\\\"")
			.Replace("\n", "\\n")
			.Replace("\r", "\\r");
		return $"\"{escaped}\"";
	}
}
