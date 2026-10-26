using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> ApproverTable: who may approve is a table of rows, not the user table's role flag. The same lookup in the API, the Python and Rust APIs and the WinUI3 rules. </summary>
[TestClass]
public class ApproverTableTests
{
    private static Dictionary<string, string> Setting(bool trusted = true, string table = "Approver:EmployeeId,TableName")
    {
        var values = new Dictionary<string, string> { ["ApproverTable"] = table };
        if (trusted)
            values["TableWriteRoles"] = "Approver:Administrator";
        return values;
    }

    [TestMethod]
    public async Task The_api_looks_a_person_up_in_the_approver_table_for_the_table_being_decided()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, extra: Setting(), withApprover: true);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string approvals = read("Apis/Approvals.cs");
        Expect.Contains(approvals, "context.Set<Approver>().AsNoTracking().AnyAsync(a => a.EmployeeId == person.Key && (a.TableName == null || a.TableName == \"\" || a.TableName == table))");
        Expect.Contains(approvals, "ApprovalPeople.MayDecideAsync(context, person, \"TimeSheet\")");
        Expect.Contains(approvals, "Flag = false");
        Expect.DoesNotContain(approvals, "Task.FromResult(person.MayDecide)");
    }

    [TestMethod]
    public async Task Without_the_setting_the_role_flag_decides_as_before()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string approvals = read("Apis/Approvals.cs");
        Expect.Contains(approvals, "Task.FromResult(person.MayDecide)");
        Expect.Contains(approvals, "Flag = u.IsManager == true");
    }

    [TestMethod]
    public async Task A_table_without_a_name_column_lets_a_person_decide_every_table()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, extra: Setting(table: "Approver:EmployeeId"), withApprover: true);

        Assert.IsTrue(success, string.Join(" | ", errors));
        Expect.Contains(read("Apis/Approvals.cs"), "AnyAsync(a => a.EmployeeId == person.Key)");
    }

    [TestMethod]
    public async Task The_run_refuses_an_approver_table_anyone_signed_in_may_change()
    {
        var (success, errors, _, _) = await RowApprovalsTests.GenerateAsync(withSide: true, extra: Setting(trusted: false), withApprover: true);

        Assert.IsFalse(success);
        Expect.Contains(string.Join(" | ", errors), "TableWriteRoles=Approver:Administrator");
    }

    [TestMethod]
    public async Task The_run_names_a_person_column_that_is_not_there()
    {
        var (success, errors, _, _) = await RowApprovalsTests.GenerateAsync(withSide: true, extra: Setting(table: "Approver:PersonId"), withApprover: true);

        Assert.IsFalse(success);
        Expect.Contains(string.Join(" | ", errors), "needs a whole-number column PersonId");
    }

    [TestMethod]
    public async Task Python_asks_the_approver_table_with_the_same_rule()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Python", extra: Setting(), withApprover: true);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string approvals = read("routes/approvals.py");
        Expect.Contains(approvals, "from app.models.approver import Approver");
        Expect.Contains(approvals, "from sqlalchemy import literal, or_, select, update");
        Expect.Contains(approvals, "def may_decide(session: Session, person: Person, table: str) -> bool:");
        Expect.Contains(approvals, "query = select(literal(1)).select_from(Approver).where(Approver.employee_id == person.key)");
        Expect.Contains(approvals, "if not may_decide(session, person, spec.table):");
    }

    [TestMethod]
    public async Task Rust_counts_the_approver_rows_for_the_person_and_table()
    {
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Rust", extra: Setting(), withApprover: true);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string approvals = read("routes/approvals.rs");
        Expect.Contains(approvals, "async fn may_decide(pool: &Pool, person: &Person, table: &str) -> ApiResult<bool> {");
        Expect.Contains(approvals, "SELECT COUNT(*) FROM \"Approver\" WHERE \"EmployeeId\" = ? AND (\"TableName\" IS NULL OR \"TableName\" = '' OR \"TableName\" = ?)");
        Expect.Contains(approvals, ".bind(person.key).bind(table)");
        Expect.Contains(approvals, "require_approver(pool, user, \"TimeSheet\")");
    }

    [TestMethod]
    public async Task WinUI3_runs_the_same_lookup_in_process_and_its_tests_seed_the_approver_rows()
    {
        var extra = Setting();
        extra["DesktopTests"] = "true";
        var (success, errors, read, _) = await RowApprovalsTests.GenerateAsync(withSide: true, stacks: "Api,WinUI3", extra: extra, withApprover: true);

        Assert.IsTrue(success, string.Join(" | ", errors));
        string service = read("Repositories/ApprovalService.cs");
        Expect.Contains(service, "context.Set<Approver>().AsNoTracking().AnyAsync(a => a.EmployeeId == person.Key && (a.TableName == null || a.TableName == \"\" || a.TableName == table))");
        Expect.Contains(service, "ApprovalPeople.RequireApproverAsync(context, \"TimeSheet\")");
        string tests = read("ApprovalServiceTests.cs");
        Expect.Contains(tests, "Make<Approver>(BobApprover)");
        Expect.Contains(tests, "A_row_for_another_table_does_not_let_a_person_decide_this_one");
    }
}
