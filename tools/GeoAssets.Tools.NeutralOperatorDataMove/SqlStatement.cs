using System.Globalization;
using System.Text;
using Npgsql;
using NpgsqlTypes;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// A statement under construction: its text and its positional parameters (<c>$1</c>, <c>$2</c>, …), numbered in the
/// order they are added. Every value travels as a parameter; only identifiers — from the EF models, validated against
/// the catalog — are written into the text, always delimited (<see cref="Identifier"/>).
/// </summary>
internal sealed class SqlStatement
{
    private readonly StringBuilder _text = new();
    private readonly List<NpgsqlParameter> _parameters = [];

    public string Text => _text.ToString();

    public IReadOnlyList<NpgsqlParameter> Parameters => _parameters;

    public SqlStatement Append(string sql)
    {
        _text.Append(sql);
        return this;
    }

    /// <summary>A text parameter (null is SQL NULL); the statement casts it to the column's type where it needs to.</summary>
    public string TextValue(string? value) =>
        Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = value is null ? DBNull.Value : value });

    public string TextArray(IEnumerable<string?> values) =>
        Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text, Value = values.ToArray() });

    public string Integer(int value) =>
        Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = value });

    public string IntegerArray(IEnumerable<int?> values) =>
        Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Integer, Value = values.ToArray() });

    /// <summary><paramref name="name"/> as a delimited PostgreSQL identifier.</summary>
    public static string Identifier(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    /// <summary><c>"schema"."name"</c>.</summary>
    public static string Qualified(string schema, string name) => Identifier(schema) + "." + Identifier(name);

    private string Add(NpgsqlParameter parameter)
    {
        _parameters.Add(parameter);
        return "$" + _parameters.Count.ToString(CultureInfo.InvariantCulture);
    }
}
