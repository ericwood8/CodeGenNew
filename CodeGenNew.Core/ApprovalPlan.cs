namespace CodeGenNew.Core;

/// <summary> The values of the approval status column of a <c>&lt;Table&gt;Approval</c> row. </summary>
public static class ApprovalStatus
{
    public const int Pending = 1;
    public const int Approved = 2;
    public const int Rejected = 3;
    public const int Withdrawn = 4;
}

/// <summary> A table whose rows each need their own decision, and the side table that holds one row per request cycle. </summary>
public sealed record ApprovalTable(TableModel Table, ColumnModel Key, TableModel Side, ColumnModel SideKey, ColumnModel Link, ColumnModel Status, ColumnModel Reason, ColumnModel RequestedBy, ColumnModel RequestedOn,
    ColumnModel DecidedBy, ColumnModel DecidedOn, ColumnModel DecisionNote, ColumnModel Revision);

/// <summary> <c>RowApprovals=TimeSheet,Expense</c>: for each table a <c>&lt;Table&gt;Approval</c> side table, one row per request cycle (Pending, Approved, Rejected, Withdrawn). The requester is the signed-in person,
/// matched to the user table by name; the approver is an administrator or a person whose user-table role flag is set, never the requester. </summary>
public sealed record ApprovalPlan(IReadOnlyList<ApprovalTable> Tables, UserTablePlan? User, IReadOnlyList<string> Problems)
{
    public const string SuffixOfSide = "Approval";

    public static string SideName(string table) => table + SuffixOfSide;

    public static ApprovalPlan Resolve(DatabaseModel database, ProjectSettings project)
    {
        var tables = new List<ApprovalTable>();
        var problems = new List<string>();
        if (project.RowApprovals.Length == 0)
            return new ApprovalPlan(tables, null, problems);
        if (!project.ApiAuth)
            return new ApprovalPlan(tables, null, ["RowApprovals needs ApiAuth=true: a decision needs to know who is asking."]);
        var (user, userProblem) = UserTable.Resolve(database, project);
        if (user is null)
            return new ApprovalPlan(tables, null, [userProblem ?? "RowApprovals needs UserTable: the requester and the approver are people of the user table."]);
        if (user.Role is null || !user.RoleIsFlag)
            problems.Add("RowApprovals needs UserRoleColumn to be a yes/no column: a person whose flag is set (or an administrator) approves.");

        var apiTables = database.AspNetApiTables(project);
        foreach (string name in project.RowApprovals)
        {
            var table = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(name));
            if (table is null)
            {
                problems.Add($"RowApprovals: {name} is not a table of the database.");
                continue;
            }
            if (!apiTables.Contains(table) || table.PrimaryKeyColumns.Count != 1 || !table.PrimaryKeyColumns[0].IsIntegerColumn)
            {
                problems.Add($"RowApprovals: {table.TableName} needs a generated API and a whole-number key.");
                continue;
            }
            string sideName = SideName(table.TableName);
            var side = database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(sideName));
            if (side is null)
            {
                problems.Add($"RowApprovals: {table.TableName} needs the table {sideName}; the SP_ApprovalTable script ({database.Dialect}) creates it. Apply it, then generate again.");
                continue;
            }
            ColumnModel? Find(string column) => side.Columns.FirstOrDefault(c => c.Name.EqualsIgnoreCase(column));
            var missing = new List<string>();
            ColumnModel Need(string column, Func<ColumnModel, bool> fits, string kind)
            {
                var found = Find(column);
                if (found is null || !fits(found))
                {
                    missing.Add($"{column} ({kind})");
                    return side.Columns[0];
                }
                return found;
            }
            var key = table.PrimaryKeyColumns[0];
            if (side.PrimaryKeyColumns.Count != 1)
            {
                problems.Add($"RowApprovals: {sideName} needs a single whole-number key.");
                continue;
            }
            var plan = new ApprovalTable(table, key, side, side.PrimaryKeyColumns[0],
                Need(table.TableName + "Id", c => c.IsIntegerColumn && !c.IsNullable, "whole number"),
                Need("ApprovalStatusId", c => c.IsIntegerColumn && !c.IsNullable, "whole number"),
                Need("Reason", c => c.IsStringColumn && c.IsNullable, "nullable text"),
                Need("RequestedBy", c => c.IsStringColumn && !c.IsNullable, "text"),
                Need("RequestedOn", c => c.IsDateColumn && !c.IsNullable, "date and time"),
                Need("DecidedBy", c => c.IsStringColumn && c.IsNullable, "nullable text"),
                Need("DecidedOn", c => c.IsDateColumn && c.IsNullable, "nullable date and time"),
                Need("DecisionNote", c => c.IsStringColumn && c.IsNullable, "nullable text"),
                Need("Revision", c => c.IsIntegerColumn && !c.IsNullable, "whole number"));
            if (missing.Count > 0)
                problems.Add($"RowApprovals: {sideName} lacks {string.Join(", ", missing)}.");
            else
                tables.Add(plan);
        }
        return new ApprovalPlan(tables, user, problems);
    }

    /// <summary> The create script of a side table for the given database, so the project starts from the columns the plan expects. </summary>
    public static string CreateScript(SqlDialect dialect, string table)
    {
        string side = SideName(table);
        string Id() => dialect switch
        {
            SqlDialect.PostgreSql => $"\"{side}Id\" integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY",
            SqlDialect.MySql => $"`{side}Id` int NOT NULL AUTO_INCREMENT PRIMARY KEY",
            SqlDialect.Sqlite => $"\"{side}Id\" INTEGER PRIMARY KEY AUTOINCREMENT",
            _ => $"[{side}Id] int IDENTITY(1,1) NOT NULL PRIMARY KEY"
        };
        string Quote(string name) => dialect switch { SqlDialect.PostgreSql or SqlDialect.Sqlite => $"\"{name}\"", SqlDialect.MySql => $"`{name}`", _ => $"[{name}]" };
        string Int = dialect == SqlDialect.Sqlite ? "INTEGER" : "int";
        string Text(int n) => dialect switch { SqlDialect.PostgreSql => $"varchar({n})", SqlDialect.MySql => $"varchar({n})", SqlDialect.Sqlite => $"VARCHAR({n})", _ => $"varchar({n})" };
        string When = dialect switch { SqlDialect.PostgreSql => "timestamp", SqlDialect.MySql => "datetime", SqlDialect.Sqlite => "DATETIME", _ => "datetime2" };
        var lines = new List<string>
        {
            "    " + Id(),
            $"    {Quote(table + "Id")} {Int} NOT NULL REFERENCES {Quote(table)} ({Quote(table + "Id")})",
            $"    {Quote("ApprovalStatusId")} {Int} NOT NULL DEFAULT {ApprovalStatus.Pending}",
            $"    {Quote("Reason")} {Text(500)} NULL",
            $"    {Quote("RequestedBy")} {Text(50)} NOT NULL",
            $"    {Quote("RequestedOn")} {When} NOT NULL",
            $"    {Quote("DecidedBy")} {Text(50)} NULL",
            $"    {Quote("DecidedOn")} {When} NULL",
            $"    {Quote("DecisionNote")} {Text(500)} NULL",
            $"    {Quote("Revision")} {Int} NOT NULL DEFAULT 1"
        };
        return $"CREATE TABLE {Quote(side)} (\n{string.Join(",\n", lines)}\n);\n";
    }
}
