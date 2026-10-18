using System.Text;

namespace CodeGenNew.Core;

/// <summary> One search box of a desktop list page: the view-model field and property that hold what was typed, the name the API's search reads it by and the caption. </summary>
public sealed record GridSearchBox(string Field, string Prop, string ParamName, string Label);

/// <summary> One drop-down filter of a desktop list page: the parent whose rows it lists, the C# type of the foreign key column and the <c>Convert</c> method that turns the boxed parent key into it
/// (null when it needs none), and the names of the view-model field, property and choices list. </summary>
public sealed record GridKeyFilter(FormParent Parent, string Label, string Type, string? Convert, string Field, string Prop, string Choices, string ParamName);

/// <summary> What the WinUI3 and Uno list pages and view models take from the form that is not markup: the search boxes, the parent drop-down filters and the text of a grid cell. Both stacks write the
/// same text from here. </summary>
public static class DesktopGridCode
{
    /// <summary> One box per searchable column and per typed key, in column order. The API's search takes them by name, so the order only sets the boxes' layout. </summary>
    public static List<GridSearchBox> SearchBoxes(FormModel form) => form.Table.SearchableColumns.Concat(form.TextKeyBoxes).OrderBy(c => c.OrdinalPosition)
        .Select(c => new GridSearchBox(TemplateHelpers.LowerFirst(c.Name) + "Filter", c.Name + "Filter", TemplateHelpers.LowerFirst(c.Name), GridCaption.For(c, Labels.Column(c.Name))))
        .ToList();

    /// <summary> A drop-down of the parent's rows per whole-number foreign key: an exact match on the key (the first entry, "All", is no filter). </summary>
    public static List<GridKeyFilter> KeyFilters(FormModel form) => form.SearchKeys
        .Select(k => new GridKeyFilter(k.Parent, k.Label, k.Column.CSharpBase(),
            k.Column.CSharpBase() switch { "int" => "ToInt32", "long" => "ToInt64", "short" => "ToInt16", "byte" => "ToByte", _ => null },
            TemplateHelpers.LowerFirst(k.Column.Name) + "Filter", k.Column.Name + "Filter", k.Column.Name + "Choices", k.ParamName))
        .ToList();

    /// <summary> The element of the view model's <c>cells</c> list that shows the column (sixteen spaces in, a comma at the end): the parent's name for a foreign key, yes / no, a date, an amount of money
    /// in the project's currency, a decimal with two places, text or the value. </summary>
    public static string CellLines(FormModel form, ProjectSettings project, ColumnModel column)
    {
        var cellKind = form.CellOf(column);
        string prop = column.Name;
        string varName = TemplateHelpers.LowerFirst(prop);
        bool nullable = column.IsNullable;
        string text;
        if (cellKind == CellKind.Lookup)
        {
            var p = form.ParentOf(column)!;
            text = nullable
                ? $"e.{prop} is {column.CSharpBase()} {varName}Id && {WinUI3Names.NamesDictionary(p)}.TryGetValue({varName}Id, out var {varName}Name) ? {varName}Name : \"\","
                : $"{WinUI3Names.NamesDictionary(p)}.TryGetValue(e.{prop}, out var {varName}Name) ? {varName}Name : e.{prop}.ToString(),";
        }
        else if (cellKind == CellKind.Boolean)
            text = nullable ? $"e.{prop} switch {{ true => \"Yes\", false => \"No\", null => \"\" }}," : $"e.{prop} ? \"Yes\" : \"No\",";
        else if (cellKind == CellKind.Date)
            text = $"e.{prop}" + (nullable ? "?.ToString(\"MM/dd/yyyy\") ?? \"\"" : ".ToString(\"MM/dd/yyyy\")") + ",";
        else if (cellKind == CellKind.DateTime)
            text = $"e.{prop}" + (nullable ? "?.ToString(\"MM/dd/yyyy HH:mm\") ?? \"\"" : ".ToString(\"MM/dd/yyyy HH:mm\")") + ",";
        else if (cellKind == CellKind.Money)
            // currency, in the project's CurrencyCode with the column's own number of decimals (the same as the editor)
            text = (nullable ? $"e.{prop} is {{ }} {varName}Money ? " : "")
                + $"new Windows.Globalization.NumberFormatting.CurrencyFormatter(\"{project.CurrencyCode}\") {{ IsGrouped = true, FractionDigits = {NumericClassifier.CurrencyDigits(column)} }}.FormatDouble((double){(nullable ? varName + "Money" : "e." + prop)})"
                + (nullable ? " : \"\"" : "") + ",";
        else if (cellKind == CellKind.Decimal)
            // a decimal that is not money shows two decimals, as on the web
            text = $"e.{prop}" + (nullable ? "?.ToString(\"N2\") ?? \"\"" : ".ToString(\"N2\")") + ",";
        else if (TemplateHelpers.IsText(column))
            text = $"e.{prop}" + (nullable ? " ?? \"\"" : "") + ",";
        else   // number or Guid
            text = $"e.{prop}" + (nullable ? "?.ToString() ?? \"\"" : ".ToString()") + ",";
        return "                " + text + "\n";
    }
}
