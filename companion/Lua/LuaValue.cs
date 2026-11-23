using System.Globalization;

namespace GuildProfessions.Companion.Lua;

public abstract record LuaValue
{
	public string? AsString() => (this as LuaString)?.Value;

	public double? AsNumber() => (this as LuaNumber)?.Value;

	public int? AsInt() => this is LuaNumber n ? (int)n.Value : null;

	public long? AsLong() => this is LuaNumber n ? (long)n.Value : null;

	public bool? AsBool() => (this as LuaBool)?.Value;

	public LuaTable? AsTable() => this as LuaTable;
}

public sealed record LuaString(string Value) : LuaValue;

public sealed record LuaNumber(double Value) : LuaValue;

public sealed record LuaBool(bool Value) : LuaValue;

public sealed record LuaNil : LuaValue;

/// <summary>
/// Table Lua : partie dictionnaire (clés chaîne ou nombre, normalisées en
/// chaîne invariante) + partie tableau (items positionnels).
/// </summary>
public sealed record LuaTable : LuaValue
{
	public Dictionary<string, LuaValue> Map { get; } = [];
	public List<LuaValue> Items { get; } = [];

	public LuaValue? Get(string key) => Map.GetValueOrDefault(key);

	public string? GetString(string key) => Get(key)?.AsString();

	public int? GetInt(string key) => Get(key)?.AsInt();

	public long? GetLong(string key) => Get(key)?.AsLong();

	public bool GetBool(string key) => Get(key)?.AsBool() ?? false;

	public LuaTable? GetTable(string key) => Get(key)?.AsTable();

	public static string NormalizeKey(double number) => number.ToString(CultureInfo.InvariantCulture);
}
