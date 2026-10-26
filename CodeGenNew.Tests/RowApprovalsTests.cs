using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> RowApprovals: a decision per row over a side table, the routes, the four eyes, the gate clause and the create script. </summary>
[TestClass]
public class RowApprovalsTests
{
    private const string Side = "CREATE TABLE TimeSheetApproval (TimeSheetApprovalId INTEGER PRIMARY KEY, TimeSheetId INTEGER NOT NULL REFERENCES TimeSheet (TimeSheetId), ApprovalStatusId INTEGER NOT NULL, Reason VARCHAR(500) NULL, RequestedBy VARCHAR(50) NOT NULL, RequestedOn DATETIME NOT NULL, DecidedBy VARCHAR(50) NULL, DecidedOn DATETIME NULL, DecisionNote VARCHAR(500) NULL, Revision INTEGER NOT NULL)";

    private static string CreateDatabase(bool withSide, bool withApprover)
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_apr_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        var statements = new List<string>
        {
            "CREATE TABLE Employee (EmployeeId INTEGER PRIMARY KEY, FullName VARCHAR(40) NOT NULL, Login VARCHAR(30) NOT NULL, PasswordHash VARCHAR(200) NOT NULL, IsManager BOOLEAN NOT NULL, IsAdministrator BOOLEAN NOT NULL)",
            "CREATE TABLE TimeSheet (TimeSheetId INTEGER PRIMARY KEY, EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId), WeekEnding DATE NOT NULL, Hours DECIMAL(6,2) NOT NULL, IsPaid BOOLEAN NOT NULL)"
        };
        if (withSide)
            statements.Add(Side);
        if (withApprover)
            statements.Add("CREATE TABLE Approver (ApproverId INTEGER PRIMARY KEY, EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId), TableName VARCHAR(50) NULL)");
        foreach (string statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    internal static async Task<(bool Success, List<string> Errors, Func<string, string> Read, List<string> Files)> GenerateAsync(bool withSide, string gates = "", string stacks = "Api", Dictionary<string, string>? extra = null, bool withApprover = false)
    {
        string database = CreateDatabase(withSide, withApprover);
        string output = Path.Combine(Path.GetTempPath(), "codegen_apr_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = stacks, ["Dashboard"] = "false", ["AccessMode"] = "Ef",
                ["ApiAuth"] = "true", ["UserTable"] = "Employee", ["UserNameColumn"] = "Login", ["PasswordHashColumn"] = "PasswordHash", ["UserRoleColumn"] = "IsManager", ["UserAdminColumn"] = "IsAdministrator",
                ["RowApprovals"] = "TimeSheet"
            };
            if (gates.Length > 0)
                values["ChangeGates"] = gates;
            foreach (var (key, value) in extra ?? [])
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = stacks.Split(","), OutputDirectory = output, DatabaseName = database, Schema = "main", Essentials = true
            });
            var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(output, f).Replace('\\', '/')).ToList();
            var contents = files.ToDictionary(f => f, f => File.ReadAllText(Path.Combine(output, f)).Replace("\r\n", "\n"));
            return (report.Success, report.Errors.ToList(), name => contents[files.Single(f => f.EndsWith(name))], files);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }

    [TestMethod]
    public async Task Without_the_side_table_the_run_says_which_script_creates_it_and_writes_that_script()
    {
        var (success, errors, read, files) = await GenerateAsync(withSide: false);

        Assert.IsFalse(success);
        Expect.Contains(string.Join(" | ", errors), "TimeSheet needs the table TimeSheetApproval");
        string script = read("Sql/ApprovalTables.sql");
        Expect.Contains(script, "CREATE TABLE \"TimeSheetApproval\"");
        Expect.Contains(script, "\"ApprovalStatusId\" INTEGER NOT NULL DEFAULT 1");
    }

    [TestMethod]
    public async Task With_the_side_table_the_API_gets_the_routes_the_four_eyes_and_no_crud_for_the_side_table()
    {
        var (success, errors, read, files) = await GenerateAsync(withSide: true);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string approvals = read("Apis/Approvals.cs");
        Expect.Contains(approvals, "app.MapPost(\"/api/timesheets/{id}/request\", RequestAsync)");
        Expect.Contains(approvals, "app.MapGet(\"/api/timesheets/toapprove\", ToApproveAsync)");
        Expect.Contains(approvals, "string.Equals(latest.RequestedBy, person.Name, StringComparison.OrdinalIgnoreCase)");
        Expect.Contains(approvals, "a.ApprovalStatusId == 1)");
        Expect.Contains(approvals, "Where(u => u.Login == name).Select(u => new { Name = u.Login, Key = u.EmployeeId, Flag = u.IsManager == true, Admin = u.IsAdministrator == true })");
        Expect.Contains(read("Apis/ApiRegistration.cs"), "ApprovalsApi.Register(app);");
        Assert.IsFalse(files.Any(f => f.EndsWith("TimeSheetApprovalApi.cs")), "the side table has no CRUD API of its own");
        Assert.IsTrue(files.Any(f => f.EndsWith("TimeSheetApi.cs")));
    }

    [TestMethod]
    public async Task A_gate_with_the_approval_clause_is_exempt_once_the_latest_request_is_approved()
    {
        var (success, errors, read, _) = await GenerateAsync(withSide: true, "TimeSheet.IsPaid; to 1; when Hours>0; approval");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string gates = read("Apis/Gates.cs");
        Expect.Contains(gates, "bool approved = !isNew && await RowApprovedAsync(context, row.TimeSheetId);");
        Expect.Contains(gates, "&& !approved)");
        Expect.Contains(gates, "OrderByDescending(a => a.Revision)");
        Expect.Contains(gates, "unless the row is approved.");
    }

    [TestMethod]
    public void The_approval_clause_needs_the_table_in_RowApprovals()
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["ApiAuth"] = "true", ["ChangeGates"] = "TimeSheet.IsPaid; to 1; when Hours>0; approval"
        });

        CollectionAssert.DoesNotContain(project.ImpliedPlanTemplates.ToList(), "API_Approvals");
        Assert.AreEqual(0, project.RowApprovals.Length);
    }

    [TestMethod]
    public void The_create_script_is_written_for_every_database()
    {
        foreach (var dialect in new[] { SqlDialect.SqlServer, SqlDialect.PostgreSql, SqlDialect.MySql, SqlDialect.Sqlite })
        {
            string script = ApprovalPlan.CreateScript(dialect, "Expense");
            Expect.Contains(script, "ExpenseApproval");
            Expect.Contains(script, "Revision");
            Expect.Contains(script, "DecisionNote");
        }
        Expect.Contains(ApprovalPlan.CreateScript(SqlDialect.SqlServer, "Expense"), "[ExpenseApprovalId] int IDENTITY(1,1)");
    }
}
