namespace CodeGenNew.Core;

/// <summary> The style rules of a sortable Angular grid (the header button and the right-click menu). The React pages style the same two things inline in gridSort.tsx. </summary>
public static class GridSortStyles
{
    public const string Css =
        ".sort-header {\n  font: inherit;\n  font-weight: inherit;\n  background: none;\n  border: none;\n  padding: 0;\n  cursor: pointer;\n  color: inherit;\n}\n\n" +
        ".sort-menu {\n  position: fixed;\n  z-index: 10;\n  margin: 0;\n  padding: 4px;\n  list-style: none;\n  background: Canvas;\n  color: CanvasText;\n  border: 1px solid GrayText;\n  border-radius: 4px;\n}\n";
}
