namespace CodeGenNew.Core;

/// <summary> The kind of control a column gets in the filter row of an OData screen. </summary>
public enum ODataFilterKind
{
    /// <summary> A text box: the rows whose text contains it, ignoring case. </summary>
    Text,
    /// <summary> Two boxes, from and to. </summary>
    Number,
    /// <summary> Two date boxes, from and to (the last day is included). </summary>
    Date,
    /// <summary> Any, yes or no. </summary>
    Boolean,
    /// <summary> A foreign key: a drop-down of the parent's rows. </summary>
    Parent,
    /// <summary> A column whose values the database lists: a drop-down of them. </summary>
    Choice
}

/// <summary> One control of the filter row: the column's header name (what the grid sorts by), its JSON name, the kind, whether the column can be empty (it then also gets an "empty" check box), the parent
/// list a drop-down reads and how a chosen value is written in <c>$filter</c> (<c>number</c>, <c>string</c> or <c>guid</c>). </summary>
public sealed record ODataFilterField(string Name, string Property, ODataFilterKind Kind, bool Nullable, string? Literal, FormParent? Parent, IReadOnlyList<string> Choices);

/// <summary> The one rule for which grid column gets which filter control, so every web stack's OData screen filters the same way. A column with no control (long text, binary, a guid or other value that is
/// not text) has none: <see cref="For"/> answers null in its place. </summary>
public static class ODataFilterFields
{
    /// <summary> One entry for each grid column, in the grid's order; null where the column has no control. </summary>
    public static IReadOnlyList<ODataFilterField?> For(FormModel form) => form.GridColumns.Select(c => Of(form, c)).ToList();

    public static ODataFilterField? Of(FormModel form, ColumnModel column)
    {
        string name = column.Name, property = JsonNames.Camel(column.Name);
        ODataFilterField Field(ODataFilterKind kind, string? literal = null, FormParent? parent = null, IReadOnlyList<string>? choices = null) =>
            new(name, property, kind, column.IsNullable, literal, parent, choices ?? []);

        var cell = form.CellOf(column);
        if (cell == CellKind.Lookup)
        {
            var parent = form.ParentOf(column);
            return parent is null ? null : Field(ODataFilterKind.Parent, parent.KeyType?.Kind == KeyKind.Int ? "number" : parent.KeyType?.Kind == KeyKind.Guid ? "guid" : "string", parent);
        }
        if (column.Choices is { Count: > 0 } choices)
            return Field(ODataFilterKind.Choice, "string", choices: choices);
        return cell switch
        {
            CellKind.Boolean => Field(ODataFilterKind.Boolean),
            CellKind.Date => Field(ODataFilterKind.Date),
            CellKind.Money or CellKind.Decimal => Field(ODataFilterKind.Number),
            _ when column.IsIntegerColumn => Field(ODataFilterKind.Number),
            _ when column.IsStringColumn && !column.IsLongTextColumn => Field(ODataFilterKind.Text),
            _ => null
        };
    }
}
