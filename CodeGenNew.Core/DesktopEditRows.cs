using System.Text;

namespace CodeGenNew.Core;

/// <summary> Child rows edited inside the parent's dialog on the desktop stacks (<c>EditableChildren</c>): for each such child one row class (the values of one line, with its check and its copy to and from the
/// child's class), one holder of the drop-down lists and one panel of controls built in code, so a number, a date, a flag, a list of choices and a parent's name behave as in the dialog's own fields. WinUI3 and Uno
/// write the same text from here; they differ only in how the view model reads the lists and saves the rows. </summary>
public static class DesktopEditRows
{
    /// <summary> The children of <paramref name="table"/> a desktop dialog edits in place: the web rule (<see cref="EditableChild"/>) and columns the desktop controls can hold. A name / active parent shows its children read-only. </summary>
    public static bool Applies(ChildGrid grid, TableModel parent) =>
        grid.IsEditable && grid.KeyName is not null && !grid.IsNameActive && !TableModel.HasNameActiveShape(parent.Columns)
        && grid.EditColumns.Count > 0 && grid.EditColumns.All(c => Kind(grid, c) is not null);

    private enum Cell { Lookup, Choices, Boolean, Date, Money, Decimal, Whole, Guid, Text }

    private static Cell? Kind(ChildGrid grid, ColumnModel c)
    {
        if (grid.EditLookupOf(c) is not null)
            return grid.Lookups.Any(l => l.ColumnName.EqualsIgnoreCase(c.Name) && l.ColumnKeyType is not null) ? Cell.Lookup : null;
        if (c.HasChoices) return Cell.Choices;
        if (grid.IsMoneyInput(c)) return Cell.Money;
        if (FormModel.IsDecimalBox(c)) return Cell.Decimal;
        if (FormModel.IsWholeNumberBox(c)) return Cell.Whole;
        if (c.IsBooleanColumn) return Cell.Boolean;
        if (TemplateHelpers.IsDate(c)) return Cell.Date;
        if (c.IsGuidColumn) return Cell.Guid;
        if (TemplateHelpers.IsNumber(c) || !TemplateHelpers.IsEditableType(c)) return null;
        return Cell.Text;
    }

    public static string RowClass(string parent, string member) => parent + member + "EditRow";
    public static string LookupsClass(string parent, string member) => parent + member + "EditLookups";
    public static string PanelClass(string parent, string member) => parent + member + "EditPanel";
    public static string OptionClass(string parent) => parent + "EditOption";

    /// <summary> The statements of the view model's <c>CheckEditRows</c> body: the first line that is not valid stops the save with its message. </summary>
    public static string CheckLines(IEnumerable<(string Var, string Title)> children)
    {
        var o = new StringBuilder();
        foreach (var (var, title) in children)
        {
            o.Append($"        for (int i = 0; i < {var}Edit.Count; i++)\n");
            o.Append($"            if ({var}Edit[i].Check() is {{ }} error{var})\n");
            o.Append($"                return \"{title}, row \" + (i + 1) + \": \" + error{var};\n");
        }
        return o.ToString();
    }

    /// <summary> The file of the row, lookups and panel classes of every editable child of <paramref name="parent"/>. <paramref name="entityOf"/> names the class of a child's row in the stack (the EF entity or the API model);
    /// <paramref name="usings"/> are whole <c>using</c> lines for it. </summary>
    public static string File(string viewNamespace, string parent, string usings, ProjectSettings project, IReadOnlyList<(DesktopChild Child, ChildGrid Grid)> children, Func<DesktopChild, string> entityOf)
    {
        var o = new StringBuilder();
        void L(string s = "") => o.Append(s).Append('\n');
        L("using System;");
        L("using System.Collections.Generic;");
        L("using System.Collections.ObjectModel;");
        L("using System.Linq;");
        L("using System.Threading.Tasks;");
        L("using Microsoft.UI.Xaml;");
        L("using Microsoft.UI.Xaml.Automation;");
        L("using Microsoft.UI.Xaml.Controls;");
        o.Append(usings);
        L();
        L($"namespace {viewNamespace};");
        L();
        L($"/// <summary> One entry of a drop-down of a {parent} dialog's editable rows: the id of the parent row and the text to show for it. </summary>");
        L($"public sealed class {OptionClass(parent)}(object? id, string text)");
        L("{");
        L("    public object? Id { get; } = id;");
        L("    public string Text { get; } = text;");
        L("    public override string ToString() => Text;");
        L("}");
        foreach (var (child, grid) in children)
        {
            string row = RowClass(parent, child.Member), option = OptionClass(parent), entity = entityOf(child);
            var columns = grid.EditColumns;
            string P(ColumnModel c) => c.CSharpName();
            string Label(ColumnModel c) => Labels.Field(c).Replace("\"", "'");
            string Time(ColumnModel c) => "_" + TemplateHelpers.LowerFirst(P(c)) + "Time";

            L();
            L($"/// <summary> One line of the {child.Title} rows: what the boxes hold, checked and copied into the child's class on Save. </summary>");
            L($"public sealed class {row}");
            L("{");
            L($"    /// <summary> The key of the stored row; the default for a line that was just added. </summary>");
            L($"    public {grid.KeyColumn!.CSharpBase()} RowKey {{ get; set; }}");
            foreach (var c in columns)
            {
                string p = P(c);
                switch (Kind(grid, c)!.Value)
                {
                    case Cell.Lookup:
                        L($"    public {c.CSharpBase()}? {p} {{ get; set; }}");
                        L($"    public string {p}Text {{ get; set; }} = \"\";");
                        break;
                    case Cell.Choices:
                    case Cell.Guid:
                    case Cell.Text:
                        L($"    public string {p} {{ get; set; }} = \"\";");
                        break;
                    case Cell.Boolean:
                        bool on = !c.IsNullable && c.DatabaseDefaultSql is not null && c.DatabaseDefaultSql.Trim('(', ')', ' ') == "1";
                        L(c.IsNullable ? $"    public bool? {p} {{ get; set; }}" : $"    public bool {p} {{ get; set; }}{(on ? " = true" : "")};");
                        break;
                    case Cell.Date:
                        L($"    public DateTimeOffset? {p} {{ get; set; }}{(c.IsNullable ? "" : " = new DateTimeOffset(DateTime.Today)")};");
                        L($"    private TimeSpan {Time(c)};");
                        break;
                    default:
                        L($"    public double {p} {{ get; set; }} = double.NaN;");
                        break;
                }
            }
            L();
            L($"    public static {row} From({entity} e)");
            L("    {");
            L($"        var row = new {row} {{ RowKey = e.{grid.KeyName} }};");
            foreach (var c in columns)
            {
                string p = P(c);
                bool nullable = c.IsNullable;
                switch (Kind(grid, c)!.Value)
                {
                    case Cell.Lookup:
                        L($"        row.{p} = e.{p};");
                        break;
                    case Cell.Choices:
                    case Cell.Text:
                        L($"        row.{p} = e.{p} ?? \"\";");
                        break;
                    case Cell.Guid:
                        L($"        row.{p} = e.{p}{(nullable ? "?" : "")}.ToString() ?? \"\";");
                        break;
                    case Cell.Boolean:
                        L($"        row.{p} = e.{p};");
                        break;
                    case Cell.Date:
                        string t = c.CSharpBase();
                        string source = nullable ? p + "Value" : "e." + p;
                        string day = t == "DateOnly" ? $"{source}.ToDateTime(TimeOnly.MinValue)" : t == "DateTimeOffset" ? $"{source}.DateTime.Date" : $"{source}.Date";
                        string time = t == "DateOnly" ? "" : $" row.{Time(c)} = {source}.TimeOfDay;";
                        if (nullable)
                            L($"        if (e.{p} is {{ }} {p}Value) {{ row.{p} = new DateTimeOffset({day});{time} }} else {{ row.{p} = null; }}");
                        else
                            L($"        row.{p} = new DateTimeOffset({day});{time}");
                        break;
                    default:
                        L(nullable ? $"        row.{p} = e.{p} is {{ }} {p}Value ? (double){p}Value : double.NaN;" : $"        row.{p} = (double)e.{p};");
                        break;
                }
            }
            L("        return row;");
            L("    }");
            L();
            L("    /// <summary> The first thing wrong with the line, or null when it can be saved. </summary>");
            L("    public string? Check()");
            L("    {");
            foreach (var c in columns)
            {
                string p = P(c), label = Label(c);
                switch (Kind(grid, c)!.Value)
                {
                    case Cell.Lookup:
                    case Cell.Date:
                        if (!c.IsNullable)
                            L($"        if ({p} is null) return \"{label} is required.\";");
                        break;
                    case Cell.Choices:
                        if (!c.IsNullable)
                            L($"        if ({p}.Length == 0) return \"{label} is required.\";");
                        break;
                    case Cell.Text:
                        if (!c.IsNullable)
                            L($"        if (string.IsNullOrWhiteSpace({p})) return \"{label} is required.\";");
                        if (c.CharacterLength is > 0 and < 4000)
                            L($"        if ({p}.Length > {c.CharacterLength}) return \"{label} is longer than {c.CharacterLength} characters.\";");
                        break;
                    case Cell.Guid:
                        L(c.IsNullable ? $"        if (!string.IsNullOrWhiteSpace({p}) && !Guid.TryParse({p}, out _)) return \"{label} is not a valid value.\";" : $"        if (!Guid.TryParse({p}, out _)) return \"{label} is not a valid value.\";");
                        break;
                    case Cell.Money:
                        if (!c.IsNullable)
                            L($"        if (double.IsNaN({p})) return \"{label} is required.\";");
                        break;
                    case Cell.Decimal:
                    {
                        var (min, max) = NumericClassifier.DecimalRange(c);
                        string needed = c.IsNullable ? "" : $"if (double.IsNaN({p})) return \"{label} is required.\"; ";
                        L($"        {needed}if (!double.IsNaN({p}) && ({p} < {TemplateHelpers.Inv(min)} || {p} > {TemplateHelpers.Inv(max)})) return \"{label} must be between {TemplateHelpers.Inv(min)} and {TemplateHelpers.Inv(max)}.\";");
                        break;
                    }
                    case Cell.Whole:
                    {
                        var range = project.RangeFor(c)!.Value;
                        string needed = c.IsNullable ? "" : $"if (double.IsNaN({p})) return \"{label} is required.\"; ";
                        L($"        {needed}if (!double.IsNaN({p}) && {p} != Math.Floor({p})) return \"{label} must be a whole number.\";");
                        L($"        if (!double.IsNaN({p}) && ({p} < {range.Min} || {p} > {range.Max})) return \"{label} must be between {range.Min} and {range.Max}.\";");
                        break;
                    }
                }
            }
            L("        return null;");
            L("    }");
            L();
            L($"    public void ApplyTo({entity} e)");
            L("    {");
            foreach (var c in columns)
            {
                string p = P(c);
                bool nullable = c.IsNullable;
                switch (Kind(grid, c)!.Value)
                {
                    case Cell.Lookup:
                        string keyType = c.CSharpBase();
                        L(nullable ? $"        e.{p} = {p};" : keyType == "string" ? $"        e.{p} = {p} ?? \"\";" : $"        e.{p} = {p} ?? default;");
                        break;
                    case Cell.Choices:
                    case Cell.Text:
                        L(nullable ? $"        e.{p} = string.IsNullOrWhiteSpace({p}) ? null : {p};" : $"        e.{p} = {p};");
                        break;
                    case Cell.Guid:
                        L(nullable ? $"        e.{p} = string.IsNullOrWhiteSpace({p}) ? null : Guid.Parse({p});" : $"        e.{p} = Guid.Parse({p});");
                        break;
                    case Cell.Boolean:
                        L(nullable ? $"        e.{p} = {p};" : $"        e.{p} = {p};");
                        break;
                    case Cell.Date:
                    {
                        string t = c.CSharpBase();
                        string value = t == "DateOnly" ? $"DateOnly.FromDateTime({p}.Value.Date)"
                            : t == "DateTimeOffset" ? $"new DateTimeOffset({p}.Value.Date + {Time(c)}, TimeSpan.Zero)"
                            : $"{p}.Value.Date + {Time(c)}";
                        L(nullable ? $"        e.{p} = {p} is null ? null : {value};" : $"        e.{p} = {value};");
                        break;
                    }
                    default:
                    {
                        string t = NumericClassifier.NumericCSharpType(c);
                        string value = Kind(grid, c) == Cell.Money ? $"Math.Round((decimal){p}, {NumericClassifier.CurrencyDigits(c)})"
                            : t == "decimal" ? $"Math.Round((decimal){p}, {NumericClassifier.DecimalPlaces(c)})"
                            : t == "double" ? p : $"({t}){p}";
                        L(nullable ? $"        e.{p} = double.IsNaN({p}) ? null : {value};" : $"        e.{p} = {value};");
                        break;
                    }
                }
            }
            L("    }");
            L("}");

            L();
            L($"/// <summary> The lists the {child.Title} rows choose from: the parents' rows for a drop-down, a way to find them by what is typed for a type-ahead box. </summary>");
            L($"public sealed class {LookupsClass(parent, child.Member)}");
            L("{");
            foreach (var c in columns.Where(c => Kind(grid, c) == Cell.Lookup))
            {
                if (grid.EditTypeAhead(c))
                    L($"    public Func<string, Task<IReadOnlyList<{option}>>> Find{P(c)} {{ get; set; }} = _ => Task.FromResult<IReadOnlyList<{option}>>([]);");
                else
                    L($"    public IReadOnlyList<{option}> {P(c)}Options {{ get; set; }} = [];");
            }
            L("}");

            L();
            L($"/// <summary> The {child.Title} rows as a table of boxes with Remove on each line and an Add button, built in code; it follows the rows collection. </summary>");
            L($"public sealed class {PanelClass(parent, child.Member)} : StackPanel");
            L("{");
            L($"    private const double CellWidth = 150;");
            L($"    private readonly ObservableCollection<{row}> _rows;");
            L($"    private readonly {LookupsClass(parent, child.Member)} _lookups;");
            L("    private readonly StackPanel _lines = new() { Spacing = 4 };");
            L();
            L($"    public {PanelClass(parent, child.Member)}(ObservableCollection<{row}> rows, {LookupsClass(parent, child.Member)} lookups)");
            L("    {");
            L("        _rows = rows;");
            L("        _lookups = lookups;");
            L("        Spacing = 6;");
            L($"        Children.Add(new TextBlock {{ Text = \"{child.Title.Replace("\"", "'")}\", Style = (Style)Application.Current.Resources[\"BodyStrongTextBlockStyle\"] }});");
            L("        Children.Add(new ScrollViewer { Content = _lines, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Enabled, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled });");
            L($"        var add = new Button {{ Content = \"Add {Labels.Words(grid.BaseName).Replace("\"", "'")}\", HorizontalAlignment = HorizontalAlignment.Left }};");
            L($"        AutomationProperties.SetName(add, \"Add {Labels.Words(grid.BaseName).Replace("\"", "'")}\");");
            L($"        add.Click += (_, _) => _rows.Add(new {row}());");
            L("        Children.Add(add);");
            L("        _rows.CollectionChanged += (_, _) => Refresh();");
            L("        Refresh();");
            L("    }");
            L();
            L("    /// <summary> Builds the lines again: after the lists arrive, and whenever a line is added or removed. </summary>");
            L("    public void Refresh()");
            L("    {");
            L("        _lines.Children.Clear();");
            L("        if (_rows.Count == 0)");
            L("        {");
            L("            _lines.Children.Add(new TextBlock { Text = \"Nothing yet.\", FontStyle = Windows.UI.Text.FontStyle.Italic });");
            L("            return;");
            L("        }");
            L("        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };");
            foreach (var c in columns)
                L($"        header.Children.Add(new TextBlock {{ Text = \"{(GridCaption.For(c, Labels.Field(c)) + (grid.IsMarked(c) ? " *" : "")).Replace("\"", "'")}\", Width = CellWidth, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }});");
            L("        header.Children.Add(new TextBlock { Text = \"\", Width = 80 });");
            L("        _lines.Children.Add(header);");
            L("        foreach (var row in _rows.ToList())");
            L("        {");
            L("            var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };");
            foreach (var c in columns)
                Cell1(o, grid, c, project, option);
            L($"            var remove = new Button {{ Content = \"Remove\", Width = 80 }};");
            L($"            AutomationProperties.SetName(remove, \"Remove {Labels.Words(grid.BaseName).Replace("\"", "'")}\");");
            L("            remove.Click += (_, _) => _rows.Remove(row);");
            L("            line.Children.Add(remove);");
            L("            _lines.Children.Add(line);");
            L("        }");
            L("    }");
            L("}");
        }
        return o.ToString();
    }

    // The control of one column in a line (the line is `line`, the row is `row`): it shows the row's value and writes what is typed or picked back into it.
    private static void Cell1(StringBuilder o, ChildGrid grid, ColumnModel c, ProjectSettings project, string option)
    {
        void L(string s) => o.Append("            ").Append(s).Append('\n');
        string p = c.CSharpName(), label = Labels.Field(c).Replace("\"", "'"), box = "box" + p;
        L("{");
        void Name() => L($"    AutomationProperties.SetName({box}, \"{label}\");");
        switch (Kind(grid, c)!.Value)
        {
            case Cell.Lookup:
                string keyType = c.CSharpBase();
                if (grid.EditTypeAhead(c))
                {
                    L($"    var {box} = new AutoSuggestBox {{ Width = CellWidth, Text = row.{p}Text, TextMemberPath = \"Text\", PlaceholderText = \"Type to find\" }};");
                    Name();
                    L($"    {box}.TextChanged += async (sender, args) =>");
                    L("    {");
                    L("        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;");
                    L($"        row.{p} = null;");
                    L($"        row.{p}Text = sender.Text;");
                    L($"        sender.ItemsSource = string.IsNullOrWhiteSpace(sender.Text) ? null : await _lookups.Find{p}(sender.Text);");
                    L("    };");
                    L($"    {box}.SuggestionChosen += (_, args) =>");
                    L("    {");
                    L($"        var picked = ({option})args.SelectedItem;");
                    L($"        row.{p} = picked.Id is {keyType} id ? id : null;");
                    L($"        row.{p}Text = picked.Text;");
                    L("    };");
                }
                else
                {
                    L($"    var choices = {(c.IsNullable ? $"new List<{option}> {{ new(null, \"\") }}.Concat(_lookups.{p}Options).ToList()" : $"_lookups.{p}Options.ToList()")};");
                    L($"    var {box} = new ComboBox {{ Width = CellWidth, ItemsSource = choices, DisplayMemberPath = \"Text\" }};");
                    Name();
                    L($"    {box}.SelectedItem = choices.FirstOrDefault(r => Equals(r.Id, row.{p}));");
                    L($"    {box}.SelectionChanged += (_, _) => row.{p} = (({option}?){box}.SelectedItem)?.Id is {keyType} picked ? picked : null;");
                }
                break;
            case Cell.Choices:
                string list = string.Join(", ", c.Choices!.Select(v => "\"" + ChoiceFormat.CSharp(v) + "\""));
                L($"    var {box} = new ComboBox {{ Width = CellWidth, ItemsSource = new[] {{ {(c.IsNullable ? "\"\", " : "")}{list} }} }};");
                Name();
                L($"    {box}.SelectedItem = row.{p};");
                L($"    {box}.SelectionChanged += (_, _) => row.{p} = {box}.SelectedItem as string ?? \"\";");
                break;
            case Cell.Boolean:
                L($"    var {box} = new CheckBox {{ Width = CellWidth, IsThreeState = {(c.IsNullable ? "true" : "false")}, IsChecked = row.{p} }};");
                Name();
                if (c.IsNullable)
                {
                    L($"    {box}.Checked += (_, _) => row.{p} = true;");
                    L($"    {box}.Unchecked += (_, _) => row.{p} = false;");
                    L($"    {box}.Indeterminate += (_, _) => row.{p} = null;");
                }
                else
                {
                    L($"    {box}.Checked += (_, _) => row.{p} = true;");
                    L($"    {box}.Unchecked += (_, _) => row.{p} = false;");
                }
                break;
            case Cell.Date:
                L($"    var {box} = new CalendarDatePicker {{ Width = CellWidth, Date = row.{p} }};");
                Name();
                L($"    {box}.DateChanged += (_, args) => row.{p} = args.NewDate;");
                break;
            case Cell.Money:
            case Cell.Decimal:
            case Cell.Whole:
                L($"    var {box} = new NumberBox {{ Width = CellWidth, Value = row.{p}, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden }};");
                Name();
                if (Kind(grid, c) == Cell.Money)
                    L($"    {box}.NumberFormatter = new CurrencyNumberFormatter(\"{project.CurrencyCode}\", {NumericClassifier.CurrencyDigits(c)});");
                else if (Kind(grid, c) == Cell.Decimal)
                {
                    var (min, max) = NumericClassifier.DecimalRange(c);
                    L($"    {box}.Minimum = {TemplateHelpers.Inv(min)};");
                    L($"    {box}.Maximum = {TemplateHelpers.Inv(max)};");
                }
                else if (project.RangeFor(c) is { } range)
                {
                    L($"    {box}.Minimum = {range.Min};");
                    L($"    {box}.Maximum = {range.Max};");
                }
                L($"    {box}.ValueChanged += (_, args) => row.{p} = args.NewValue;");
                break;
            default:
                L($"    var {box} = new TextBox {{ Width = CellWidth, Text = row.{p}{(c.CharacterLength is > 0 and < 4000 ? $", MaxLength = {c.CharacterLength}" : "")} }};");
                Name();
                L($"    {box}.TextChanged += (_, _) => row.{p} = {box}.Text;");
                break;
        }
        L($"    line.Children.Add({box});");
        L("}");
    }
}
