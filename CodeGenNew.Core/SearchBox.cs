namespace CodeGenNew.Core;

/// <summary> One place the search box looks: <see cref="Path"/> is the C# property path from the row (<c>Notes</c>, <c>Employee.Name</c>) and <see cref="Guards"/> are the paths that must not be null
/// before the text is compared. </summary>
public sealed record SearchBoxTerm(string Path, IReadOnlyList<string> Guards)
{
    /// <summary> The condition on the row <paramref name="row"/> for the lower-cased text <paramref name="text"/>: <c>t.Employee != null &amp;&amp; t.Employee.Name.ToLower().Contains(term)</c>. </summary>
    public string Condition(string row, string text) =>
        string.Join(" && ", Guards.Select(g => $"{row}.{g} != null").Append($"{row}.{Path}.ToLower().Contains({text})"));
}

/// <summary> What the one search box of <c>ListProtocol=Search</c> matches: by default the table's searchable text columns and the display column of each parent the entity can reach, or the columns the
/// project lists (<c>SearchColumns</c>). A text that is contained, in any of them, with the case ignored, keeps the row. </summary>
public static class SearchBox
{
    /// <summary> The most parents the default looks into: each is a join in the query. </summary>
    public const int MaxParents = 4;

    public static List<SearchBoxTerm> For(TableModel model, ProjectSettings project, IReadOnlyList<EntityNavigation> navigations)
    {
        var listed = project.SearchColumnsOf(model.TableName);
        return listed is null ? Default(model, navigations) : Listed(model, listed, navigations);
    }

    private static List<SearchBoxTerm> Default(TableModel model, IReadOnlyList<EntityNavigation> navigations)
    {
        var terms = model.SearchableColumns.Concat(model.SearchKeyColumns.Where(c => c.IsStringColumn)).OrderBy(c => c.OrdinalPosition).Where(c => c.DbEnumType is null).Select(Own).ToList();
        foreach (var navigation in navigations.Where(n => n.ForeignKey.ReferencedDisplayColumns.Count > 0).Take(MaxParents))
            terms.Add(Parent(navigation, navigation.ForeignKey.ReferencedDisplayColumns[0], false));
        return terms;
    }

    private static List<SearchBoxTerm> Listed(TableModel model, List<string> entries, IReadOnlyList<EntityNavigation> navigations)
    {
        var terms = new List<SearchBoxTerm>();
        foreach (string entry in entries)
        {
            int dot = entry.IndexOf('.');
            if (dot < 0)
            {
                var column = model.Columns.FirstOrDefault(c => c.IsStringColumn && c.DbEnumType is null && c.Name.EqualsIgnoreCase(entry));
                if (column is not null)
                    terms.Add(Own(column));
            }
            else
            {
                var navigation = navigations.FirstOrDefault(n => n.Role.EqualsIgnoreCase(entry[..dot]));
                if (navigation is not null && entry[(dot + 1)..].Trim() is { Length: > 0 } parentColumn)
                    terms.Add(Parent(navigation, parentColumn, true));
            }
        }
        return terms;
    }

    private static SearchBoxTerm Own(ColumnModel column) => new(column.CSharpName(), column.IsNullable ? [column.CSharpName()] : []);

    private static SearchBoxTerm Parent(EntityNavigation navigation, string column, bool guardColumn) =>
        new(navigation.Role + "." + column, guardColumn ? [navigation.Role, navigation.Role + "." + column] : [navigation.Role]);
}
