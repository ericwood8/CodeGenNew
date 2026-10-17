namespace CodeGenNew.Core;

/// <summary> How a grid shows the value of a column. </summary>
public enum CellKind
{
    /// <summary> A foreign key shown as the parent's name. </summary>
    Lookup,
    /// <summary> Yes or No. </summary>
    Boolean,
    Date,
    /// <summary> A date and time the system stamps (created, modified, last changed): the day and the time of day. </summary>
    DateTime,
    /// <summary> An amount of money, with the currency. </summary>
    Money,
    /// <summary> A decimal that is not money, shown with two decimals. </summary>
    Decimal,
    /// <summary> Text, a whole number or anything else, shown as it is. </summary>
    Plain
}

/// <summary> The one rule for what a grid cell shows, so the same column reads the same in a React, Angular, Blazor and WinUI3 grid. </summary>
public static class GridCells
{
    /// <summary> The kind of cell for a column; <paramref name="lookedUp"/> says the column is a foreign key the screen shows as the parent's name. </summary>
    public static CellKind KindOf(ColumnModel column, bool lookedUp) =>
        lookedUp ? CellKind.Lookup
        : column.IsBooleanColumn ? CellKind.Boolean
        : TemplateHelpers.IsDate(column) ? (column.IsStampColumn && column.SqlType != System.Data.SqlDbType.Date ? CellKind.DateTime : CellKind.Date)
        : column.IsMoneyAmount ? CellKind.Money
        : column.SqlType is System.Data.SqlDbType.Decimal or System.Data.SqlDbType.Money or System.Data.SqlDbType.SmallMoney ? CellKind.Decimal
        : CellKind.Plain;
}
