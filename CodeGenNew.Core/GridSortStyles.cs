namespace CodeGenNew.Core;

/// <summary> The style rules of a sortable Angular grid (the header button and the right-click menu). The React pages style the same two things inline in gridSort.tsx. </summary>
public static class GridSortStyles
{
    public const string Css =
        ".sort-header {\n  font: inherit;\n  font-weight: inherit;\n  background: none;\n  border: none;\n  padding: 0;\n  cursor: pointer;\n  color: inherit;\n}\n\n" +
        ".sort-menu {\n  position: fixed;\n  z-index: 10;\n  margin: 0;\n  padding: 4px;\n  list-style: none;\n  background: Canvas;\n  color: CanvasText;\n  border: 1px solid GrayText;\n  border-radius: 4px;\n}\n";
}

/// <summary> The style rule of the Columns list of an Angular grid (the check boxes in a wrapping row). The React and Blazor pages style the same element inline. </summary>
public static class ColumnPickerStyles
{
    public const string Css = "\n.column-picker .column-list {\n  display: flex;\n  flex-wrap: wrap;\n  gap: 4px 16px;\n  padding: 4px 0;\n}\n";
}

/// <summary> The styles only the OData screen needs: the number that says the place of a column in a several-column sort, and the filter row under the headers. </summary>
public static class ODataGridStyles
{
    public const string Css =
        "\n.sort-order {\n  display: inline-block;\n  margin-left: 2px;\n  padding: 0 4px;\n  border: 1px solid currentColor;\n  border-radius: 8px;\n  font-size: 0.7em;\n  line-height: 1.3;\n  vertical-align: super;\n}\n\n" +
        ".filter-row td {\n  padding: 2px 4px;\n  vertical-align: top;\n}\n\n" +
        ".filter-row input:not([type=checkbox]),\n.filter-row select {\n  width: 100%;\n  min-width: 4.5rem;\n  box-sizing: border-box;\n}\n\n" +
        ".filter-row .range {\n  display: flex;\n  gap: 2px;\n}\n\n" +
        ".filter-row .empty-toggle {\n  display: block;\n  font-size: 0.75rem;\n  white-space: nowrap;\n}\n\n" +
        ".filter-actions {\n  white-space: nowrap;\n}\n";
}
