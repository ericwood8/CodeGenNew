namespace CodeGenNew.Core;

/// <summary> A parent table the form's drop-downs read: the foreign key, the column that holds it, what the parent's rows are keyed and named by, and how the screen reads them. The screens of
/// every stack take these facts from here and only add their own names (a service, an api module, a view-model property). </summary>
public sealed class FormParent
{
    internal FormParent(FormModel form, ForeignKeyModel foreignKey, ProjectSettings project)
    {
        ForeignKey = foreignKey;
        IsSelf = form.IsSelfReference(foreignKey);
        Column = form.Table.Columns.First(c => c.Name.Equals(foreignKey.ReferencingColumns[0], StringComparison.OrdinalIgnoreCase));
        KeyType = form.Table.ForeignKeyType(foreignKey);
        UsesLookupEndpoint = !Column.IsGuidColumn && foreignKey.ReferencedColumns.Count == 1 && !project.NoApiFor(foreignKey.ReferencedTable, foreignKey.ReferencedLookupShape);
        BaseName = ScreenNames.BaseName(foreignKey.ReferencedTable);
        ModelFile = project.ModelFileOf(foreignKey.ReferencedTable, Stem);
    }

    public ForeignKeyModel ForeignKey { get; }

    /// <summary> The parent table. </summary>
    public string Table => ForeignKey.ReferencedTable;

    /// <summary> The parent's name without an E_ or SY_ prefix; the web stacks name its type, service and api module from it. </summary>
    public string BaseName { get; }

    /// <summary> The base name with a lower-case first letter (<c>timeSheet</c>): the web stacks name the parent's list, service and api module from it. </summary>
    public string Variable => TemplateHelpers.LowerFirst(BaseName);

    /// <summary> The base name in lower case, which the web stacks name the parent's files from. </summary>
    public string Stem => BaseName.ToLowerInvariant();

    /// <summary> The name of the list of the parent's rows a web screen holds (<c>timeSheets</c>). </summary>
    public string List => Variable.Pluralize();

    /// <summary> The name of the method that turns a key into the parent's display text (<c>timeSheetName</c>). </summary>
    public string NameMethod => Variable + "Name";

    /// <summary> The parent is the table being edited (a manager is an employee). </summary>
    public bool IsSelf { get; }

    /// <summary> The form column that holds the foreign key (the first one, when two columns point at the same parent). </summary>
    public ColumnModel Column { get; }

    /// <summary> How the key travels (a number, a uniqueidentifier or text); null when the column cannot be a key. </summary>
    public KeyType? KeyType { get; }

    /// <summary> The TypeScript type of the key (<c>number</c> or <c>string</c>). </summary>
    public string KeyTs => KeyType?.TypeScript ?? "number";

    /// <summary> The C# type of the key (<c>int</c>, <c>long</c>, <c>Guid</c> or <c>string</c>); the key must be usable, which the form's drop-downs guarantee. </summary>
    public string KeyCs => KeyType!.CSharp;

    /// <summary> The type of an item of the lookup list the web client reads: <c>Lookup</c>, or <c>Lookup&lt;string&gt;</c> for another key type. </summary>
    public string LookupType => KeyTs == "number" ? "Lookup" : $"Lookup<{KeyTs}>";

    /// <summary> The client call that fills the drop-down: <c>getLookup</c> (id and name) or <c>getAll</c> (every column of every row). </summary>
    public string LoadMethod => UsesLookupEndpoint ? "getLookup" : "getAll";

    /// <summary> The TypeScript type of an item the drop-down holds: <see cref="LookupType"/> or the parent's model. </summary>
    public string ItemType => UsesLookupEndpoint ? LookupType : BaseName;

    /// <summary> The parent's key column. </summary>
    public string KeyName => ForeignKey.ReferencedColumns[0];

    /// <summary> The parent's column the drop-down shows. </summary>
    public string DisplayName => ForeignKey.ReferencedDisplayColumns[0];

    /// <summary> The drop-down reads the parent's lookup list (<c>GET &lt;route&gt;/lookup</c>: id, name and active flag) instead of every column of every row: a parent with a single-column key that is not a
    /// uniqueidentifier and has an API. </summary>
    public bool UsesLookupEndpoint { get; }

    /// <summary> The file the parent's model is in, without the extension (<c>ModelFiles</c> in the project, else the lower-case base name). </summary>
    public string ModelFile { get; }

    /// <summary> The JSON name of the key the web clients read: <c>id</c> from the lookup list, else the key column camel-cased. </summary>
    public string KeyJson => UsesLookupEndpoint ? "id" : JsonNames.Camel(KeyName);

    /// <summary> The JSON name of the display column the web clients read: <c>name</c> from the lookup list, else the column camel-cased. </summary>
    public string DisplayJson => UsesLookupEndpoint ? "name" : JsonNames.Camel(DisplayName);
}
