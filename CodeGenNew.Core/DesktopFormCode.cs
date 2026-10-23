using System.Data;
using System.Text;

namespace CodeGenNew.Core;

/// <summary> The C# of a desktop form's view model that depends only on the column and the project's rules: how a field is held, how it is filled from the row being edited, and how it is checked and
/// copied back into the row on Save. The WinUI3 and Uno dialogs (plain and master-detail) all write the same text from here, so the four templates cannot drift apart. The numbers are held as
/// doubles (NaN is blank) and the dates as <c>DateTimeOffset</c> (see <see cref="FormModel.ValueOf"/>). Every method returns whole lines ending in a line feed, indented as they sit in the view model. </summary>
public static class DesktopFormCode
{
    /// <summary> The type of the view-model property that holds the column's value. </summary>
    public static string FieldType(FormModel form, ColumnModel column)
    {
        var value = form.ValueOf(column);
        return value == ValueKind.Lookup ? form.ParentOf(column)!.KeyCs + "?"
            : value == ValueKind.Boolean ? "bool?"
            : value is ValueKind.Money or ValueKind.Decimal or ValueKind.WholeNumber ? "double"
            : TemplateHelpers.IsDate(column) ? "DateTimeOffset?"
            : "string?";
    }

    /// <summary> The check the project's validation rules ask of a text field: the shape of an email address, phone number, URL, postal code or social security number. A value still equal to the stored
    /// one is not held against the person (an old value that breaks the rule does not stop an edit of another field). Null when the column has no such rule. </summary>
    public static string? ShapeCheck(ProjectSettings project, TableModel table, ColumnModel column, string prop, string label, bool optional)
    {
        if (column.RuleOf(project, table.TableName) is not { Shape: not TextShape.None } rule)
            return null;
        string test = ColumnRules.CSharpTest(rule, prop);
        string refuse = "{ ErrorMessage = \"" + label + " is not " + ColumnRules.NounOf(rule) + ".\"; return false; }";
        string changed = "!string.Equals(entity." + prop + ", " + prop + ")";
        return optional ? "if (!string.IsNullOrWhiteSpace(" + prop + ") && " + changed + " && !(" + test + ")) " + refuse : "if (" + changed + " && !(" + test + ")) " + refuse;
    }

    /// <summary> The check of a date that is never later than today (a changed value only), or null when the column has no such rule. </summary>
    public static string? FutureCheck(ProjectSettings project, TableModel table, ColumnModel column, string prop, string label) =>
        column.RuleOf(project, table.TableName) is { NotFuture: true }
            ? "else if (" + prop + ".Value.Date > DateTime.Today && ((DateTime?)entity." + prop + ")?.Date != " + prop + ".Value.Date) { ErrorMessage = \"" + label + " must not be later than today.\"; return false; }"
            : null;

    /// <summary> For each date that must not be earlier than another (<see cref="DatePairs"/>): the earliest date the end's calendar offers, which follows the start, and the notice that tells the calendar when the start changes
    /// (four spaces in). Empty when the table has no pair. </summary>
    public static string PairProperties(FormModel form, ProjectSettings project)
    {
        var o = new StringBuilder();
        foreach (var pair in DatePairs.For(form.Table, project).Where(p => form.Columns.Contains(p.End) && form.Columns.Contains(p.Start) && form.ValueOf(p.End) == ValueKind.Date && form.ValueOf(p.Start) == ValueKind.Date))
        {
            o.Append('\n');
            o.Append($"    /// <summary> The earliest date the {pair.End.Name} calendar offers: the {pair.Start.Name} date (the calendar's own first date while there is none). </summary>\n");
            o.Append($"    public DateTimeOffset {pair.End.Name}MinDate => {pair.Start.Name}?.Date is {{ }} start ? new DateTimeOffset(start, TimeSpan.Zero) : new DateTimeOffset(1920, 1, 1, 0, 0, 0, TimeSpan.Zero);\n");
            o.Append($"    partial void On{pair.Start.Name}Changed(DateTimeOffset? value) => OnPropertyChanged(nameof({pair.End.Name}MinDate));\n");
        }
        return o.ToString();
    }

    /// <summary> The check of a date that must not be earlier than the date it follows (a changed value only), or null when the column ends no pair. </summary>
    public static string? PairCheck(FormModel form, ProjectSettings project, TableModel table, ColumnModel column, string prop)
    {
        if (DatePairs.EndingAt(table, project, column) is not { } pair || !form.Columns.Contains(pair.Start) || form.ValueOf(pair.Start) != ValueKind.Date)
            return null;
        string start = pair.Start.Name;
        return $"else if ({start} is not null && {prop}.Value.Date < {start}.Value.Date && (((DateTime?)entity.{prop})?.Date != {prop}.Value.Date || ((DateTime?)entity.{start})?.Date != {start}.Value.Date)) {{ ErrorMessage = \"{pair.Message}\"; return false; }}";
    }

    /// <summary> The line that fills the view-model property from <c>editing</c>, the row being edited (twelve spaces in). A foreign key is not set here: its drop-down is filled first. Empty for it. </summary>
    public static string LoadLines(FormModel form, ColumnModel column)
    {
        var value = form.ValueOf(column);
        string prop = column.Name;
        var o = new StringBuilder();
        void L(string s) => o.Append("            ").Append(s).Append('\n');
        switch (value)
        {
            case ValueKind.Lookup:
                break;
            case ValueKind.Money:
            case ValueKind.Decimal:
                L($"{prop} = " + (column.IsNullable ? $"editing.{prop}.HasValue ? (double)editing.{prop}.Value : double.NaN" : $"(double)editing.{prop}") + ";");
                break;
            case ValueKind.WholeNumber:
                L($"{prop} = editing.{prop}" + (column.IsNullable ? " ?? double.NaN" : "") + ";");
                break;
            case ValueKind.Boolean:
                L($"{prop} = editing.{prop};");
                break;
            case ValueKind.Date:
                L($"{prop} = " + (column.IsNullable ? $"editing.{prop}.HasValue ? new DateTimeOffset(editing.{prop}.Value) : null" : $"new DateTimeOffset(editing.{prop})") + ";");
                L($"_{char.ToLowerInvariant(prop[0]) + prop[1..]}Time = editing.{prop}" + (column.IsNullable ? "?.TimeOfDay ?? TimeSpan.Zero" : ".TimeOfDay") + ";");
                break;
            default:
                L($"{prop} = editing.{prop}" + (column.IsNullable ? "?.ToString()" : ".ToString()") + ";");
                break;
        }
        return o.ToString();
    }

    /// <summary> The view-model members of the guarded columns (four spaces in): for each, whether the signed-in person may change it (set from the API's guards) and the visibility of the guard's
    /// sentence, which shows when they may not. Empty when the table has no guarded column. </summary>
    public static string GuardProperties(FormModel form)
    {
        var o = new StringBuilder();
        foreach (var c in form.Columns.Where(c => form.GuardOf(c) is not null))
        {
            o.Append('\n');
            o.Append($"    /// <summary> The signed-in person may change {c.Name} (a guarded column, read from the API's guards); otherwise its field is disabled and the note shows. </summary>\n");
            o.Append($"    [NotifyPropertyChangedFor(nameof({c.Name}NoteVisibility))]\n");
            o.Append("    [ObservableProperty]\n");
            o.Append($"    private bool _{char.ToLowerInvariant(c.Name[0])}{c.Name[1..]}Allowed;\n");
            o.Append($"    public Visibility {c.Name}NoteVisibility => {c.Name}Allowed ? Visibility.Collapsed : Visibility.Visible;\n");
        }
        return o.ToString();
    }

    /// <summary> The lines of the view model's load that ask the API which guarded columns the signed-in person may change (eight spaces in); until it answers, and when it cannot, the fields stay read-only.
    /// <paramref name="clientProperty"/> is the table's property of <c>ApiClients</c>. Empty when the table has no guarded column. </summary>
    public static string GuardLoadLines(FormModel form, string clientProperty)
    {
        if (form.Guards.Count == 0)
            return "";
        var o = new StringBuilder();
        o.Append("        // which guarded columns the signed-in person may change (GET guards); until it answers, and when it cannot, they are read-only\n");
        o.Append("        try\n");
        o.Append("        {\n");
        o.Append($"            var guards = await _api.{clientProperty}.GetGuardsAsync();\n");
        foreach (var c in form.Columns.Where(c => form.GuardOf(c) is not null))
            o.Append($"            {c.Name}Allowed = guards.GetValueOrDefault(\"{form.GuardOf(c)!.Json}\");\n");
        o.Append("        }\n");
        o.Append("        catch (Exception ex) when (ex is ApiException or HttpRequestException)\n");
        o.Append("        {\n");
        o.Append("        }\n");
        return o.ToString();
    }

    /// <summary> The lines of <c>SaveAsync</c> that check one field and copy it into <c>entity</c> (eight spaces in): a missing required value, a number that is not whole or out of range, a text
    /// that breaks a shape rule, a date after today, each setting <c>ErrorMessage</c> and returning false. </summary>
    public static string SaveLines(FormModel form, ProjectSettings project, ColumnModel column)
    {
        var value = form.ValueOf(column);
        string prop = column.Name;
        string label = Labels.Column(column.Name);
        var table = form.Table;
        var o = new StringBuilder();
        void L(string s) => o.Append("        ").Append(s).Append('\n');
        // the branch taken for a blank value: refused when the column is NOT NULL, else stored as null
        void Blank() => L(column.IsNullable ? "    entity." + prop + " = null;" : "    ErrorMessage = \"" + label + " is required.\"; return false;");

        switch (value)
        {
            case ValueKind.Lookup:
                if (!column.IsNullable)
                {
                    L($"if ({prop} is null) {{ ErrorMessage = \"{label} is required.\"; return false; }}");
                    L($"entity.{prop} = {prop}" + (form.ParentOf(column)!.KeyCs == "string" ? "" : ".Value") + ";");
                }
                else
                    L($"entity.{prop} = {prop};");
                break;
            case ValueKind.Money:
                L($"if (double.IsNaN({prop}))");
                L("{");
                Blank();
                L("}");
                L($"else {{ entity.{prop} = Math.Round((decimal){prop}, {NumericClassifier.CurrencyDigits(column)}); }}");
                break;
            case ValueKind.Decimal:
            {
                var (decMin, decMax) = NumericClassifier.DecimalRange(column);
                string decValue = column.SqlType == SqlDbType.Decimal ? $"Math.Round((decimal){prop}, {NumericClassifier.DecimalPlaces(column)})" : column.SqlType == SqlDbType.Real ? $"(float){prop}" : prop;
                L($"if (double.IsNaN({prop}))");
                L("{");
                Blank();
                L("}");
                L($"else if ({prop} < {TemplateHelpers.Inv(decMin)} || {prop} > {TemplateHelpers.Inv(decMax)}) {{ ErrorMessage = \"{label} must be between {TemplateHelpers.Inv(decMin)} and {TemplateHelpers.Inv(decMax)}.\"; return false; }}");
                L($"else {{ entity.{prop} = {decValue}; }}");
                break;
            }
            case ValueKind.WholeNumber:
            {
                var range = project.RangeFor(column)!.Value;
                string intType = NumericClassifier.NumericCSharpType(column);
                L($"if (double.IsNaN({prop}))");
                L("{");
                Blank();
                L("}");
                L($"else if ({prop} != Math.Floor({prop})) {{ ErrorMessage = \"{label} must be a whole number.\"; return false; }}");
                L($"else if ({prop} < {range.Min} || {prop} > {range.Max}) {{ ErrorMessage = \"{label} must be between {range.Min} and {range.Max}.\"; return false; }}");
                L($"else {{ entity.{prop} = ({intType}){prop}; }}");
                break;
            }
            case ValueKind.Boolean:
                L($"entity.{prop} = {prop}" + (column.IsNullable ? "" : " ?? false") + ";");
                break;
            case ValueKind.Date:
            {
                L($"if ({prop} is null)");
                L("{");
                Blank();
                L("}");
                if (FutureCheck(project, table, column, prop, label) is { } futureCheck)
                    L(futureCheck);
                if (PairCheck(form, project, table, column, prop) is { } pairCheck)
                    L(pairCheck);
                L($"else {{ entity.{prop} = {prop}.Value.Date + _{char.ToLowerInvariant(prop[0]) + prop[1..]}Time; }}");
                break;
            }
            case ValueKind.Guid:
                L($"if (string.IsNullOrWhiteSpace({prop}))");
                L("{");
                Blank();
                L("}");
                L($"else if (!Guid.TryParse({prop}, out var {prop}Value)) {{ ErrorMessage = \"{label} is not a valid value.\"; return false; }}");
                L($"else {{ entity.{prop} = {prop}Value; }}");
                break;
            case ValueKind.Number:
            {
                string numType = NumericClassifier.NumericCSharpType(column);
                L($"if (string.IsNullOrWhiteSpace({prop}))");
                L("{");
                Blank();
                L("}");
                L($"else if (!{numType}.TryParse({prop}, out var {prop}Value)) {{ ErrorMessage = \"{label} is not a valid number.\"; return false; }}");
                L($"else {{ entity.{prop} = {prop}Value; }}");
                break;
            }
            default:   // plain text
                if (!column.IsNullable)
                {
                    L($"if (string.IsNullOrWhiteSpace({prop})) {{ ErrorMessage = \"{label} is required.\"; return false; }}");
                    if (ShapeCheck(project, table, column, prop, label, false) is { } requiredShape)
                        L(requiredShape);
                    L($"entity.{prop} = {prop};");
                }
                else
                {
                    if (ShapeCheck(project, table, column, prop, label, true) is { } optionalShape)
                        L(optionalShape);
                    L($"entity.{prop} = string.IsNullOrWhiteSpace({prop}) ? null : {prop};");
                }
                break;
        }
        return o.ToString();
    }
}
