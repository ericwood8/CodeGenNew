using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> Validation that is enforced: the real attributes and a browser check for a text column named like an email, a phone number or a URL, the project's own list of columns
/// that hold something else, and the guard clauses of the routines for a rule the database does not state. </summary>
[TestClass]
public class EnforcedValidationTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Contact(SqlDialect dialect = SqlDialect.SqlServer) => Sample.Table("Contact",
    [
        Sample.Column("ContactId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("Email", SqlDbType.NVarChar, nullable: true, characters: 100, ordinal: 3),
        Sample.Column("Phone", SqlDbType.NVarChar, nullable: true, characters: 30, ordinal: 4),
        Sample.Column("Website", SqlDbType.NVarChar, nullable: true, characters: 200, ordinal: 5),
        Sample.Column("EmailTemplate", SqlDbType.NVarChar, nullable: true, characters: 200, ordinal: 6),
        Sample.Column("Latitude", SqlDbType.Decimal, nullable: true, precision: 9, scale: 6, ordinal: 7),
        Sample.Column("Balance", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 8, currency: true),
        Sample.Column("Quantity", SqlDbType.Int, ordinal: 9),
        Sample.Column("Discount", SqlDbType.Decimal, precision: 5, scale: 2, ordinal: 10, check: new CheckRange(0, false, null, false)),
        Sample.Column("BirthDate", SqlDbType.Date, nullable: true, ordinal: 11)
    ], dialect: dialect);

    private static async Task<string> Rendered(string template, TableModel table, ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project);
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static readonly (string Key, string Value)[] Guarded =
        [("NonNegativeColumns", "Balance,Quantity,Discount"), ("NotFutureDateColumns", "Contact.BirthDate")];

    // ------------------------------------------------------------------ the classification

    [TestMethod]
    public void A_column_the_project_calls_plain_text_gets_no_format_check_and_the_other_columns_keep_theirs()
    {
        var table = Contact();
        var project = Project(("PlainTextColumns", "EmailTemplate"));

        Assert.AreEqual(TextShape.Email, table.Columns.Single(c => c.Name == "Email").RuleOf(project, "Contact")!.Shape);
        Assert.AreEqual(TextShape.Email, table.Columns.Single(c => c.Name == "EmailTemplate").RuleOf(Project(), "Contact")!.Shape, "named like an email, so checked unless the project says otherwise");
        Assert.AreEqual(TextShape.None, table.Columns.Single(c => c.Name == "EmailTemplate").RuleOf(project, "Contact")!.Shape);

        var oneTable = Project(("PlainTextColumns", "Other.EmailTemplate"));
        Assert.AreEqual(TextShape.Email, table.Columns.Single(c => c.Name == "EmailTemplate").RuleOf(oneTable, "Contact")!.Shape, "Table.Column names one table's column only");
        Assert.AreEqual(TextShape.None, table.Columns.Single(c => c.Name == "EmailTemplate").RuleOf(Project(("PlainTextColumns", "Contact.EmailTemplate")), "Contact")!.Shape);
    }

    [TestMethod]
    public void A_column_the_project_says_is_never_negative_gets_a_minimum_of_zero_in_the_shared_rule()
    {
        var table = Contact();
        var project = Project(Guarded);

        var balance = table.Columns.Single(c => c.Name == "Balance").RuleOf(project, "Contact")!;
        Assert.AreEqual(0, balance.Min);
        Assert.IsNull(balance.Max);
        Assert.AreEqual(0, table.Columns.Single(c => c.Name == "Quantity").RuleOf(project, "Contact")!.Min);
        Assert.IsNull(table.Columns.Single(c => c.Name == "Balance").RuleOf(Project(), "Contact"), "without the list the schema says nothing");
    }

    // ------------------------------------------------------------------ the C# attributes and validator

    [TestMethod]
    public async Task The_validation_class_has_the_real_attributes_and_leaves_out_what_the_project_calls_plain_text()
    {
        string cs = await Rendered("CS_Validation_v1.tt", Contact(), Project(("PlainTextColumns", "EmailTemplate")));

        Expect.Contains(cs, "    [EmailAddress]\n    public string? Email { get; set; }");
        Expect.Contains(cs, "    [Phone]\n    public string? Phone { get; set; }");
        Expect.Contains(cs, "    [Url]\n    public string? Website { get; set; }");
        Expect.Contains(cs, "    [Range(-90, 90)]\n    public decimal? Latitude { get; set; }");
        Expect.DoesNotContain(cs, "[EmailAddress]\n    public string? EmailTemplate");
        Expect.DoesNotContain(cs, "DataType.EmailAddress");
    }

    [TestMethod]
    public async Task The_validator_refuses_a_negative_value_for_a_column_the_project_lists_without_asking_for_an_upper_limit()
    {
        string validator = await Rendered("CS_Validator_v1.tt", Contact(), Project(Guarded));

        Expect.Contains(validator, "RuleFor(x => x.Balance).GreaterThanOrEqualTo(0m);");
        Expect.Contains(validator, "RuleFor(x => x.Quantity).GreaterThanOrEqualTo(0);");
        Expect.Contains(validator, "RuleFor(x => x.BirthDate).Must(value => value is null || value.Value.Date <= DateTime.Today).WithMessage(\"Birth Date must not be later than today.\");");
        Expect.DoesNotContain(await Rendered("CS_Validator_v1.tt", Contact(), Project()), "RuleFor(x => x.Balance)");
    }

    // ------------------------------------------------------------------ the web forms

    [TestMethod]
    public async Task The_web_forms_give_a_text_box_the_type_its_name_says_and_the_browser_checks_it()
    {
        var table = Contact();
        var project = Project(("PlainTextColumns", "EmailTemplate"));

        string react = await Rendered("TSX_Page_v1.tt", table, project);
        Expect.Contains(react, "              type=\"email\"");
        Expect.Contains(react, "              type=\"tel\"\n              pattern=\"" + ColumnRules.PhonePattern + "\"");
        Expect.Contains(react, "              type=\"url\"");

        string angular = await Rendered("TS_Component_v1.tt", table, project);
        Expect.Contains(angular, "<input type=\"email\" id=\"contactEmail\"");
        Expect.Contains(angular, "<input type=\"tel\" id=\"contactPhone\"");
        Expect.Contains(angular, "pattern=\"" + ColumnRules.PhonePattern + "\"");
        Expect.Contains(angular, "<input type=\"url\" id=\"contactWebsite\"");
        Expect.Contains(angular, "<textarea id=\"contactEmailTemplate\"");

        string blazor = await Rendered("BLZ_Page_v1.tt", table, project);
        Expect.Contains(blazor, "<InputText id=\"contactEmail\" @bind-Value=\"row.Email\" type=\"email\"");
        Expect.Contains(blazor, " type=\"tel\" pattern=\"" + ColumnRules.PhonePattern + "\"");
        Expect.Contains(blazor, " type=\"url\"");
        Expect.DoesNotContain(blazor, "id=\"emailTemplate\" @bind-Value=\"row.EmailTemplate\" type=");
    }

    // ------------------------------------------------------------------ the guard clauses of the routines

    [TestMethod]
    public void A_value_guard_is_written_for_a_listed_column_unless_the_database_already_states_a_minimum()
    {
        var guards = ValueGuards.Of(Contact(), Project(Guarded));

        CollectionAssert.AreEqual(new[] { "Balance", "Quantity", "BirthDate" }, guards.Select(g => g.Column.Name).ToArray(), "Discount has a CHECK of its own");
        Assert.AreEqual(ValueGuardKind.NotFuture, guards.Single(g => g.Column.Name == "BirthDate").Kind);
        Assert.IsEmpty(ValueGuards.Of(Contact(), Project()).ToList(), "no list, no guard");
        Assert.IsEmpty(ValueGuards.Of(Contact(), null).ToList());
        Assert.IsEmpty(ValueGuards.Of(Contact(), Project(("NotFutureDateColumns", "Contact.Name"))).ToList(), "a text column is no date");
    }

    [TestMethod]
    public async Task The_sql_server_routines_refuse_a_listed_value_before_they_write()
    {
        var project = Project(Guarded);

        string insert = await Rendered("SP_Insert_v1.tt", Contact(), project);
        Expect.Contains(insert, "IF (@pBalance IS NOT NULL AND @pBalance < 0)");
        Expect.Contains(insert, "RAISERROR('[Balance] must not be negative.', 16, 1);");
        Expect.Contains(insert, "IF (@pBirthDate IS NOT NULL AND CAST(@pBirthDate AS DATE) > CAST(GETDATE() AS DATE))");
        Expect.Contains(insert, "RAISERROR('[BirthDate] must not be later than today.', 16, 1);");
        Expect.DoesNotContain(insert, "[Discount] must not be negative");
        Assert.IsLessThan(insert.IndexOf("INSERT INTO", StringComparison.Ordinal), insert.IndexOf("[Balance] must not be negative", StringComparison.Ordinal), "the guard comes before the insert");

        string update = await Rendered("SP_Update_v1.tt", Contact(), project);
        Expect.Contains(update, "IF (@pBalance IS NOT NULL AND TRY_CAST(@pBalance AS decimal(38, 10)) < 0)");
        Expect.Contains(update, "IF (@pBirthDate IS NOT NULL AND TRY_CAST(@pBirthDate AS DATE) > CAST(GETDATE() AS DATE))");

        string save = await Rendered("SP_Save_v1.tt", Contact(), project);
        Expect.Contains(save, "IF (@pQuantity IS NOT NULL AND @pQuantity < 0)");
        Expect.Contains(save, "'[Quantity] must not be negative.', 1;");

        foreach (string template in new[] { "SP_Insert_v1.tt", "SP_Update_v1.tt", "SP_Save_v1.tt" })
            Expect.DoesNotContain(await Rendered(template, Contact(), Project()), "must not be negative");
    }

    [TestMethod]
    public async Task The_postgresql_and_mysql_routines_raise_the_same_refusals()
    {
        var project = Project(Guarded);

        string pgInsert = await Rendered("SP_Insert_v1.tt", Contact(SqlDialect.PostgreSql), project);
        Expect.Contains(pgInsert, "IF (\"pBalance\" IS NOT NULL AND \"pBalance\" < '0') THEN");
        Expect.Contains(pgInsert, "RAISE EXCEPTION '\"Balance\" must not be negative.';");
        Expect.Contains(pgInsert, "\"pBirthDate\"::date > current_date");

        string pgUpdate = await Rendered("SP_Update_v1.tt", Contact(SqlDialect.PostgreSql), project);
        Expect.Contains(pgUpdate, "RAISE EXCEPTION '\"Quantity\" must not be negative.';");
        string pgSave = await Rendered("SP_Save_v1.tt", Contact(SqlDialect.PostgreSql), project);
        Expect.Contains(pgSave, "RAISE EXCEPTION '\"BirthDate\" must not be later than today.';");

        string myInsert = await Rendered("SP_Insert_v1.tt", Contact(SqlDialect.MySql), project);
        Expect.Contains(myInsert, "IF (`pBalance` IS NOT NULL AND `pBalance` < 0) THEN");
        Expect.Contains(myInsert, "SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = '`Balance` must not be negative.';");
        Expect.Contains(myInsert, "DATE(`pBirthDate`) > CURDATE()");
        Expect.Contains(await Rendered("SP_Update_v1.tt", Contact(SqlDialect.MySql), project), "`Quantity` must not be negative.");
        Expect.Contains(await Rendered("SP_Save_v1.tt", Contact(SqlDialect.MySql), project), "`BirthDate` must not be later than today.");

        Expect.DoesNotContain(await Rendered("SP_Insert_v1.tt", Contact(SqlDialect.PostgreSql), Project()), "must not be negative");
    }
}
