using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> RowApprovals in the Python and Rust APIs: the routes, the sentences shared with the ASP.NET API, the router order and the gate clause. </summary>
[TestClass]
public class PythonRustApprovalsTests
{
    private const string Gate = "TimeSheet.IsPaid; to 1; when EmployeeId>0; approval";

    [TestMethod]
    public async Task Python_gets_the_approval_router_before_the_tables_routers_with_the_same_words()
    {
        var (success, errors, read, files) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Python");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string approvals = read("routes/approvals.py");
        Expect.Contains(approvals, "@router.get(spec.route + \"/toapprove\"");
        Expect.Contains(approvals, "@router.post(spec.route + \"/{id}/request\"");
        Expect.Contains(approvals, ApprovalWords.FourEyes);
        Expect.Contains(approvals, ApprovalWords.RequesterUnknown);
        Expect.Contains(approvals, ApprovalWriter.FourEyesType);
        Expect.Contains(approvals, "route=\"/api/timesheets\", row=TimeSheet, row_key=TimeSheet.time_sheet_id, side=TimeSheetApproval");
        string routes = read("routes/__init__.py");
        Assert.IsTrue(routes.IndexOf("approvals.router", StringComparison.Ordinal) is var at and >= 0 && at < routes.IndexOf("time_sheet.router", StringComparison.Ordinal), routes);
        Assert.IsFalse(files.Any(f => f.EndsWith("routes/time_sheet_approval.py")), "the side table has no routes of its own");
    }

    [TestMethod]
    public async Task Python_gate_with_the_approval_clause_is_exempt_once_the_latest_request_is_approved()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, Gate, stacks: "Python");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string gates = read("app/gates.py");
        Expect.Contains(gates, "def row_approved_time_sheet(session: Session, row_id) -> bool:");
        Expect.Contains(gates, "approved = current is not None and row_approved_time_sheet(session, current.time_sheet_id)");
        Expect.Contains(gates, "and not approved:");
    }

    [TestMethod]
    public async Task Rust_gets_the_approval_routes_with_sql_for_its_database_and_a_row_struct_of_its_own()
    {
        var (success, errors, read, files) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Rust");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string approvals = read("routes/approvals.rs");
        Expect.Contains(approvals, ".route(\"/api/timesheets/toapprove\", get(to_approve_time_sheet))");
        Expect.Contains(approvals, "struct TimeSheetApproval {");
        Expect.Contains(approvals, "#[sqlx(rename = \"ApprovalStatusId\")]");
        Expect.Contains(approvals, ApprovalWords.FourEyes);
        Expect.Contains(approvals, "UPDATE \"TimeSheetApproval\" SET \"ApprovalStatusId\" = ?, \"DecidedBy\" = ?");
        Expect.Contains(approvals, "pub static HOOKS: RwLock<ApprovalHooks>");
        Expect.Contains(read("routes/mod.rs"), ".merge(approvals::router())");
        Assert.IsFalse(files.Any(f => f.EndsWith("routes/time_sheet_approval.rs")), "the side table has no routes of its own");
    }

    [TestMethod]
    public async Task Rust_gate_with_the_approval_clause_is_exempt_once_the_latest_request_is_approved()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, Gate, stacks: "Rust");

        Assert.IsTrue(success, string.Join(" | ", errors));
        string gates = read("routes/gates.rs");
        Expect.Contains(gates, "async fn row_approved_time_sheet(pool: &Pool, id: i32) -> ApiResult<bool> {");
        Expect.Contains(gates, "let approved = match current { Some(current) => row_approved_time_sheet(pool, current.time_sheet_id).await?, None => false };");
        Expect.Contains(gates, "&& !approved {");
    }

    [TestMethod]
    public void The_api_sentences_are_said_once_in_Core()
    {
        string api = ApprovalWriter.File(ApprovalPlanFor(), "ShopContext", []);

        foreach (string sentence in new[] { ApprovalWords.RequesterUnknown, ApprovalWords.NotApprover, ApprovalWords.NotRequester, ApprovalWords.FourEyes, ApprovalWords.AlreadyWaiting, ApprovalWords.NothingWaiting })
            Expect.Contains(api, sentence);
    }

    private static ApprovalPlan ApprovalPlanFor()
    {
        var timeSheet = Sample.Table("TimeSheet", [Sample.Column("TimeSheetId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)]);
        var side = Sample.Table("TimeSheetApproval",
        [
            Sample.Column("TimeSheetApprovalId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("TimeSheetId", System.Data.SqlDbType.Int, ordinal: 2),
            Sample.Column("ApprovalStatusId", System.Data.SqlDbType.Int, ordinal: 3),
            Sample.Column("Reason", System.Data.SqlDbType.VarChar, nullable: true, characters: 500, ordinal: 4),
            Sample.Column("RequestedBy", System.Data.SqlDbType.VarChar, characters: 50, ordinal: 5),
            Sample.Column("RequestedOn", System.Data.SqlDbType.DateTime, ordinal: 6),
            Sample.Column("DecidedBy", System.Data.SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 7),
            Sample.Column("DecidedOn", System.Data.SqlDbType.DateTime, nullable: true, ordinal: 8),
            Sample.Column("DecisionNote", System.Data.SqlDbType.VarChar, nullable: true, characters: 500, ordinal: 9),
            Sample.Column("Revision", System.Data.SqlDbType.Int, ordinal: 10)
        ]);
        var employee = Sample.Table("Employee",
        [
            Sample.Column("EmployeeId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Login", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2),
            Sample.Column("PasswordHash", System.Data.SqlDbType.VarChar, characters: 200, ordinal: 3),
            Sample.Column("IsManager", System.Data.SqlDbType.Bit, ordinal: 4)
        ]);
        var project = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["ApiAuth"] = "true", ["UserTable"] = "Employee", ["UserNameColumn"] = "Login", ["PasswordHashColumn"] = "PasswordHash", ["UserRoleColumn"] = "IsManager", ["RowApprovals"] = "TimeSheet"
        });
        var plan = ApprovalPlan.Resolve(new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [timeSheet, side, employee] }, project);
        Assert.IsEmpty(plan.Problems, string.Join(" | ", plan.Problems));
        return plan;
    }
}
