using System.Globalization;
using System.Text;

namespace GuildProfessions.Companion;

/// <summary>
/// Génère le Data.lua chargé par l'addon à partir des exports du serveur :
/// une section par guilde connue du poste, l'addon choisissant celle du
/// personnage connecté. Le format doit rester lisible par Core.lua.
/// </summary>
public static class DataLuaWriter
{
	public static string Build(IReadOnlyList<ExportDto> exports)
	{
		var builder = new StringBuilder();
		builder.AppendLine("-- Fichier généré par l'app compagnon Guildkeep.");
		builder.AppendLine("-- Ne pas éditer : écrasé à chaque synchronisation.");
		builder.AppendLine("Guildkeep_ServerData = {");
		builder.AppendLine("\tguilds = {");
		foreach (var export in exports)
		{
			builder.AppendLine("\t\t{");
			AppendGuild(builder, export, "\t\t\t");
			builder.AppendLine("\t\t},");
		}
		builder.AppendLine("\t},");
		builder.AppendLine("}");
		return builder.ToString();
	}

	private static void AppendGuild(StringBuilder builder, ExportDto export, string indent)
	{
		builder.AppendLine($"{indent}version = {export.Version},");
		builder.AppendLine($"{indent}generatedUtc = \"{export.GeneratedUtc:yyyy-MM-dd HH:mm} UTC\",");
		if (export.GuildName is not null)
		{
			builder.AppendLine($"{indent}guildName = {Quote(export.GuildName)},");
		}

		builder.AppendLine($"{indent}recipeNames = {{");
		foreach (var (spellId, name) in export.RecipeNames.OrderBy(entry => entry.Key))
		{
			builder.AppendLine($"{indent}\t[{spellId}] = {Quote(name)},");
		}
		builder.AppendLine($"{indent}}},");

		builder.AppendLine($"{indent}characters = {{");
		foreach (var character in export.Characters)
		{
			var c = indent + "\t\t";
			builder.AppendLine($"{indent}\t{{");
			builder.AppendLine($"{c}name = {Quote(character.Name)},");
			AppendIfNotNull(builder, c, "classFile", character.ClassFile);
			AppendIfNotNull(builder, c, "raceFile", character.RaceFile);
			AppendIfNotNull(builder, c, "raceId", character.RaceId);
			AppendIfNotNull(builder, c, "gender", character.Gender);
			AppendIfNotNull(builder, c, "level", character.Level);
			if (character.Equipment is { Count: > 0 })
			{
				builder.Append($"{c}equipment = {{ ");
				builder.Append(string.Join(", ", character.Equipment
					.OrderBy(entry => entry.Key)
					.Select(entry => $"[{entry.Key}] = {Quote(entry.Value)}")));
				builder.AppendLine(" },");
			}
			builder.AppendLine($"{c}professions = {{");
			foreach (var profession in character.Professions)
			{
				builder.Append($"{c}\t{{ ");
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
			builder.AppendLine($"{c}}},");
			builder.AppendLine($"{indent}\t}},");
		}
		builder.AppendLine($"{indent}}},");

		builder.AppendLine($"{indent}orders = {{");
		foreach (var order in export.Orders ?? [])
		{
			builder.Append($"{indent}\t{{ ");
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
		builder.AppendLine($"{indent}}},");
	}

	private static void AppendIfNotNull(StringBuilder builder, string indent, string key, string? value)
	{
		if (value is not null)
		{
			builder.AppendLine($"{indent}{key} = {Quote(value)},");
		}
	}

	private static void AppendIfNotNull(StringBuilder builder, string indent, string key, int? value)
	{
		if (value is not null)
		{
			builder.AppendLine($"{indent}{key} = {value.Value.ToString(CultureInfo.InvariantCulture)},");
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
