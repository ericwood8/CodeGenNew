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
        Expect.Contains(read("models/time_sheet_approval.py"), "class TimeSheetApproval(Base):");
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
    public async Task Python_tests_seed_the_people_and_play_the_cycle_with_the_shared_words()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Python", extra: new() { ["PythonTests"] = "true" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tests = read("tests/test_approvals.py");
        Expect.Contains(tests, "INSERT INTO \\\"Employee\\\"");
        Expect.Contains(tests, "zz_ann");
        Expect.Contains(tests, ApprovalWords.FourEyes);
        Expect.Contains(tests, ApprovalWords.NotApprover);
        Expect.Contains(tests, ApprovalWriter.FourEyesType);
        Expect.Contains(tests, "def test_a_hook_can_refuse_a_request_and_sees_a_decision(call):");
        Expect.DoesNotContain(tests, "another_table");
        Expect.Contains(read("tests/conftest.py"), "shutil.copy(source, copy)");
        Expect.Contains(read("requirements.txt"), "pytest");
    }

    [TestMethod]
    public async Task Python_tests_are_only_written_when_the_project_asks_for_them()
    {
        var (success, errors, _, files) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Python");

        Assert.IsTrue(success, string.Join(" | ", errors));
        Assert.IsFalse(files.Any(f => f.Contains("tests/test_approvals.py")));
    }

    [TestMethod]
    public async Task With_an_approver_table_the_tests_seed_its_rows_and_test_a_row_for_another_table()
    {
        var extra = new Dictionary<string, string> { ["PythonTests"] = "true", ["RustTests"] = "true", ["ApproverTable"] = "Approver:EmployeeId,TableName", ["TableWriteRoles"] = "Approver:Administrator" };
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Python,Rust", extra: extra, withApprover: true);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("tests/test_approvals.py"), "def test_a_row_for_another_table_does_not_let_a_person_decide_this_one(call):");
        Expect.Contains(read("tests/test_approvals.py"), "'Elsewhere'");
        Expect.Contains(read("tests/api/approval_routes.rs"), "async fn a_row_for_another_table_does_not_let_a_person_decide_this_one() {");
        Expect.Contains(read("tests/api/approval_routes.rs"), "INSERT INTO \"Approver\"");
    }

    [TestMethod]
    public async Task Rust_tests_use_the_scratch_database_and_are_listed_in_the_test_program()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Rust", extra: new() { ["RustTests"] = "true" });

        Assert.IsTrue(success, string.Join(" | ", errors));
        string tests = read("tests/api/approval_routes.rs");
        Expect.Contains(tests, "let api = Client::start_signed_out().await?;");
        Expect.Contains(tests, "api.as_person(\"zz_ann\")");
        Expect.Contains(tests, ApprovalWords.FourEyes);
        Expect.Contains(read("tests/api/main.rs"), "mod approval_routes;");
        string support = read("tests/api/test_support.rs");
        Expect.Contains(support, "pub fn as_person(&self, name: &str) -> Client {");
        Expect.Contains(support, "pub fn pool(&self) -> Pool {");
    }

    [TestMethod]
    public void The_seed_gives_up_with_a_reason_when_a_required_column_cannot_be_filled()
    {
        var timeSheet = Sample.Table("TimeSheet", [Sample.Column("TimeSheetId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)]);
        var side = Sample.Table("TimeSheetApproval", [Sample.Column("TimeSheetApprovalId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)]);
        var employee = Sample.Table("Employee",
        [
            Sample.Column("EmployeeId", System.Data.SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Login", System.Data.SqlDbType.VarChar, characters: 30, ordinal: 2),
            Sample.Column("PasswordHash", System.Data.SqlDbType.VarChar, characters: 200, ordinal: 3),
            Sample.Column("IsManager", System.Data.SqlDbType.Bit, ordinal: 4),
            Sample.Column("Photo", System.Data.SqlDbType.VarBinary, ordinal: 5)
        ]);
        var database = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [timeSheet, side, employee] };
        var project = ProjectSettings.FromValues(new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["ApiAuth"] = "true", ["UserTable"] = "Employee", ["UserNameColumn"] = "Login", ["PasswordHashColumn"] = "PasswordHash", ["UserRoleColumn"] = "IsManager", ["RowApprovals"] = "TimeSheet"
        });
        var plan = new ApprovalPlan([new ApprovalTable(timeSheet, timeSheet.Columns[0], side, side.Columns[0], side.Columns[0], side.Columns[0], side.Columns[0], side.Columns[0], side.Columns[0], side.Columns[0], side.Columns[0], side.Columns[0], side.Columns[0])],
            new UserTablePlan(employee, employee.Columns[1], employee.Columns[2], null, employee.Columns[3], "Manager", "Employee", null), []);

        var seed = ApprovalTestSeed.For(plan, database, project, out string reason);

        Assert.IsNull(seed);
        Expect.Contains(reason, "Employee.Photo has no example value");
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
