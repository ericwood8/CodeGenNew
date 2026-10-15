using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> ChangeGates and GuardedColumns: the rule language, what it refuses, and the C# the API template writes from it. </summary>
[TestClass]
public class GatedChangesTests
{
    private const string Gate = "SalesInvoice.InvoiceStatusId; to 2,3,4,5,6; when Customer.CustomerStatusId=2; unless Customer.IgnoreBadStatus=1; start 1";
    private const string Guard = "Customer.IgnoreBadStatus; by Employee.IsManager; stampBy IgnoreBadStatusBy; stampOn IgnoreBadStatusOn";

    private static ProjectSettings Project(params (string Key, string Value)[] values)
    {
        var map = new Dictionary<string, string>
        {
            ["ProjectName"] = "Shop", ["ContextName"] = "ShopContext", ["ApiAuth"] = "true", ["UserTable"] = "Employee", ["UserNameColumn"] = "Login",
            ["PasswordHashColumn"] = "PasswordHash", ["UserRoleColumn"] = "IsManager", ["ChangeGates"] = Gate, ["GuardedColumns"] = Guard
        };
        foreach (var (key, value) in values)
            map[key] = value;
        return ProjectSettings.FromValues(map);
    }

    private static TableModel Customer() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerName", SqlDbType.VarChar, characters: 50, ordinal: 2),
        Sample.Column("CustomerStatusId", SqlDbType.Int, ordinal: 3),
        Sample.Column("IgnoreBadStatus", SqlDbType.Bit, ordinal: 4),
        Sample.Column("IgnoreBadStatusBy", SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 5),
        Sample.Column("IgnoreBadStatusOn", SqlDbType.DateTime, nullable: true, ordinal: 6)
    ]);

    private static TableModel SalesInvoice(params ForeignKeyModel[] extra) => Sample.Table("SalesInvoice",
    [
        Sample.Column("SalesInvoiceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2),
        Sample.Column("BillToCustomerId", SqlDbType.Int, nullable: true, ordinal: 3),
        Sample.Column("InvoiceStatusId", SqlDbType.Int, ordinal: 4),
        Sample.Column("IsPrinted", SqlDbType.Bit, ordinal: 5),
        Sample.Column("Notes", SqlDbType.VarChar, nullable: true, characters: 200, ordinal: 6)
    ], [Sample.ForeignKey("CustomerId", "Customer", "CustomerId", "CustomerName"), .. extra]);

    private static TableModel Employee() => Sample.Table("Employee",
    [
        Sample.Column("EmployeeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.VarChar, characters: 50, ordinal: 2),
        Sample.Column("IsManager", SqlDbType.Bit, ordinal: 3),
        Sample.Column("Login", SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 4),
        Sample.Column("PasswordHash", SqlDbType.VarChar, nullable: true, characters: 200, ordinal: 5)
    ]);

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = (tables.Length == 0 ? [Customer(), SalesInvoice(), Employee()] : tables.ToList()) };

    // ------------------------------------------------------------------ the rule language

    [TestMethod]
    public void A_gate_and_a_guard_are_read_from_the_settings()
    {
        var plan = GatePlan.Resolve(Database(), Project());

        Assert.IsEmpty(plan.Problems, string.Join(" | ", plan.Problems));
        var gate = plan.Gates.Single();
        Assert.AreEqual("InvoiceStatusId", gate.Column.Name);
        Assert.AreEqual(GateOperator.In, gate.To.Operator);
        CollectionAssert.AreEqual(new[] { "2", "3", "4", "5", "6" }, gate.To.Values.ToArray());
        var when = gate.When.Terms.Single();
        Assert.AreEqual("CustomerStatusId", when.Column.Name);
        Assert.AreEqual("Customer", when.Table.TableName);
        Assert.IsNotNull(when.Via);
        Assert.AreEqual("true", gate.Unless!.Terms.Single().Values[0]);
        Assert.AreEqual("1", gate.Start);

        var guard = plan.Guards.Single();
        Assert.AreEqual("IgnoreBadStatus", guard.Column.Name);
        Assert.AreEqual("IsManager", guard.By.Name);
        Assert.AreEqual("IgnoreBadStatusBy", guard.StampBy!.Name);
        Assert.AreEqual("IgnoreBadStatusOn", guard.StampOn!.Name);
    }

    [TestMethod]
    public void Rules_are_split_on_a_bar_outside_quotes_and_the_table_is_found_without_the_schema()
    {
        var rules = GatePlan.RulesOf("A.B; to 1; message \"one | two; three\" | C.D; to 2");

        Assert.HasCount(2, rules);
        Assert.AreEqual("A", GatePlan.TableOf(rules[0]));
        Assert.AreEqual("C", GatePlan.TableOf(rules[1]));
        Assert.IsTrue(GatePlan.HasRules(Project(), SalesInvoice()));
        Assert.IsTrue(GatePlan.HasRules(Project(), Customer()));
        Assert.IsFalse(GatePlan.HasRules(Project(), Employee()));
        Assert.IsFalse(GatePlan.HasRules(Project(("ApiAuth", "false")), Customer()), "without sign-in there is nobody to ask");
    }

    [TestMethod]
    public void And_binds_tighter_than_or_and_the_words_come_out_in_the_message()
    {
        var plan = GatePlan.Resolve(Database(), Project(("ChangeGates", "SalesInvoice.InvoiceStatusId; to > 1; when Customer.CustomerStatusId=2 and IsPrinted=0 or Customer.CustomerStatusId in 3,4; message \"No.\"")));

        var gate = plan.Gates.Single();
        Assert.HasCount(2, gate.When.AnyOf);
        Assert.HasCount(2, gate.When.AnyOf[0]);
        Assert.HasCount(1, gate.When.AnyOf[1]);
        Assert.AreEqual("No.", gate.Message);
        string code = GateCode.Predicate(gate.When, t => $"<{t.Column.Name}>");
        Assert.AreEqual("(<CustomerStatusId> && <IsPrinted>) || <CustomerStatusId>", code);
        Assert.AreEqual("row.InvoiceStatusId > 1", GateCode.Test("row.InvoiceStatusId", gate.To));
        Assert.AreEqual("x is 3 or 4", GateCode.Test("x", gate.When.AnyOf[1][0]));
    }

    [TestMethod]
    [DataRow("Nope.Status; to 1; when Customer.CustomerStatusId=2", "Nope is not a table")]
    [DataRow("SalesInvoice.Nope; to 1; when Customer.CustomerStatusId=2", "SalesInvoice has no column Nope")]
    [DataRow("SalesInvoice.InvoiceStatusId; when Customer.CustomerStatusId=2", "it needs 'to'")]
    [DataRow("SalesInvoice.InvoiceStatusId; to 2", "it needs 'when'")]
    [DataRow("SalesInvoice.InvoiceStatusId; to 2; when Employee.Login='x'", "no single-column foreign key to Employee")]
    [DataRow("SalesInvoice.InvoiceStatusId; to 2; when Customer.Nope=2", "Customer has no column Nope")]
    [DataRow("SalesInvoice.InvoiceStatusId; to 2; when Customer.IgnoreBadStatus=maybe", "yes/no column")]
    [DataRow("SalesInvoice.InvoiceStatusId; to 2.5; when Customer.CustomerStatusId=2", "whole-number")]
    [DataRow("SalesInvoice.InvoiceStatusId; to 2; when Customer.CustomerStatusId ~ 2", "is not Column = value")]
    [DataRow("SalesInvoice.InvoiceStatusId; to 2; when Customer.IgnoreBadStatus > 0", "is not a number")]
    [DataRow("SalesInvoice.InvoiceStatusId; to 2; when Customer.CustomerStatusId=2; soon 3", "is not to, when, unless, start or message")]
    [DataRow("SalesInvoice.SalesInvoiceId; to 2; when Customer.CustomerStatusId=2", "key or a computed column")]
    public void A_rule_that_does_not_fit_is_a_sentence_and_not_a_half_built_rule(string rule, string expected)
    {
        var plan = GatePlan.Resolve(Database(), Project(("ChangeGates", rule), ("GuardedColumns", "")));

        Assert.IsEmpty(plan.Gates);
        Expect.Contains(string.Join(" | ", plan.Problems), expected);
    }

    [TestMethod]
    [DataRow("Customer.IgnoreBadStatus; stampBy IgnoreBadStatusBy", "it needs 'by'")]
    [DataRow("Customer.IgnoreBadStatus; by SalesInvoice.IsPrinted", "must name a column of the user table")]
    [DataRow("Customer.IgnoreBadStatus; by Employee.Name", "must be a yes/no column")]
    [DataRow("Customer.IgnoreBadStatus; by Employee.IsManager; stampBy IgnoreBadStatusOn", "stampBy IgnoreBadStatusOn must be a text column")]
    [DataRow("Customer.IgnoreBadStatus; by Employee.IsManager; stampOn IgnoreBadStatusBy", "stampOn IgnoreBadStatusBy must be a date column")]
    [DataRow("Employee.IsManager; by Employee.IsManager", "is the user table")]
    public void A_guard_that_does_not_fit_is_a_sentence(string rule, string expected)
    {
        var plan = GatePlan.Resolve(Database(), Project(("ChangeGates", ""), ("GuardedColumns", rule)));

        Assert.IsNotEmpty(plan.Problems);
        Expect.Contains(string.Join(" | ", plan.Problems), expected);
    }

    [TestMethod]
    public void Two_foreign_keys_to_the_same_parent_are_ambiguous_and_the_rules_need_ApiAuth()
    {
        var twoKeys = SalesInvoice(Sample.ForeignKey("BillToCustomerId", "Customer", "CustomerId", "CustomerName"));
        var plan = GatePlan.Resolve(Database(Customer(), twoKeys, Employee()), Project());
        Expect.Contains(string.Join(" | ", plan.Problems), "2 foreign keys to Customer");

        var noAuth = GatePlan.Resolve(Database(), Project(("ApiAuth", "false")));
        Expect.Contains(string.Join(" | ", noAuth.Problems), "need ApiAuth=true");
        Assert.IsEmpty(GatePlan.Resolve(Database(), Project(("ChangeGates", ""), ("GuardedColumns", ""))).Problems);
    }

    [TestMethod]
    public void The_gate_template_is_in_the_plan_only_with_a_rule_and_sign_in()
    {
        CollectionAssert.Contains(Project().ImpliedPlanTemplates.ToList(), "API_Gates");
        CollectionAssert.DoesNotContain(Project(("ChangeGates", ""), ("GuardedColumns", "")).ImpliedPlanTemplates.ToList(), "API_Gates");
    }

    // ------------------------------------------------------------------ the generated checks

    private static async Task<string> Gates(ProjectSettings project, DatabaseModel? database = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Gates_v1.tt"), database ?? Database(), project);
        if (!result.Success)
            Assert.Fail(string.Join(" | ", result.Errors).ReplaceLineEndings(" "));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task The_gate_is_written_as_a_check_before_the_row_is_saved()
    {
        string text = await Gates(Project());

        Expect.Contains(text, "public static class SalesInvoiceGates");
        Expect.Contains(text, "public static async Task<IResult?> CheckAsync(ShopContext context, ClaimsPrincipal caller, SalesInvoice row, bool isNew)");
        Expect.Contains(text, "if (row.InvoiceStatusId != 1)");   // a new row always starts as 1
        Expect.Contains(text, "return Results.ValidationProblem(");
        Expect.Contains(text, "moves = before is not null && !Equals(before.InvoiceStatusId, row.InvoiceStatusId);");
        Expect.Contains(text, "if (moves && row.InvoiceStatusId is 2 or 3 or 4 or 5 or 6)");
        Expect.Contains(text, "var parent_CustomerId = await context.Set<Customer>().AsNoTracking().Where(p => p.CustomerId == row.CustomerId)");
        Expect.Contains(text, ".Select(p => new { p.CustomerStatusId, p.IgnoreBadStatus }).FirstOrDefaultAsync();");
        Expect.Contains(text, "if (((parent_CustomerId is not null && parent_CustomerId.CustomerStatusId == 2)) && !((parent_CustomerId is not null && parent_CustomerId.IgnoreBadStatus == true)))");
        Expect.Contains(text, "statusCode: 409, type: \"" + ProblemTypes.ChangeNotAllowed + "\"");
        Expect.Contains(text, "Invoice Status cannot change to 2, 3, 4, 5, 6 while Customer Status = 2 unless Customer Ignore Bad Status = true.");
    }

    [TestMethod]
    public async Task The_guard_checks_the_caller_stamps_the_change_and_keeps_the_stored_stamp_otherwise()
    {
        string text = await Gates(Project());

        Expect.Contains(text, "public static class CustomerGates");
        Expect.Contains(text, "bool changed = isNew ? row.IgnoreBadStatus == true : !Equals(row.IgnoreBadStatus, stored1!.IgnoreBadStatus);");
        Expect.Contains(text, "if (changed && !await MayIgnoreBadStatusAsync(context, caller))");
        Expect.Contains(text, "statusCode: 403, type: \"" + ProblemTypes.ColumnNotYours + "\"");
        Expect.Contains(text, "row.IgnoreBadStatusBy = caller.FindFirstValue(AuthNames.NameClaim);");
        Expect.Contains(text, "row.IgnoreBadStatusOn = DateTime.Now;");
        Expect.Contains(text, "row.IgnoreBadStatusBy = isNew ? default! : stored1!.IgnoreBadStatusBy;");   // a caller cannot forge the stamp by sending it
        Expect.Contains(text, "if (caller.IsInRole(AuthNames.AdminRole))");
        Expect.Contains(text, "context.Set<Employee>().AsNoTracking().Where(u => u.Login == name).Select(u => u.IsManager == true).FirstOrDefaultAsync()");
        Expect.Contains(text, "app.MapGet(\"/api/customers/guards\"");
        Expect.Contains(text, "[\"ignoreBadStatus\"] = await CustomerGates.MayIgnoreBadStatusAsync(context, caller),");
        Expect.Contains(text, ".RequireAuthorization();");
    }

    [TestMethod]
    public async Task A_rule_that_does_not_fit_stops_the_template_with_the_reason()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Gates_v1.tt"), Database(), Project(("ChangeGates", "SalesInvoice.Nope; to 2; when IsPrinted=1")));

        Assert.IsFalse(result.Success);
        Expect.Contains(string.Join(" | ", result.Errors), "SalesInvoice has no column Nope");
    }

    private static async Task<string> Api(TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Crud_v1.tt"), table, project);
        if (!result.Success)
            Assert.Fail(string.Join(" | ", result.Errors).ReplaceLineEndings(" "));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task Only_a_table_with_a_rule_calls_its_checks()
    {
        string invoice = await Api(SalesInvoice(), Project());
        Expect.Contains(invoice, "protected override Task<IResult?> BeforeSaveAsync(ShopContext context, SalesInvoice row, bool isNew) => SalesInvoiceGates.CheckAsync(context, Caller, row, isNew);");

        string plain = await Api(Customer(), Project(("GuardedColumns", ""), ("ChangeGates", "")));
        Expect.DoesNotContain(plain, "Gates");
        Expect.DoesNotContain(plain, "BeforeSaveAsync");

        string noAuth = await Api(SalesInvoice(), Project(("ApiAuth", "false")));
        Expect.DoesNotContain(noAuth, "Gates");
    }

    [TestMethod]
    public async Task A_clone_starts_like_a_new_row_and_does_not_carry_a_guarded_value_over()
    {
        string text = await Gates(Project());

        Expect.Contains(text, "public static async Task<IResult?> AfterCloneAsync(ShopContext context, ClaimsPrincipal caller, int newId)");
        Expect.Contains(text, "copy.InvoiceStatusId = 1;");
        Expect.Contains(text, "copy.IgnoreBadStatus = default!;");
        Expect.Contains(text, "copy.IgnoreBadStatusBy = default!;");
        Expect.Contains(text, "if (await GateInvoiceStatusIdAsync(context, copy, true) is { } refusedInvoiceStatusId)");
        Expect.Contains(text, "context.Remove(copy);");

        string invoice = await Api(SalesInvoice(), Project());
        bool clones = invoice.Contains("protected override bool CanClone", StringComparison.Ordinal);
        Assert.AreEqual(clones, invoice.Contains("AfterCloneAsync", StringComparison.Ordinal), "the API calls the clone check exactly when it clones");
    }

    [TestMethod]
    public async Task The_csv_import_into_a_table_with_a_rule_is_for_the_administrator_only()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Csv_v1.tt"), Database(), Project(("ApiCsv", "true")));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string csv = result.GeneratedText!.ReplaceLineEndings("\n");

        string invoiceLine = csv.Split('\n').Single(line => line.Contains("MapTable<SalesInvoice, ShopContext>", StringComparison.Ordinal));
        string employeeLine = csv.Split('\n').Single(line => line.Contains("MapTable<Employee, ShopContext>", StringComparison.Ordinal));
        Assert.EndsWith(", false, true);", invoiceLine);
        Assert.EndsWith(", false, false);", employeeLine);
        Expect.Contains(csv, ".WithName(\"Import\" + file + \"Csv\").AdminOnly(adminOnly || gated);");
    }

    [TestMethod]
    public async Task The_registration_maps_the_guards_route_only_when_a_guard_exists()
    {
        async Task<string> Registration(ProjectSettings project)
        {
            var result = await Repo.Cache.RunAsync(Repo.Template("API_Registration_v1.tt"), Database(), project);
            Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
            return result.GeneratedText!.ReplaceLineEndings("\n");
        }

        Expect.Contains(await Registration(Project()), "GatesApi.Register(app);");
        Expect.DoesNotContain(await Registration(Project(("GuardedColumns", ""))), "GatesApi");
    }

    // ------------------------------------------------------------------ the screens

    private static TableModel CustomerWithChildren() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerName", SqlDbType.VarChar, characters: 50, ordinal: 2),
        Sample.Column("IgnoreBadStatus", SqlDbType.Bit, ordinal: 3),
        Sample.Column("IgnoreBadStatusBy", SqlDbType.VarChar, nullable: true, characters: 50, ordinal: 4),
        Sample.Column("IgnoreBadStatusOn", SqlDbType.DateTime, nullable: true, ordinal: 5)
    ], [], childForeignKeys: [Sample.ChildForeignKey("SalesInvoice", "CustomerId", "CustomerId", childOwnPrimaryKey: ["SalesInvoiceId"])]);

    private static async Task<Dictionary<string, string>> Angular(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => f.RelativePath.EndsWith(".spec.ts") ? "spec" : Path.GetExtension(f.RelativePath).TrimStart('.'), f => f.Content.Replace("\r\n", "\n"));
    }

    [TestMethod]
    [DataRow("TS_Component_v1.tt", false)]
    [DataRow("TS_DetailMasterComponent_v1.tt", true)]
    public async Task A_guarded_field_is_a_control_for_a_person_who_may_change_it_and_text_with_the_reason_for_everyone_else(string template, bool withChildren)
    {
        var table = withChildren ? CustomerWithChildren() : Customer();
        var files = await Angular(template, table, Project(("AngularVersion", "18")));

        string html = files["html"];
        Expect.Contains(html, "guards['ignoreBadStatus']");
        Expect.Contains(html, "Only a manager can change this.");
        Expect.Contains(html, "selectedRow.ignoreBadStatus ? 'Yes' : 'No'");
        Expect.Contains(html, "[(ngModel)]=\"selectedRow.ignoreBadStatus\"");
        Expect.DoesNotContain(files["ts"], "ignoreBadStatusOn.substring");   // the stamp is shown with its time, so the edit screen must not cut it to a date
        Expect.Contains(html, "selectedRow.ignoreBadStatusBy || 'not recorded'");
        Expect.Contains(html, "(selectedRow.ignoreBadStatusOn | date:'medium') : 'not recorded'");
        Expect.DoesNotContain(html, "[(ngModel)]=\"selectedRow.ignoreBadStatusBy\"");
        Expect.Contains(files["ts"], "guards: Record<string, boolean> = {};");
        Expect.Contains(files["ts"], "this.service.getGuards().subscribe((data) => { this.guards = data; });");

        var plain = await Angular(template, table, Project(("AngularVersion", "18"), ("GuardedColumns", "")));
        Expect.DoesNotContain(plain["html"], "guards[");
        Expect.DoesNotContain(plain["ts"], "getGuards");
        Expect.Contains(plain["html"], "[(ngModel)]=\"selectedRow.ignoreBadStatusBy\"");   // without the guard the stamp column is an ordinary field
    }

    private static async Task<Dictionary<string, string>> Written(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => f.RelativePath.Replace('\\', '/'), f => f.Content.Replace("\r\n", "\n"));
    }

    private static string Of(Dictionary<string, string> files, string endsWith) => files.Single(f => f.Key.EndsWith(endsWith)).Value;

    [TestMethod]
    [DataRow("TSX_Page_v1.tt", false)]
    [DataRow("TSX_DetailMasterPage_v1.tt", true)]
    public async Task A_React_form_shows_a_guarded_field_as_a_control_only_to_a_person_who_may_change_it(string template, bool withChildren)
    {
        var table = withChildren ? CustomerWithChildren() : Customer();
        var files = await Written(template, table, Project());
        string page = Of(files, "Page.tsx");

        Expect.Contains(page, "const [guards, setGuards] = useState<Record<string, boolean>>({});");
        Expect.Contains(page, "customerApi.getGuards().then(setGuards).catch(() => {});");
        Expect.Contains(page, "{guards['ignoreBadStatus'] ? (");
        Expect.Contains(page, "selectedRow.ignoreBadStatus ? 'Yes' : 'No'");
        Expect.Contains(page, "Only a manager can change this.");
        Expect.Contains(page, "checked={!!selectedRow.ignoreBadStatus}");
        Expect.Contains(page, "selectedRow.ignoreBadStatusBy || 'not recorded'");
        Expect.Contains(page, "new Date(selectedRow.ignoreBadStatusOn).toLocaleString() : 'not recorded'");
        Expect.DoesNotContain(page, "selectedRow.ignoreBadStatusBy}");   // the stamp is never an input

        var plain = Of(await Written(template, table, Project(("GuardedColumns", ""))), "Page.tsx");
        Expect.DoesNotContain(plain, "guards");
        Expect.DoesNotContain(plain, "getGuards");
    }

    [TestMethod]
    public async Task The_React_page_spec_checks_both_forms_of_a_guarded_field()
    {
        var spec = Of(await Written("TSX_Page_v1.tt", Customer(), Project()), ".test.tsx");
        Expect.Contains(spec, "getGuards').mockResolvedValue({ ignoreBadStatus: false })");
        Expect.Contains(spec, "getGuards').mockResolvedValue({ ignoreBadStatus: true })");
        Expect.Contains(spec, "findByText('Only a manager can change this.')");
        Expect.Contains(spec, "import { fireEvent, render, screen }");
    }

    [TestMethod]
    public async Task The_React_api_module_asks_for_the_guards_only_for_a_table_that_has_one()
    {
        Expect.Contains(Of(await Written("TSX_Api_v1.tt", Customer(), Project()), "customerApi.ts"), "getGuards: () => request<Record<string, boolean>>(`${apiUrl}/guards`),");
        Expect.DoesNotContain(Of(await Written("TSX_Api_v1.tt", SalesInvoice(), Project()), "salesinvoiceApi.ts"), "getGuards");
    }

    [TestMethod]
    public async Task A_Blazor_form_shows_a_guarded_field_as_a_control_only_to_a_person_who_may_change_it()
    {
        var files = await Written("BLZ_Page_v1.tt", Customer(), Project(("Auth", "true")));
        string page = Of(files, "CustomerPage.razor");

        Expect.Contains(page, "private Dictionary<string, bool> guards = new();");
        Expect.Contains(page, "guards = await Client.GetGuardsAsync();");
        Expect.Contains(page, "@if (guards.GetValueOrDefault(\"ignoreBadStatus\"))");
        Expect.Contains(page, "@(row.IgnoreBadStatus == true ? \"Yes\" : \"No\")");
        Expect.Contains(page, "Only a manager can change this.");
        Expect.Contains(page, "@(row.IgnoreBadStatusBy ?? \"not recorded\")");
        Expect.Contains(page, "@(row.IgnoreBadStatusOn?.ToString(\"g\") ?? \"not recorded\")");
        Expect.DoesNotContain(page, "@bind-Value=\"row.IgnoreBadStatusBy\"");

        var plain = Of(await Written("BLZ_Page_v1.tt", Customer(), Project(("Auth", "true"), ("GuardedColumns", ""))), "CustomerPage.razor");
        Expect.DoesNotContain(plain, "guards");
    }

    [TestMethod]
    public async Task The_Blazor_clients_ask_for_the_guards_only_for_a_table_that_has_one()
    {
        Expect.Contains(Of(await Written("BLZ_Client_v1.tt", Customer(), Project()), "CustomerClient.cs"), "GetGuardsAsync()");
        Expect.DoesNotContain(Of(await Written("BLZ_Client_v1.tt", SalesInvoice(), Project()), "SalesInvoiceClient.cs"), "GetGuardsAsync");
    }

    [TestMethod]
    public async Task The_service_asks_for_the_guards_only_for_a_table_that_has_one()
    {
        var withGuard = await Angular("TS_Service_v1.tt", Customer(), Project());
        Expect.Contains(withGuard["ts"], "getGuards(): Observable<Record<string, boolean>>");
        Expect.Contains(withGuard["ts"], "this.http.get<Record<string, boolean>>(`${this.apiUrl}/guards`)");

        var without = await Angular("TS_Service_v1.tt", SalesInvoice(), Project());
        Expect.DoesNotContain(without["ts"], "getGuards");
    }

    [TestMethod]
    public void The_sentence_for_a_guarded_field_names_the_role_when_the_column_is_a_role_flag()
    {
        Assert.AreEqual("Only a manager can change this.", GatePlan.GuardNote("IsManager"));
        Assert.AreEqual("Only people with Can Approve can change this.", GatePlan.GuardNote("CanApprove"));
        var hint = GatePlan.GuardsOf(Project(), Customer()).Single();
        Assert.AreEqual("ignoreBadStatus", hint.Json);
        CollectionAssert.AreEqual(new[] { "IgnoreBadStatusBy", "IgnoreBadStatusOn" }, hint.Stamps.ToArray());
        Assert.IsEmpty(GatePlan.GuardsOf(Project(), SalesInvoice()));
        Assert.IsEmpty(GatePlan.GuardsOf(Project(("ApiAuth", "false")), Customer()));
    }

    [TestMethod]
    public void The_settings_are_on_the_sign_in_tab_with_a_hint()
    {
        foreach (string key in new[] { "ChangeGates", "GuardedColumns" })
        {
            CollectionAssert.Contains(ProjectSettings.Keys.ToList(), key);
            Assert.IsTrue(ProjectSettingsHints.All.ContainsKey(key), key + " has a hint");
            Assert.AreEqual("Sign-in", ProjectSettingGroups.TabOf(key));
        }
    }
}
