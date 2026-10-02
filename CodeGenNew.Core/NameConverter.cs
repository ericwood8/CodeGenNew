using System.Text;

namespace CodeGenNew.Core;

/// <summary> How a table or column name read from the database becomes the name generated code uses. A database that writes
/// <c>customer_item</c> and <c>customer_id</c> (MySQL and PostgreSQL usually do; MySQL on Windows even stores table names in lower case)
/// is meant to give <c>CustomerItem</c> and <c>CustomerId</c> in C# and TypeScript, while the SQL keeps the real names. </summary>
public enum NamingStyle
{
    /// <summary> The database's own names are the generated names (the default; what SQL Server samples use). </summary>
    AsIs,

    /// <summary> <c>customer_item</c> becomes <c>CustomerItem</c>: split at underscores, first letter of each part upper-cased, the rest kept
    /// (so <c>CustomerItem</c> stays <c>CustomerItem</c> and <c>customer</c> becomes <c>Customer</c>). </summary>
    Pascal
}

public static class NameConverter
{
    public static string Apply(NamingStyle style, string name) => style == NamingStyle.Pascal ? ToPascal(name) : name;

    public static string ToPascal(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (string part in name.Split('_', StringSplitOptions.RemoveEmptyEntries))
        {
            sb.Append(char.ToUpperInvariant(part[0]));
            sb.Append(part, 1, part.Length - 1);
        }
        return sb.Length > 0 ? sb.ToString() : name;
    }
}
