namespace CodeGenNew.Core;

/// <summary> A rule the database does not state but the project does. </summary>
public enum ValueGuardKind { NonNegative, NotFuture }

/// <summary> One guard clause of the insert, update and save routines: a value the routine refuses before it reaches the table. </summary>
public sealed record ValueGuard(ColumnModel Column, ValueGuardKind Kind)
{
    /// <summary> What the caller reads when the routine refuses the value, with the column name in the quoting <paramref name="quote"/> gives. </summary>
    public string Message(Func<string, string> quote) => Kind == ValueGuardKind.NonNegative
        ? $"{quote(Column.DbName)} must not be negative."
        : $"{quote(Column.DbName)} must not be later than today.";

    /// <summary> The test, true when the value breaks the rule; a NULL parameter (nothing supplied, or "leave the column as it is") never does. <paramref name="textParameter"/> is for the SQL Server
    /// update procedure, whose parameters are all text: the value is converted first, and a value that is no number or date is left to the statement that stores it. </summary>
    /// <param name="storedRow">For an update: the <c>FROM table WHERE key = parameter</c> that finds the row being changed. A value equal to the one the row already holds is not judged again, so a
    /// stored value that breaks the rule does not stop an edit that leaves it alone; only a value the caller changes is checked. Null for an insert, which has no stored row.</param>
    public string Condition(string parameter, SqlDialect dialect, bool textParameter = false, string? storedRow = null)
    {
        string broken = Broken(parameter, dialect, textParameter);
        return storedRow is null ? broken : $"{broken} AND NOT EXISTS (SELECT 1 {storedRow} AND {SameAsStored(parameter, dialect, textParameter)})";
    }

    private string Broken(string parameter, SqlDialect dialect, bool textParameter)
    {
        if (Kind == ValueGuardKind.NonNegative)
        {
            // PostgreSQL has no money < integer; an untyped '0' takes the type of the parameter (money, numeric or integer)
            string zero = dialect == SqlDialect.PostgreSql ? "'0'" : "0";
            return textParameter ? $"{parameter} IS NOT NULL AND TRY_CAST({parameter} AS decimal(38, 10)) < 0" : $"{parameter} IS NOT NULL AND {parameter} < {zero}";
        }

        return dialect switch
        {
            SqlDialect.PostgreSql => $"{parameter} IS NOT NULL AND {parameter}::date > current_date",
            SqlDialect.MySql => $"{parameter} IS NOT NULL AND DATE({parameter}) > CURDATE()",
            _ when textParameter => $"{parameter} IS NOT NULL AND TRY_CAST({parameter} AS DATE) > CAST(GETDATE() AS DATE)",
            _ => $"{parameter} IS NOT NULL AND CAST({parameter} AS DATE) > CAST(GETDATE() AS DATE)"
        };
    }

    // a date rule compares days: the same day is the same value as far as the rule goes
    private string SameAsStored(string parameter, SqlDialect dialect, bool textParameter)
    {
        string column = dialect switch
        {
            SqlDialect.PostgreSql => "\"" + Column.DbName.Replace("\"", "\"\"") + "\"",
            SqlDialect.MySql => "`" + Column.DbName.Replace("`", "``") + "`",
            _ => "[" + Column.DbName.Replace("]", "]]") + "]"
        };
        if (Kind == ValueGuardKind.NonNegative)
            return textParameter ? $"{column} = TRY_CAST({parameter} AS decimal(38, 10))" : $"{column} = {parameter}";

        return dialect switch
        {
            SqlDialect.PostgreSql => $"{column}::date = {parameter}::date",
            SqlDialect.MySql => $"DATE({column}) = DATE({parameter})",
            _ when textParameter => $"CAST({column} AS DATE) = TRY_CAST({parameter} AS DATE)",
            _ => $"CAST({column} AS DATE) = CAST({parameter} AS DATE)"
        };
    }
}

public static class ValueGuards
{
    /// <summary> The guards of a table, from the columns the project lists in <c>NonNegativeColumns</c> and <c>NotFutureDateColumns</c>. A column whose CHECK constraint already states a minimum
    /// needs none (the database refuses the value itself), and neither does a column the system fills in. Without a project there are none. </summary>
    public static IReadOnlyList<ValueGuard> Of(TableModel model, ProjectSettings? project)
    {
        var guards = new List<ValueGuard>();
        if (project is null)
            return guards;

        foreach (var column in model.Columns.Where(c => !c.IsSystemFilled()))
        {
            bool number = column.IsIntegerColumn || column.IsNumericColumn || column.IsMoneyColumn;
            if (number && column.Check?.Min is null && project.IsNonNegative(model.TableName, column.Name))
                guards.Add(new ValueGuard(column, ValueGuardKind.NonNegative));
            else if (TemplateHelpers.IsDate(column) && project.IsNotFuture(model.TableName, column.Name))
                guards.Add(new ValueGuard(column, ValueGuardKind.NotFuture));
        }
        return guards;
    }
}
