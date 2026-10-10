namespace CodeGenNew.Core;

/// <summary> What kind of value a single-column primary key holds. </summary>
public enum KeyKind
{
    /// <summary> A whole number (int, bigint, smallint, tinyint). </summary>
    Int,

    /// <summary> A uniqueidentifier. </summary>
    Guid,

    /// <summary> A text key (char, varchar, nchar, nvarchar): a natural key such as a country, currency or state code. </summary>
    Text
}

/// <summary>
/// How a table's key travels through every layer, decided once so the repository, the API, the front ends and the drop-downs agree: the C# type, the TypeScript and Python types,
/// the route segment, and what an empty new row starts with. A template asks for these instead of testing <c>IsInt32Column</c> itself; adding a kind here is how a new key type reaches every stack.
/// </summary>
public sealed record KeyType(KeyKind Kind, string CSharp, string TypeScript, string Python, string RouteConstraint, string TypeScriptEmpty, bool ChosenByPerson)
{
    /// <summary> True for a key of any kind other than a whole number, which the older templates (written for <c>{id:int}</c>) do not handle. </summary>
    public bool IsNotInt => Kind != KeyKind.Int;

    /// <summary> The route segment for this key: <c>{id:int}</c>, <c>{id:guid}</c> or <c>{id}</c> (a text key has no constraint). </summary>
    public string RouteSegment(string name = "id") => "{" + name + RouteConstraint + "}";

    /// <summary> The base class of a generated repository: <c>GenericRepo&lt;Entity&gt;</c> for an int key (the original form), <c>GenericRepo&lt;Entity, string&gt;</c> for any other. </summary>
    public string RepoBase(string entity) => CSharp == "int" ? $"GenericRepo<{entity}>" : $"GenericRepo<{entity}, {CSharp}>";

    /// <summary> The key type of one primary key column; null for a column that cannot be a key the generated stacks understand (a date, a decimal, text/ntext). </summary>
    public static KeyType? Of(ColumnModel column)
    {
        if (column.IsGuidColumn)
            return new KeyType(KeyKind.Guid, "Guid", "string", "UUID", ":guid", "'00000000-0000-0000-0000-000000000000'", false);
        if (column.IsIntegerColumn)
        {
            string csharp = column.CSharpBase();
            return new KeyType(KeyKind.Int, csharp, "number", "int", csharp == "long" ? ":long" : ":int", "0", !column.IsIdentity);
        }
        if (column.IsStringColumn && !column.IsLargeTextColumn)
            return new KeyType(KeyKind.Text, "string", "string", "str", "", "''", true);
        return null;
    }

    /// <summary> The key type of a single-column foreign key, read from its referencing column among <paramref name="columns"/> (the columns of the table the foreign key is on). </summary>
    public static KeyType? OfForeignKey(IEnumerable<ColumnModel> columns, ForeignKeyModel foreignKey) =>
        foreignKey.ReferencingColumns.Count == 1 && columns.FirstOrDefault(c => c.Name.Equals(foreignKey.ReferencingColumns[0], StringComparison.OrdinalIgnoreCase)) is { } column
            ? Of(column)
            : null;

    /// <summary> The key type of a table with a single-column primary key; null for no key, a composite key, or a key of another type. </summary>
    public static KeyType? Of(TableModel table) => table.PrimaryKeyColumns.Count == 1 ? Of(table.PrimaryKeyColumns[0]) : null;

    /// <summary> The stacks whose templates read <see cref="KeyType"/>, so the tables with a uniqueidentifier or text key get their files and their menu entries. A stack is added here
    /// when its templates have been moved (spec item 103); until then it leaves those tables out. </summary>
    private static readonly string[] StacksThatHandleEveryKey = ["Api", "Angular", "React", "Blazor", "WinUI3"];

    /// <summary> True when every stack in <paramref name="stacks"/> handles every key type (and there is at least one). </summary>
    public static bool StackHandlesEveryKey(params string?[] stacks) =>
        stacks.Length > 0 && stacks.All(s => s is not null && StacksThatHandleEveryKey.Contains(s, StringComparer.OrdinalIgnoreCase));
}
