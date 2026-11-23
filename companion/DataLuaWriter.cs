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
	private const string SignaturePrefix = "-- guildkeep-sync: ";
	// Marque d'un export partiel : à version égale, l'export complet qui suit ne
	// correspond pas à la signature et réécrit le fichier (sinon les sections
	// absentes resteraient figées jusqu'au prochain changement côté serveur).
	private const string PartialMarker = "!partiel";

	/// <summary>
	/// Ligne qui identifie le contenu (guildes et versions d'export). Le compagnon
	/// réécrit Data.lua dès qu'elle ne correspond plus : fichier remplacé par une
	/// mise à jour de l'addon (CurseForge, zip manuel), effacé ou modifié.
	/// </summary>
	public static string Signature(IReadOnlyDictionary<string, long> versions) =>
		SignaturePrefix + string.Join(";", versions
			.OrderBy(entry => entry.Key, StringComparer.Ordinal)
			.Select(entry => $"{entry.Key}={entry.Value.ToString(CultureInfo.InvariantCulture)}"));

	/// <summary>Signature d'exports reçus : celle des versions, avec la marque des exports partiels.</summary>
	public static string Signature(IReadOnlyList<ExportDto> exports) =>
		SignaturePrefix + string.Join(";", exports
			.Select(e => (Key: e.GuildName ?? "", e.Version, e.Partial))
			.OrderBy(entry => entry.Key, StringComparer.Ordinal)
			.Select(entry => $"{entry.Key}={entry.Version.ToString(CultureInfo.InvariantCulture)}{(entry.Partial ? PartialMarker : "")}"));

	/// <summary>Vrai si le Data.lua existant a été écrit pour exactement ces versions (exports complets).</summary>
	public static bool IsCurrent(string path, IReadOnlyDictionary<string, long> versions) => HasSignature(path, Signature(versions));

	/// <summary>Vrai si le Data.lua existant a été écrit pour exactement ces exports (versions et exports partiels).</summary>
	public static bool IsCurrent(string path, IReadOnlyList<ExportDto> exports) => HasSignature(path, Signature(exports));

	private static bool HasSignature(string path, string expected) =>
		File.Exists(path) && File.ReadLines(path).Take(5).Any(line => line == expected);

	public static string Build(IReadOnlyList<ExportDto> exports)
	{
		var builder = new StringBuilder();
		builder.AppendLine("-- Fichier généré par l'app compagnon Guildkeep.");
		builder.AppendLine("-- Ne pas éditer : écrasé à chaque synchronisation.");
		builder.AppendLine(Signature(exports));
		builder.AppendLine("Guildkeep_ServerData = {");
		builder.AppendLine("\tguilds = {");
		foreach (var export in exports)
		{
			builder.AppendLine("\t\t{");
			AppendGuild(builder, export, "\t\t\t");
			AppendRaids(builder, export, "\t\t\t");
			AppendCatalog(builder, export, "\t\t\t");
			CommunicationLua.Append(builder, export, "\t\t\t", Quote);
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

		// Sections absentes (module en panne côté serveur) : omises, l'addon les lit comme vides.
		if (export.RecipeNames is { } recipeNames)
		{
			builder.AppendLine($"{indent}recipeNames = {{");
			foreach (var (spellId, name) in recipeNames.OrderBy(entry => entry.Key))
			{
				builder.AppendLine($"{indent}\t[{spellId}] = {Quote(name)},");
			}
			builder.AppendLine($"{indent}}},");
		}

		AppendCharacters(builder, export, indent);
		AppendOrders(builder, export, indent);
		AppendEvents(builder, export, indent);
	}

	private static void AppendCharacters(StringBuilder builder, ExportDto export, string indent)
	{
		if (export.Characters is not { } characters)
		{
			return;
		}
		builder.AppendLine($"{indent}characters = {{");
		foreach (var character in characters)
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
			if (character.Quests is { Count: > 0 })
			{
				builder.AppendLine($"{c}quests = {{");
				foreach (var q in character.Quests)
				{
					builder.Append($"{c}\t{{ id = {q.Id}, title = {Quote(q.Title)}, kind = {Quote(q.Kind)}, complete = {(q.Complete ? "true" : "false")}");
					if (q.Location is not null)
					{
						builder.Append($", location = {Quote(q.Location)}");
					}
					if (q.Level is not null)
					{
						builder.Append($", level = {q.Level}");
					}
					builder.AppendLine(" },");
				}
				builder.AppendLine($"{c}}},");
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
	}

	private static void AppendOrders(StringBuilder builder, ExportDto export, string indent)
	{
		if (export.Orders is not { } orders)
		{
			return;
		}
		builder.AppendLine($"{indent}orders = {{");
		foreach (var order in orders)
		{
			builder.Append($"{indent}\t{{ ");
			builder.Append($"id = {order.Id}, requester = {Quote(order.Requester)}, ");
			if (order.Crafter is not null)
			{
				builder.Append($"crafter = {Quote(order.Crafter)}, ");
			}
			builder.Append($"item = {Quote(order.Item)}, qty = {order.Quantity}, status = {Quote(order.Status)}, createdUtc = {Quote(order.CreatedUtc)}");
			if (order.ClientId is not null)
			{
				builder.Append($", clientId = {Quote(order.ClientId)}");
			}
			if (order.Note is not null)
			{
				builder.Append($", note = {Quote(order.Note)}");
			}
			if (order.RecipeId is not null)
			{
				builder.Append($", recipeId = {order.RecipeId}");
			}
			if (order.ExpiresUtc is not null)
			{
				builder.Append($", expiresUtc = {Quote(order.ExpiresUtc)}");
			}
			if (order.Offers is { Count: > 0 })
			{
				builder.Append(", offers = { ");
				builder.Append(string.Join(", ", order.Offers.Select(f =>
					$"{{ crafter = {Quote(f.Crafter)}, createdUtc = {Quote(f.CreatedUtc)}{(f.Note is null ? "" : $", note = {Quote(f.Note)}")} }}")));
				builder.Append(" }");
			}
			builder.AppendLine(" },");
		}
		builder.AppendLine($"{indent}}},");
	}

	private static void AppendEvents(StringBuilder builder, ExportDto export, string indent)
	{
		if (export.Events is { Count: > 0 } events)
		{
			builder.AppendLine($"{indent}events = {{");
			foreach (var e in events)
			{
				var c = indent + "\t\t";
				builder.AppendLine($"{indent}\t{{");
				builder.Append($"{c}id = {e.Id}, title = {Quote(e.Title)}, kind = {Quote(e.Kind)}, startsAt = {e.StartsAt}, organizer = {Quote(e.Organizer)}, ");
				builder.AppendLine($"tank = {e.Tank}, healer = {e.Healer}, melee = {e.Melee}, ranged = {e.Ranged}, cancelled = {(e.Cancelled ? "true" : "false")}, locked = {(e.Locked ? "true" : "false")}, updatedAt = {e.UpdatedAt},");
				AppendIfNotNull(builder, c, "clientId", e.ClientId);
				AppendIfNotNull(builder, c, "description", e.Description);
				builder.AppendLine($"{c}signups = {{");
				foreach (var s in e.Signups)
				{
					builder.Append($"{c}\t{{ character = {Quote(s.Character)}, role = {Quote(s.Role)}, response = {Quote(s.Response)}, responseAt = {s.ResponseAt}, ");
					builder.Append($"placement = {Quote(s.Placement)}, placementAt = {s.PlacementAt}");
					if (s.ClassFile is not null)
					{
						builder.Append($", classFile = {Quote(s.ClassFile)}");
					}
					if (s.Spec is not null)
					{
						builder.Append($", spec = {Quote(s.Spec)}");
					}
					if (s.Note is not null)
					{
						builder.Append($", note = {Quote(s.Note)}");
					}
					builder.AppendLine(" },");
				}
				builder.AppendLine($"{c}}},");
				builder.AppendLine($"{c}reserves = {{");
				foreach (var r in e.Reserves ?? [])
				{
					builder.Append($"{c}\t{{ character = {Quote(r.Character)}, priority = {Quote(r.Priority)}, itemId = {r.ItemId}, at = {r.At}");
					if (r.ItemName is not null)
					{
						builder.Append($", itemName = {Quote(r.ItemName)}");
					}
					if (r.SetBy is not null)
					{
						builder.Append($", setBy = {Quote(r.SetBy)}");
					}
					builder.AppendLine(" },");
				}
				builder.AppendLine($"{c}}},");
				builder.AppendLine($"{c}hardReserves = {{");
				foreach (var h in e.HardReserves ?? [])
				{
					builder.Append($"{c}\t{{ itemId = {h.ItemId}, active = {(h.Active ? "true" : "false")}, at = {h.At}");
					if (h.ItemName is not null)
					{
						builder.Append($", itemName = {Quote(h.ItemName)}");
					}
					if (h.Holder is not null)
					{
						builder.Append($", holder = {Quote(h.Holder)}");
					}
					builder.AppendLine(" },");
				}
				builder.AppendLine($"{c}}},");
				builder.AppendLine($"{c}duties = {{");
				foreach (var d in e.Duties ?? [])
				{
					builder.Append($"{c}\t{{ duty = {Quote(d.Duty)}, at = {d.At}");
					if (d.Character is not null)
					{
						builder.Append($", character = {Quote(d.Character)}");
					}
					builder.AppendLine(" },");
				}
				builder.AppendLine($"{c}}},");
				builder.AppendLine($"{indent}\t}},");
			}
			builder.AppendLine($"{indent}}},");
		}
	}

	// Catalogue des recettes de la guilde : { [id] = { "nom", "métier" } }.
	private static void AppendCatalog(StringBuilder builder, ExportDto export, string indent)
	{
		if (export.Catalog is not { Count: > 0 } catalog)
		{
			return;
		}
		builder.AppendLine($"{indent}catalog = {{");
		foreach (var recipe in catalog)
		{
			builder.AppendLine($"{indent}\t[{recipe.SpellId}] = {{ {Quote(recipe.Name)}, {Quote(recipe.Profession)} }},");
		}
		builder.AppendLine($"{indent}}},");
	}

	private static void AppendRaids(StringBuilder builder, ExportDto export, string indent)
	{
		if (export.Raids is not { Count: > 0 } raids)
		{
			return;
		}
		builder.AppendLine($"{indent}raids = {{");
		foreach (var r in raids)
		{
			var c = indent + "\t\t";
			builder.AppendLine($"{indent}\t{{");
			builder.Append($"{c}id = {r.Id}, key = {Quote(r.Key)}, instance = {Quote(r.Instance)}, day = {Quote(r.Day)}, startedAt = {r.StartedAt}, endedAt = {r.EndedAt}");
			if (r.Leader is not null)
			{
				builder.Append($", leader = {Quote(r.Leader)}");
			}
			if (r.EventId is not null)
			{
				builder.Append($", eventId = {r.EventId}");
			}
			builder.AppendLine(",");
			builder.Append($"{c}participants = {{ ");
			builder.Append(string.Join(", ", r.Participants.Select(p =>
				$"{{ character = {Quote(p.Character)}, minutes = {p.Minutes}{(p.ClassFile is null ? "" : $", classFile = {Quote(p.ClassFile)}")} }}")));
			builder.AppendLine(" },");
			builder.AppendLine($"{c}loot = {{");
			foreach (var l in r.Loot)
			{
				builder.Append($"{c}\t{{ id = {l.Id}, character = {Quote(l.Character)}, itemId = {l.ItemId}, quality = {l.Quality}, ordinal = {l.Ordinal}, at = {l.At}");
				if (l.ItemName is not null)
				{
					builder.Append($", itemName = {Quote(l.ItemName)}");
				}
				builder.Append($", voided = {(l.Voided ? "true" : "false")}, voidAt = {l.VoidAt}");
				if (l.VoidNote is not null)
				{
					builder.Append($", voidNote = {Quote(l.VoidNote)}");
				}
				if (l.VoidBy is not null)
				{
					builder.Append($", voidBy = {Quote(l.VoidBy)}");
				}
				builder.AppendLine(" },");
			}
			builder.AppendLine($"{c}}},");
			builder.Append($"{c}corrections = {{ ");
			builder.Append(string.Join(", ", r.Corrections.Select(k =>
				$"{{ character = {Quote(k.Character)}, status = {Quote(k.Status)}, at = {k.At}" +
				(k.Note is null ? "" : $", note = {Quote(k.Note)}") + (k.By is null ? "" : $", by = {Quote(k.By)}") + " }")));
			builder.AppendLine(" },");
			builder.AppendLine($"{indent}\t}},");
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
