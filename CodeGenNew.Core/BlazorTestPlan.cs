namespace CodeGenNew.Core;

/// <summary> What the bUnit tests of one Blazor page (<c>BLZ_Test</c>) look for and with which values: the row the fake API returns (<see cref="GridTestRow"/>), the key as it goes into a route, the role the test signs in as, the child grids
/// (read-only and editable) of a master-detail page, the header that sorts and the box that searches. The template only writes the text. </summary>
public sealed class BlazorTestPlan
{
    private BlazorTestPlan(TableModel model, ProjectSettings project)
    {
        Form = FormModel.For(model, project);
        Chosen = ChosenGridColumns.For(Form);
        Row = GridTestRow.For(Form, Chosen);
        Type = BlazorNames.TypeName(model.TableName);
        Title = Form.Title;
        ApiRoute = BlazorNames.ApiRoute(model.TableName);
        ODataSet = "/odata/" + ODataNames.SetName(model.TableName);
        KeyText = Row is null ? "1" : Uri.EscapeDataString(Row.KeyText);

        IReadOnlyList<string> readRoles = project.Auth ? WriteRolePlan.ReadFor(project, model) : [];
        IReadOnlyList<string> writeRoles = project.Auth ? WriteRolePlan.For(project, model) : [];
        Role = writeRoles.Count > 0 ? WriteRoleSpec.AllowedRole(writeRoles) : readRoles.Count > 0 ? readRoles[0] : "Tester";
        SignIn = project.Auth ? $"        SignIn(\"{Role.Replace("\\", "\\\\").Replace("\"", "\\\"")}\");\n" : "";
        ParentKeyJson = Form.TextKey && Row is not null ? "\"" + Row.KeyText + "\"" : "1";

        // the child tables a master-detail page shows under the form: a read-only grid each, or (EditableChildren) rows in boxes of the form
        var childGrids = ChildGrid.ForWeb(model, project)
            .Where(g => DatabaseModel.IsDetailMaster(model, project) && g.KeyColumn is not null && g.KeyType is not null && !g.IsNameActive)
            .ToList();
        Children = childGrids.Where(g => !g.IsEditable).ToList();
        Editable = Form.IsNameActive ? [] : childGrids.Where(g => g.IsEditable).ToList();   // a name / active page shows its child tables read-only

        SortName = Chosen.Initial.FirstOrDefault(c => c.Sortable)?.Name;

        // the OData page's own grid: the header it sorts by (a column of the table itself, not a parent's display column), and one filter row control of each kind a test can type into
        ODataSort = Chosen.Initial.FirstOrDefault(c => c.Sortable && c.Filter?.Kind is not (ODataFilterKind.Parent or ODataFilterKind.Choice));
        ODataText = Chosen.Initial.FirstOrDefault(c => c.Filter?.Kind == ODataFilterKind.Text);
        ODataNumber = Chosen.Initial.FirstOrDefault(c => c.Filter?.Kind == ODataFilterKind.Number);
        ODataBoolean = Chosen.Initial.FirstOrDefault(c => c.Filter?.Kind == ODataFilterKind.Boolean);
        ODataDate = Chosen.Initial.FirstOrDefault(c => c.Filter?.Kind == ODataFilterKind.Date);
        ODataChoice = Chosen.Initial.FirstOrDefault(c => c.Filter?.Kind == ODataFilterKind.Choice && c.Filter.Choices.Any(IsPlainChoice));
        ODataChoiceValue = ODataChoice?.Filter!.Choices.First(IsPlainChoice);
        // a drop-down of a number-keyed parent's lookup list; a parent that is searched as the person types has no list to pick from
        ODataParent = Chosen.Initial.FirstOrDefault(c => c.Filter is { Kind: ODataFilterKind.Parent, Literal: "number", Parent: { UsesLookupEndpoint: true, TypeAhead: false } });
        Search = Form.SearchFields.FirstOrDefault(f => f.Column is not null);
    }

    /// <summary> The rows on a page of the grid, REST or OData (BLZ_Page asks for this many). </summary>
    public const int PageSize = 20;

    public static BlazorTestPlan For(TableModel model, ProjectSettings project) => new(model, project);

    public FormModel Form { get; }
    public ChosenGridColumns Chosen { get; }

    /// <summary> The row the fake API answers with; null for a table with no column to recognise a row by (it has the heading test only). </summary>
    public GridTestRow? Row { get; }

    public string Type { get; }
    public string Title { get; }
    public string ApiRoute { get; }
    public string ODataSet { get; }

    /// <summary> The key of the row, as it goes into a route. </summary>
    public string KeyText { get; }

    /// <summary> The role the test signs in as (with <c>Auth=true</c>): one that may write the table, else read it. </summary>
    public string Role { get; }

    /// <summary> The line of the test's <c>Open</c> that signs in, or blank without <c>Auth</c>. </summary>
    public string SignIn { get; }

    public string ParentKeyJson { get; }
    public IReadOnlyList<ChildGrid> Children { get; }
    public IReadOnlyList<ChildGrid> Editable { get; }

    /// <summary> The first sortable column the grid starts with, or null. </summary>
    public string? SortName { get; }

    /// <summary> The first search box that searches a column, or null. </summary>
    public SearchField? Search { get; }

    /// <summary> The OData page's sortable header whose sort path is the column's own property, the text, number and yes-or-no filter row columns; null when the grid has none of that kind. </summary>
    public ChosenColumn? ODataSort { get; }
    public ChosenColumn? ODataText { get; }
    public ChosenColumn? ODataNumber { get; }
    public ChosenColumn? ODataBoolean { get; }
    public ChosenColumn? ODataDate { get; }
    public ChosenColumn? ODataChoice { get; }

    /// <summary> The choice the test picks: one that needs no escaping in HTML or in <c>$filter</c>. </summary>
    public string? ODataChoiceValue { get; }

    public ChosenColumn? ODataParent { get; }

    private static bool IsPlainChoice(string choice) => choice.Length > 0 && choice.All(ch => char.IsLetterOrDigit(ch) || ch is ' ' or '_' or '-');
}
