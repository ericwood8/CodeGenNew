using CodeGenNew.Core;

namespace CodeGenNew.SchemaIntrospection;

/// <summary> The key of a table whose database declares no primary key: the project's KeyColumns entry, else the unique index with the fewest columns that has only NOT NULL columns, else the
/// table's identity column. <see cref="Columns"/> is the table's columns with the key marked; null when none was found; <see cref="Problem"/> says why a KeyColumns entry was not usable (also when another key was found instead). </summary>
public sealed record KeyPick(List<RawColumn>? Columns, string? Source, string? Problem)
{
    public static KeyPick None(string? problem = null) => new(null, null, problem);
}

public static class KeyChooser
{
    /// <param name="declared"> The KeyColumns entry for this table (column names in order), or null. </param>
    /// <param name="named"> The generated name of a database name, so an entry may use either. </param>
    public static KeyPick Choose(List<RawColumn> columns, IReadOnlyList<IndexRow> indexes, string[]? declared, Func<string, string> named)
    {
        RawColumn? Find(string name) => columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(name) || named(c.Name).EqualsIgnoreCase(name));
        List<RawColumn> Marked(IEnumerable<RawColumn> key) => columns.Select(c => key.Contains(c) ? c with { IsPrimaryKey = true } : c).ToList();

        string? problem = null;
        if (declared is { Length: > 0 })
        {
            var found = declared.Select(Find).ToList();
            if (found.Any(c => c is null))
                problem = $"KeyColumns names {declared[found.IndexOf(null)]}, which is not a column of the table";
            else if (found.FirstOrDefault(c => c!.IsNullable) is { } nullable)
                problem = $"KeyColumns names {nullable.Name}, which allows NULL, so it cannot be a key";
            else
                return new KeyPick(Marked(found!), "the KeyColumns setting", null);
        }

        foreach (var index in indexes.Where(i => i.IsUnique && i.Columns.Count > 0).OrderBy(i => i.Columns.Count).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
        {
            var found = index.Columns.Select(Find).ToList();
            if (found.All(c => c is not null && !c.IsNullable))
                return new KeyPick(Marked(found!), $"the unique index {index.Name}", problem);
        }

        // the database hands out the values, so they do not repeat; nothing enforces it, which the run note says
        if (columns.Where(c => c.IsIdentity && !c.IsNullable).ToList() is [var identity])
            return new KeyPick(Marked([identity]), "the identity column", problem);

        return KeyPick.None(problem);
    }
}
