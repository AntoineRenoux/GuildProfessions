using System.Globalization;
using System.Text;
using GuildProfessions.Companion.Lua;

namespace GuildProfessions.Companion;

/// <summary>
/// Communication de la guilde (annonces, modèles, charte, objectifs) : lecture
/// des changements faits en jeu, répartition entre guildes et écriture dans
/// Data.lua. À part du reste : une erreur ici ne touche pas aux autres données.
/// </summary>
public static class CommunicationLua
{
	private static IEnumerable<LuaTable> Rows(LuaTable? table) =>
		((IEnumerable<LuaValue>?)table?.Map.Values ?? []).Select(v => v.AsTable()).OfType<LuaTable>();

	// Clé d'une annonce ou d'un objectif en jeu : numérique = id serveur, sinon id client.
	private static int? ServerId(string? key) => int.TryParse(key, out var id) ? id : null;

	/// <summary>
	/// GuildkeepDB.announcementEdits, templateEdits, handbookEdits, goalEdits
	/// et goalContributions ; null s'il n'y a rien à envoyer.
	/// </summary>
	public static UploadCommunication? Read(LuaTable db)
	{
		var announcements = Rows(db.GetTable("announcementEdits"))
			.Where(a => a.GetString("key") is not null && a.GetString("title") is not null && a.GetString("by") is not null)
			.Select(a => new UploadAnnouncement(a.GetString("key")!, ServerId(a.GetString("key")), a.GetString("title")!,
				a.GetString("body") ?? "", a.GetInt("eventId"), a.GetString("by")!, a.GetBool("deleted"), a.GetLong("at") ?? 0))
			.ToList();
		var templates = Rows(db.GetTable("templateEdits"))
			.Where(t => t.GetString("key") is not null && t.GetString("title") is not null && t.GetString("by") is not null)
			.Select(t => new UploadTemplate(t.GetString("key")!, t.GetString("title")!, t.GetString("body") ?? "", t.GetString("by")!,
				t.GetBool("deleted"), t.GetLong("at") ?? 0))
			.ToList();
		var handbook = Rows(db.GetTable("handbookEdits"))
			.Where(h => h.GetString("key") is not null && h.GetString("title") is not null && h.GetString("by") is not null)
			.Select(h => new UploadHandbookSection(h.GetString("key")!, h.GetString("title")!, h.GetString("body") ?? "",
				h.GetInt("order") ?? 0, h.GetString("by")!, h.GetBool("deleted"), h.GetLong("at") ?? 0))
			.ToList();
		var goals = Rows(db.GetTable("goalEdits"))
			.Where(g => g.GetString("key") is not null && g.GetString("title") is not null && g.GetString("by") is not null)
			.Select(g => new UploadGoal(g.GetString("key")!, ServerId(g.GetString("key")), g.GetString("title")!, g.GetString("description"),
				g.GetInt("target") ?? 0, g.GetString("unit"), g.GetLong("deadline"), g.GetBool("closed"), g.GetString("by")!, g.GetLong("at") ?? 0))
			.ToList();
		var contributions = Rows(db.GetTable("goalContributions"))
			.Where(c => c.GetString("clientId") is not null && c.GetString("goal") is not null && c.GetString("character") is not null)
			.Select(c =>
			{
				var goal = c.GetString("goal")!;
				var goalId = ServerId(goal);
				return new UploadContribution(c.GetString("clientId")!, goalId, goalId is null ? goal : null, c.GetString("character")!,
					c.GetInt("amount") ?? 0, c.GetString("note"), c.GetLong("at") ?? 0);
			})
			.ToList();
		if (announcements.Count + templates.Count + handbook.Count + goals.Count + contributions.Count == 0)
		{
			return null;
		}
		return new UploadCommunication(
			announcements.Count > 0 ? announcements : null,
			templates.Count > 0 ? templates : null,
			handbook.Count > 0 ? handbook : null,
			goals.Count > 0 ? goals : null,
			contributions.Count > 0 ? contributions : null);
	}

	/// <summary>Part d'une guilde : les changements faits par ses personnages (officier ou contributeur).</summary>
	public static UploadCommunication? ForGuild(UploadCommunication? communication, Func<string?, bool> mine)
	{
		if (communication is null)
		{
			return null;
		}
		var part = new UploadCommunication(
			NullIfEmpty(communication.Announcements?.Where(a => mine(a.By)).ToList()),
			NullIfEmpty(communication.Templates?.Where(t => mine(t.By)).ToList()),
			NullIfEmpty(communication.Handbook?.Where(h => mine(h.By)).ToList()),
			NullIfEmpty(communication.Goals?.Where(g => mine(g.By)).ToList()),
			NullIfEmpty(communication.Contributions?.Where(c => mine(c.Character)).ToList()));
		return part.Announcements is null && part.Templates is null && part.Handbook is null && part.Goals is null && part.Contributions is null
			? null
			: part;
	}

	private static List<T>? NullIfEmpty<T>(List<T>? list) => list is { Count: > 0 } ? list : null;

	/// <summary>Section communication = { announcements, templates, handbook, goals } de Data.lua.</summary>
	public static void Append(StringBuilder builder, ExportDto export, string indent, Func<string, string> quote)
	{
		if (export.Communication is not { } c)
		{
			return; // module absent de l'export : section omise, l'addon la traite comme vide
		}
		string N(long value) => value.ToString(CultureInfo.InvariantCulture);
		var i1 = indent + "\t";
		var i2 = indent + "\t\t";
		builder.AppendLine($"{indent}communication = {{");
		builder.AppendLine($"{i1}announcements = {{");
		foreach (var a in c.Announcements)
		{
			builder.Append($"{i2}{{ id = {a.Id}, title = {quote(a.Title)}, body = {quote(a.Body)}, author = {quote(a.Author)}, ");
			builder.Append($"createdAt = {N(a.CreatedAt)}, updatedAt = {N(a.UpdatedAt)}");
			if (a.ClientId is not null)
			{
				builder.Append($", clientId = {quote(a.ClientId)}");
			}
			if (a.EventId is not null)
			{
				builder.Append($", eventId = {a.EventId}");
			}
			builder.AppendLine(" },");
		}
		builder.AppendLine($"{i1}}},");
		builder.AppendLine($"{i1}templates = {{");
		foreach (var t in c.Templates)
		{
			builder.AppendLine($"{i2}{{ key = {quote(t.Key)}, title = {quote(t.Title)}, body = {quote(t.Body)}, updatedAt = {N(t.UpdatedAt)} }},");
		}
		builder.AppendLine($"{i1}}},");
		builder.AppendLine($"{i1}handbook = {{");
		foreach (var h in c.Handbook)
		{
			builder.Append($"{i2}{{ key = {quote(h.Key)}, title = {quote(h.Title)}, body = {quote(h.Body)}, order = {h.Order}, updatedAt = {N(h.UpdatedAt)}");
			if (h.UpdatedBy is not null)
			{
				builder.Append($", updatedBy = {quote(h.UpdatedBy)}");
			}
			builder.AppendLine(" },");
		}
		builder.AppendLine($"{i1}}},");
		builder.AppendLine($"{i1}goals = {{");
		foreach (var g in c.Goals)
		{
			builder.Append($"{i2}{{ id = {g.Id}, title = {quote(g.Title)}, target = {g.Target}, progress = {g.Progress}, ");
			builder.Append($"closed = {(g.Closed ? "true" : "false")}, createdBy = {quote(g.CreatedBy)}, updatedAt = {N(g.UpdatedAt)}");
			if (g.ClientId is not null)
			{
				builder.Append($", clientId = {quote(g.ClientId)}");
			}
			if (g.Description is not null)
			{
				builder.Append($", description = {quote(g.Description)}");
			}
			if (g.Unit is not null)
			{
				builder.Append($", unit = {quote(g.Unit)}");
			}
			if (g.Deadline is not null)
			{
				builder.Append($", deadline = {N(g.Deadline.Value)}");
			}
			builder.Append(", contributions = { ");
			builder.Append(string.Join(", ", g.Contributions.Select(x =>
				$"{{ clientId = {quote(x.ClientId)}, character = {quote(x.Character)}, amount = {x.Amount}, at = {N(x.At)}" +
				(x.Note is null ? "" : $", note = {quote(x.Note)}") + " }")));
			builder.AppendLine(" } },");
		}
		builder.AppendLine($"{i1}}},");
		builder.AppendLine($"{indent}}},");
	}
}
