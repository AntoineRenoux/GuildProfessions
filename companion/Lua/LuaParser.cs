using System.Globalization;
using System.Text;

namespace GuildProfessions.Companion.Lua;

/// <summary>
/// Parseur minimal du sous-ensemble de Lua émis par WoW dans les
/// SavedVariables : littéraux de table, chaînes, nombres, booléens, nil.
/// On parse, on n'exécute jamais — un SavedVariables malveillant ne peut
/// donc rien faire d'autre qu'échouer à parser.
/// </summary>
public sealed class LuaParser
{
	private readonly string _text;
	private int _pos;

	private LuaParser(string text, int pos)
	{
		_text = text;
		_pos = pos;
	}

	/// <summary>Extrait la valeur assignée à <paramref name="variableName"/> dans le fichier.</summary>
	public static LuaTable ParseSavedVariable(string content, string variableName)
	{
		var index = content.IndexOf(variableName + " =", StringComparison.Ordinal);
		if (index < 0)
		{
			index = content.IndexOf(variableName + "=", StringComparison.Ordinal);
		}
		if (index < 0)
		{
			throw new FormatException($"Variable {variableName} introuvable dans le fichier.");
		}
		var equals = content.IndexOf('=', index);
		var parser = new LuaParser(content, equals + 1);
		parser.SkipTrivia();
		if (parser.ParseValue() is not LuaTable table)
		{
			throw new FormatException($"{variableName} n'est pas une table.");
		}
		return table;
	}

	private LuaValue ParseValue()
	{
		SkipTrivia();
		if (_pos >= _text.Length)
		{
			throw new FormatException("Fin de fichier inattendue.");
		}
		var c = _text[_pos];
		if (c == '{')
		{
			return ParseTable();
		}
		if (c is '"' or '\'')
		{
			return new LuaString(ParseString(c));
		}
		if (char.IsDigit(c) || c is '-' or '+' or '.')
		{
			return ParseNumber();
		}
		if (MatchKeyword("true"))
		{
			return new LuaBool(true);
		}
		if (MatchKeyword("false"))
		{
			return new LuaBool(false);
		}
		if (MatchKeyword("nil"))
		{
			return new LuaNil();
		}
		throw new FormatException($"Caractère inattendu '{c}' à la position {_pos}.");
	}

	private LuaTable ParseTable()
	{
		Expect('{');
		var table = new LuaTable();
		while (true)
		{
			SkipTrivia();
			if (Peek() == '}')
			{
				_pos++;
				return table;
			}
			if (Peek() == '[')
			{
				// [clé] = valeur — clé chaîne ou nombre.
				_pos++;
				var key = ParseValue();
				SkipTrivia();
				Expect(']');
				SkipTrivia();
				Expect('=');
				var value = ParseValue();
				var normalized = key switch
				{
					LuaString s => s.Value,
					LuaNumber n => LuaTable.NormalizeKey(n.Value),
					_ => throw new FormatException("Clé de table non supportée."),
				};
				table.Map[normalized] = value;
			}
			else if (char.IsLetter(Peek()) || Peek() == '_')
			{
				// identifiant = valeur, ou mot-clé littéral en item de tableau.
				var save = _pos;
				var identifier = ParseIdentifier();
				SkipTrivia();
				if (Peek() == '=')
				{
					_pos++;
					table.Map[identifier] = ParseValue();
				}
				else
				{
					_pos = save;
					table.Items.Add(ParseValue());
				}
			}
			else
			{
				table.Items.Add(ParseValue());
			}
			SkipTrivia();
			if (Peek() is ',' or ';')
			{
				_pos++;
			}
		}
	}

	private string ParseString(char quote)
	{
		_pos++; // quote ouvrante
		var builder = new StringBuilder();
		while (_pos < _text.Length)
		{
			var c = _text[_pos++];
			if (c == quote)
			{
				return builder.ToString();
			}
			if (c == '\\' && _pos < _text.Length)
			{
				var escaped = _text[_pos++];
				switch (escaped)
				{
					case 'n': builder.Append('\n'); break;
					case 'r': builder.Append('\r'); break;
					case 't': builder.Append('\t'); break;
					case '\\': builder.Append('\\'); break;
					case '"': builder.Append('"'); break;
					case '\'': builder.Append('\''); break;
					default:
						if (char.IsDigit(escaped))
						{
							// \ddd : code décimal sur 1 à 3 chiffres.
							var code = escaped - '0';
							for (var i = 0; i < 2 && _pos < _text.Length && char.IsDigit(_text[_pos]); i++)
							{
								code = code * 10 + (_text[_pos++] - '0');
							}
							builder.Append((char)code);
						}
						else
						{
							builder.Append(escaped);
						}
						break;
				}
			}
			else
			{
				builder.Append(c);
			}
		}
		throw new FormatException("Chaîne non terminée.");
	}

	private LuaNumber ParseNumber()
	{
		var start = _pos;
		while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] is '.' or '-' or '+'))
		{
			// IsLetterOrDigit couvre l'hexa (0x1A) et l'exposant (1e5).
			if (_text[_pos] is '-' or '+' && _pos > start && _text[_pos - 1] is not ('e' or 'E'))
			{
				break;
			}
			_pos++;
		}
		var raw = _text[start.._pos];
		double value = raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
			? long.Parse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture)
			: double.Parse(raw, CultureInfo.InvariantCulture);
		return new LuaNumber(value);
	}

	private string ParseIdentifier()
	{
		var start = _pos;
		while (_pos < _text.Length && (char.IsLetterOrDigit(_text[_pos]) || _text[_pos] == '_'))
		{
			_pos++;
		}
		return _text[start.._pos];
	}

	private bool MatchKeyword(string keyword)
	{
		if (_text.AsSpan(_pos).StartsWith(keyword) &&
			(_pos + keyword.Length >= _text.Length || !char.IsLetterOrDigit(_text[_pos + keyword.Length])))
		{
			_pos += keyword.Length;
			return true;
		}
		return false;
	}

	private void SkipTrivia()
	{
		while (_pos < _text.Length)
		{
			var c = _text[_pos];
			if (char.IsWhiteSpace(c))
			{
				_pos++;
			}
			else if (c == '-' && _pos + 1 < _text.Length && _text[_pos + 1] == '-')
			{
				while (_pos < _text.Length && _text[_pos] != '\n')
				{
					_pos++;
				}
			}
			else
			{
				break;
			}
		}
	}

	private char Peek() => _pos < _text.Length ? _text[_pos] : '\0';

	private void Expect(char expected)
	{
		if (Peek() != expected)
		{
			throw new FormatException($"'{expected}' attendu à la position {_pos}.");
		}
		_pos++;
	}
}
