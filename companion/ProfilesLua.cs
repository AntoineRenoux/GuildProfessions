using System.Globalization;
using System.Text;
using GuildProfessions.Companion.Lua;

namespace GuildProfessions.Companion;

/// <summary>
/// Profils et accueil (principal, rerolls, rôles, talents, versions, arrivées
/// et départs, étapes d'accueil) : lecture des relevés faits en jeu,
/// répartition entre guildes et écriture dans Data.lua. À part du reste : une
/// erreur ici ne touche pas aux autres données.
/// </summary>
public static class ProfilesLua
{
	private static IEnumerable<LuaTable> Rows(LuaTable? table) =>
		((IEnumerable<LuaValue>?)table?.Items ?? []).Concat((IEnumerable<LuaValue>?)table?.Map.Values ?? [])
			.Select(v => v.AsTable()).OfType<LuaTable>();

	private static List<string> Strings(LuaTable? table) =>
		((IEnumerable<LuaValue>?)table?.Items ?? []).Select(v => v.AsString()).OfType<string>().ToList();

	/// <summary>
	/// GuildkeepDB.profileEdits, talents, versions, rosterChanges et
	/// onboardingEdits ; null s'il n'y a rien à envoyer.
	/// </summary>
	public static UploadProfiles? Read(LuaTable db)
	{
		var profiles = Rows(db.GetTable("profileEdits"))
			.Where(p => p.GetString("main") is not null)
			.Select(p => new UploadProfile(p.GetString("main")!, Strings(p.GetTable("alts")), Strings(p.GetTable("roles")),
				p.GetString("availability"), p.GetString("note"), p.GetLong("at") ?? 0))
			.ToList();
		var talents = new List<UploadTalents>();
		foreach (var (character, value) in db.GetTable("talents")?.Map ?? [])
		{
			if (value.AsTable() is { } t && t.GetString("summary") is { } summary)
			{
				talents.Add(new UploadTalents(character, t.GetString("spec"), summary, t.GetLong("at") ?? 0));
			}
		}
		var versions = new List<UploadVersion>();
		foreach (var (character, value) in db.GetTable("versions")?.Map ?? [])
		{
			if (value.AsString() is { } version)
			{
				versions.Add(new UploadVersion(character, version));
			}
		}
		var changes = Rows(db.GetTable("rosterChanges"))
			.Where(r => r.GetString("character") is not null && r.GetString("kind") is not null)
			.Select(r => new UploadRosterChange(r.GetString("character")!, r.GetString("kind")!, r.GetLong("at") ?? 0, r.GetString("by")))
			.ToList();
		var onboarding = Rows(db.GetTable("onboardingEdits"))
			.Where(o => o.GetString("character") is not null && o.GetString("step") is not null && o.GetString("by") is not null)
			.Select(o => new UploadOnboardingStep(o.GetString("character")!, o.GetString("step")!, o.GetBool("done"), o.GetString("by")!,
				o.GetLong("at") ?? 0))
			.ToList();
		var part = new UploadProfiles(NullIfEmpty(profiles), NullIfEmpty(talents), NullIfEmpty(versions), NullIfEmpty(changes),
			NullIfEmpty(onboarding));
		return IsEmpty(part) ? null : part;
	}

	/// <summary>Part d'une guilde : ce que ses personnages ont relevé ou décidé.</summary>
	public static UploadProfiles? ForGuild(UploadProfiles? profiles, Func<string?, bool> mine)
	{
		if (profiles is null)
		{
			return null;
		}
		var part = new UploadProfiles(
			NullIfEmpty(profiles.Profiles?.Where(p => mine(p.Main)).ToList()),
			NullIfEmpty(profiles.Talents?.Where(t => mine(t.Character)).ToList()),
			NullIfEmpty(profiles.Versions?.Where(v => mine(v.Character)).ToList()),
			NullIfEmpty(profiles.RosterChanges?.Where(r => mine(r.By)).ToList()),
			NullIfEmpty(profiles.Onboarding?.Where(o => mine(o.By)).ToList()));
		return IsEmpty(part) ? null : part;
	}

	private static bool IsEmpty(UploadProfiles p) =>
		p.Profiles is null && p.Talents is null && p.Versions is null && p.RosterChanges is null && p.Onboarding is null;

	private static List<T>? NullIfEmpty<T>(List<T>? list) => list is { Count: > 0 } ? list : null;

	/// <summary>Section profiles = { profiles, talents, rosterChanges, onboarding, versions } de Data.lua.</summary>
	public static void Append(StringBuilder builder, ExportDto export, string indent, Func<string, string> quote)
	{
		if (export.Profiles is not { } p)
		{
			return; // module absent de l'export : section omise, l'addon la traite comme vide
		}
		string N(long value) => value.ToString(CultureInfo.InvariantCulture);
		string List(IEnumerable<string> values) => "{ " + string.Join(", ", values.Select(quote)) + " }";
		var i1 = indent + "\t";
		var i2 = indent + "\t\t";
		builder.AppendLine($"{indent}profiles = {{");
		builder.AppendLine($"{i1}profiles = {{");
		foreach (var profile in p.Profiles)
		{
			builder.Append($"{i2}{{ main = {quote(profile.Main)}, alts = {List(profile.Alts)}, roles = {List(profile.Roles)}, updatedAt = {N(profile.UpdatedAt)}");
			if (profile.Availability is not null)
			{
				builder.Append($", availability = {quote(profile.Availability)}");
			}
			if (profile.Note is not null)
			{
				builder.Append($", note = {quote(profile.Note)}");
			}
			builder.AppendLine(" },");
		}
		builder.AppendLine($"{i1}}},");
		builder.AppendLine($"{i1}talents = {{");
		foreach (var t in p.Talents)
		{
			builder.AppendLine($"{i2}{{ character = {quote(t.Character)}, summary = {quote(t.Summary)}, at = {N(t.At)}" +
				(t.Spec is null ? "" : $", spec = {quote(t.Spec)}") + " },");
		}
		builder.AppendLine($"{i1}}},");
		builder.AppendLine($"{i1}rosterChanges = {{");
		foreach (var r in p.RosterChanges)
		{
			builder.AppendLine($"{i2}{{ character = {quote(r.Character)}, kind = {quote(r.Kind)}, at = {N(r.At)} }},");
		}
		builder.AppendLine($"{i1}}},");
		builder.AppendLine($"{i1}onboarding = {{");
		foreach (var o in p.Onboarding)
		{
			builder.AppendLine($"{i2}{{ character = {quote(o.Character)}, done = {List(o.Done)}, updatedAt = {N(o.UpdatedAt)} }},");
		}
		builder.AppendLine($"{i1}}},");
		builder.AppendLine($"{i1}versions = {{");
		foreach (var v in p.Versions)
		{
			builder.AppendLine($"{i2}{{ character = {quote(v.Character)}, version = {quote(v.Version)}, at = {N(v.At)} }},");
		}
		builder.AppendLine($"{i1}}},");
		builder.AppendLine($"{indent}}},");
	}
}
