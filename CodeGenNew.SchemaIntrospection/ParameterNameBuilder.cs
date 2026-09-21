using System.Data;
using System.Text;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> Builds Hungarian-prefixed SQL parameter names matching the convention in the original
/// ProductionUnitMaster_Update worked example (Docs/specs.md Appendix A). </summary>
public static class ParameterNameBuilder
{
    public static string Build(string columnName, SqlDbType sqlType)
    {
        string prefix = sqlType switch
        {
            SqlDbType.BigInt or SqlDbType.Int or SqlDbType.SmallInt or SqlDbType.TinyInt => "lng",
            SqlDbType.Char or SqlDbType.NChar or SqlDbType.VarChar or SqlDbType.NVarChar
                or SqlDbType.Text or SqlDbType.NText => "str",
            SqlDbType.Date or SqlDbType.DateTime or SqlDbType.DateTime2
                or SqlDbType.SmallDateTime or SqlDbType.DateTimeOffset or SqlDbType.Time => "dte",
            SqlDbType.Bit => "bln",
            SqlDbType.Decimal => "dec",
            SqlDbType.Float or SqlDbType.Real => "flt",
            SqlDbType.Money or SqlDbType.SmallMoney => "cur",
            SqlDbType.UniqueIdentifier => "guid",
            SqlDbType.Binary or SqlDbType.VarBinary or SqlDbType.Image => "bin",
            _ => "var"
        };

        return $"@p{prefix}{ToPascalCase(columnName)}";
    }

    private static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Most column names are already PascalCase; just make sure the first character is uppercase
        // and strip any bracket-quoting a reserved-word name might already carry.
        string cleaned = name.Trim('[', ']');
        if (cleaned.Length == 0)
            return cleaned;

        var builder = new StringBuilder(cleaned);
        builder[0] = char.ToUpperInvariant(builder[0]);
        return builder.ToString();
    }
}
