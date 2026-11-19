using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;

namespace CodeGenNew.Tests.Shapes;

/// <summary> The shapes a table can have that the project names in a setting, asked of <see cref="TableShapes"/>, and the shapes the detectors offer for the right-click menu: the log, the single-row table, the database's own
/// versioning, the owner column, row approvals, the exchange rate on a document, and the project-wide choices that are offered on the table that holds them. </summary>
[TestClass]
public class MoreTableShapeTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" }.Concat(values.Select(v => KeyValuePair.Create(v.Key, v.Value))).ToDictionary());

    private static ColumnModel Key(string name) => Sample.Column(name, SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1);

    private static ShapeSuggestion? Find(TableModel table, string shape, params (string, string)[] project) =>
        TableShapeDetector.Of(table, Project(project)).FirstOrDefault(s => s.Shape == shape);

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [.. tables] };

    private static TableModel AuditEvent() => Sample.Table("AuditEvent",
    [
        Key("AuditEventId"), Sample.Column("OccurredOn", SqlDbType.DateTime, ordinal: 2), Sample.Column("Level", SqlDbType.NVarChar, characters: 10, ordinal: 3), Sample.Column("Message", SqlDbType.NVarChar, characters: 200, ordinal: 4)
    ]);

    [TestMethod]
    public void A_table_that_looks_like_a_log_is_suggested_for_LogTables()
    {
        var log = Find(AuditEvent(), "Log table")!;
        Assert.IsTrue(log.Fits, log.Note);
        Assert.AreEqual("LogTables", log.Setting);
        Assert.AreEqual("AuditEvent", log.Item);
        Assert.IsNull(Find(AuditEvent(), "Log table", ("LogTables", "AuditEvent")), "a listed table is not suggested again");
    }

    [TestMethod]
    public void A_log_needs_the_EF_search_to_fit()
    {
        var log = TableShapeDetector.Of(AuditEvent(), ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Routines" })).First(s => s.Shape == "Log table");
        Assert.IsFalse(log.Fits);
        StringAssert.Contains(log.Note, "AccessMode=Ef");
    }

    [TestMethod]
    public void A_table_with_a_claim_stamp_is_a_queue_and_not_a_log()
    {
        var queue = Sample.Table("Job",
        [
            Key("JobId"), Sample.Column("Status", SqlDbType.NVarChar, characters: 20, ordinal: 2), Sample.Column("RetryCount", SqlDbType.Int, ordinal: 3), Sample.Column("CreatedOn", SqlDbType.DateTime, ordinal: 4),
            Sample.Column("ClaimedOn", SqlDbType.DateTime, nullable: true, ordinal: 5), Sample.Column("ProcessedOn", SqlDbType.DateTime, nullable: true, ordinal: 6)
        ]);
        Assert.IsNotNull(Find(queue, "Work queue"));
        Assert.IsNull(Find(queue, "Log table"));
    }

    private static TableModel Settings(long rows) => Sample.Table("CompanySetting",
    [
        Key("CompanySettingId"), Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2), Sample.Column("Address", SqlDbType.NVarChar, characters: 100, ordinal: 3),
        Sample.Column("Phone", SqlDbType.NVarChar, characters: 20, ordinal: 4), Sample.Column("Email", SqlDbType.NVarChar, characters: 80, ordinal: 5)
    ], rowCount: rows);

    [TestMethod]
    public void A_table_with_one_row_is_suggested_as_a_single_row_table()
    {
        var single = Find(Settings(1), "Single-row table")!;
        Assert.IsTrue(single.Fits);
        Assert.AreEqual("SingleRowTables", single.Setting);
        Assert.AreEqual("CompanySetting", single.Item);
        Assert.IsNull(Find(Settings(2), "Single-row table"));
        Assert.IsNull(Find(Settings(1), "Single-row table", ("SingleRowTables", "CompanySetting")));
    }

    [TestMethod]
    public void A_table_the_database_versions_itself_is_suggested_for_TemporalTables_on_SQL_Server_only()
    {
        TableModel Versioned(SqlDialect dialect) => Sample.Table("Price", [Key("PriceId"), Sample.Column("Amount", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 2)], dialect: dialect, systemVersioned: true);
        var temporal = Find(Versioned(SqlDialect.SqlServer), "System-versioned table")!;
        Assert.IsTrue(temporal.Fits);
        Assert.AreEqual("TemporalTables", temporal.Setting);
        Assert.IsNull(Find(Versioned(SqlDialect.SqlServer), "System-versioned table", ("TemporalTables", "Price")));
        Assert.IsNull(Find(Versioned(SqlDialect.MySql), "System-versioned table"));
    }

    [TestMethod]
    public void A_table_with_the_owner_column_is_suggested_for_OwnedTables_when_the_API_signs_people_in()
    {
        var note = Sample.Table("Note", [Key("NoteId"), Sample.Column("EmployeeId", SqlDbType.Int, ordinal: 2), Sample.Column("Text", SqlDbType.NVarChar, characters: 200, ordinal: 3)]);
        var owned = Find(note, "Owned by a person", ("ApiAuth", "true"))!;
        Assert.IsTrue(owned.Fits, owned.Note);
        Assert.AreEqual("OwnedTables", owned.Setting);
        Assert.IsNull(Find(note, "Owned by a person"), "without ApiAuth nobody is signed in");
        Assert.IsNull(Find(note, "Owned by a person", ("ApiAuth", "true"), ("OwnedTables", "Note")));
    }

    [TestMethod]
    public void The_shapes_the_project_names_are_asked_of_the_table()
    {
        var settings = Settings(1);
        var shapes = TableShapes.For(settings, Project(("SingleRowTables", "CompanySetting"), ("TemporalTables", "CompanySetting"), ("KeySequenceTables", "CompanySetting")));
        Assert.IsNotNull(shapes.SingleRow);
        Assert.IsNotNull(shapes.Temporal);
        Assert.IsNotNull(shapes.KeySequence);
        Assert.IsNull(shapes.Rate);
        var plain = TableShapes.For(settings, Project());
        Assert.IsNull(plain.SingleRow);
        Assert.IsNull(plain.Temporal);
        Assert.IsNull(plain.KeySequence);
    }

    [TestMethod]
    public void A_rate_table_has_the_rate_shape()
    {
        var rates = Sample.Table("ExchangeRate",
        [
            Key("ExchangeRateId"), Sample.Column("FromCurrency", SqlDbType.VarChar, characters: 3, ordinal: 2), Sample.Column("ToCurrency", SqlDbType.VarChar, characters: 3, ordinal: 3),
            Sample.Column("Rate", SqlDbType.Decimal, precision: 18, scale: 6, ordinal: 4), Sample.Column("RateDate", SqlDbType.Date, ordinal: 5)
        ]);
        Assert.IsNotNull(TableShapes.For(rates, Project(("RateTables", "ExchangeRate"))).Rate);
        Assert.IsNull(TableShapes.For(rates, Project()).Rate);
    }

    [TestMethod]
    public void A_shape_that_reads_other_tables_needs_the_database_and_a_plain_table_has_none_of_them()
    {
        var table = Settings(1);
        Assert.ThrowsExactly<InvalidOperationException>(() => TableShapes.For(table, Project()).Has<SnapshotShape>());
        var shapes = TableShapes.For(table, Project(), Database(table));
        Assert.IsNull(shapes.Snapshot);
        Assert.IsNull(shapes.RateDocument);
        Assert.IsNull(shapes.PartyRoot);
        Assert.IsNull(shapes.PartyIdentifiers);
        Assert.IsNull(shapes.Link);
        Assert.IsNull(shapes.Linked);
        Assert.IsNull(shapes.Archive);
        Assert.IsNull(shapes.Partition);
        Assert.IsNull(shapes.TaxCategory);
        Assert.IsNull(shapes.Permission);
        Assert.IsNull(shapes.Feature);
        Assert.IsNull(shapes.Subscription);
        Assert.IsNull(shapes.SubscriptionPlan);
        Assert.IsNull(shapes.PlanLimit);
        Assert.AreEqual(0, shapes.All.Count);
    }

    private static TableModel Employee() => Sample.Table("Employee",
    [
        Key("EmployeeId"), Sample.Column("UserName", SqlDbType.NVarChar, characters: 50, ordinal: 2), Sample.Column("PasswordHash", SqlDbType.NVarChar, characters: 200, ordinal: 3), Sample.Column("IsManager", SqlDbType.Bit, ordinal: 4)
    ]);

    private static TableModel Approvable() => Sample.Table("Expense", [Key("ExpenseId"), Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2)]);

    private static TableModel ApprovalSide() => Sample.Table("ExpenseApproval",
    [
        Key("ExpenseApprovalId"), Sample.Column("ExpenseId", SqlDbType.Int, ordinal: 2), Sample.Column("ApprovalStatusId", SqlDbType.Int, ordinal: 3), Sample.Column("Reason", SqlDbType.NVarChar, nullable: true, characters: 500, ordinal: 4),
        Sample.Column("RequestedBy", SqlDbType.NVarChar, characters: 50, ordinal: 5), Sample.Column("RequestedOn", SqlDbType.DateTime, ordinal: 6), Sample.Column("DecidedBy", SqlDbType.NVarChar, nullable: true, characters: 50, ordinal: 7),
        Sample.Column("DecidedOn", SqlDbType.DateTime, nullable: true, ordinal: 8), Sample.Column("DecisionNote", SqlDbType.NVarChar, nullable: true, characters: 500, ordinal: 9), Sample.Column("Revision", SqlDbType.Int, ordinal: 10)
    ], [Sample.ForeignKey("ExpenseId", "Expense", "ExpenseId", "Name")]);

    [TestMethod]
    public void A_table_with_an_approval_side_table_is_suggested_for_RowApprovals()
    {
        var database = Database(Employee(), Approvable(), ApprovalSide());
        var project = Project(("ApiAuth", "true"), ("UserTable", "Employee"), ("UserRoleColumn", "IsManager"));
        var found = DatabaseShapeDetector.Of(database, project).Single(d => d.Suggestion.Shape == "Row approvals");
        Assert.AreEqual("Expense", found.Table);
        Assert.AreEqual("RowApprovals", found.Suggestion.Setting);
        Assert.IsTrue(found.Suggestion.Fits, found.Suggestion.Note);
        Assert.IsFalse(DatabaseShapeDetector.Of(database, Project(("ApiAuth", "true"), ("UserTable", "Employee"), ("UserRoleColumn", "IsManager"), ("RowApprovals", "Expense"))).Any(d => d.Suggestion.Shape == "Row approvals"));
        Assert.IsFalse(DatabaseShapeDetector.Of(Database(Employee(), Approvable()), project).Any(d => d.Suggestion.Shape == "Row approvals"), "no side table");
    }

    [TestMethod]
    public void Row_approvals_without_a_user_table_only_look_like_it()
    {
        var found = DatabaseShapeDetector.Of(Database(Approvable(), ApprovalSide()), Project(("ApiAuth", "true"))).Single(d => d.Suggestion.Shape == "Row approvals");
        Assert.IsFalse(found.Suggestion.Fits);
        StringAssert.Contains(found.Suggestion.Note, "UserTable");
    }

    private static TableModel ExchangeRates() => Sample.Table("ExchangeRate",
    [
        Key("ExchangeRateId"), Sample.Column("FromCurrency", SqlDbType.VarChar, characters: 3, ordinal: 2), Sample.Column("ToCurrency", SqlDbType.VarChar, characters: 3, ordinal: 3),
        Sample.Column("Rate", SqlDbType.Decimal, precision: 18, scale: 6, ordinal: 4), Sample.Column("RateDate", SqlDbType.Date, ordinal: 5)
    ]);

    private static TableModel Invoice() => Sample.Table("Invoice",
    [
        Key("InvoiceId"), Sample.Column("CurrencyCode", SqlDbType.VarChar, characters: 3, ordinal: 2), Sample.Column("ExchangeRate", SqlDbType.Decimal, precision: 18, scale: 6, ordinal: 3),
        Sample.Column("Total", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 4), Sample.Column("BaseTotal", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 5), Sample.Column("InvoiceDate", SqlDbType.Date, ordinal: 6)
    ]);

    [TestMethod]
    public void A_document_that_converts_its_total_with_a_rate_is_suggested_for_RateDocuments()
    {
        var database = Database(ExchangeRates(), Invoice());
        var project = Project(("RateTables", "ExchangeRate"), ("BaseCurrency", "USD"));
        var found = DatabaseShapeDetector.Of(database, project).Single(d => d.Suggestion.Shape == "Exchange rate on a document");
        Assert.AreEqual("Invoice", found.Table);
        Assert.AreEqual("Invoice:CurrencyCode:ExchangeRate:Total:BaseTotal:InvoiceDate", found.Suggestion.Item);
        Assert.IsTrue(found.Suggestion.Fits, found.Suggestion.Note);
        var withoutRates = DatabaseShapeDetector.Of(database, Project(("BaseCurrency", "USD"))).Single(d => d.Suggestion.Shape == "Exchange rate on a document");
        Assert.IsFalse(withoutRates.Suggestion.Fits, "there is no rate table to look the rate up in");
        Assert.IsFalse(DatabaseShapeDetector.Of(database, Project(("RateTables", "ExchangeRate"), ("BaseCurrency", "USD"), ("RateDocuments", "Invoice:CurrencyCode:ExchangeRate:Total:BaseTotal"))).Any(d => d.Suggestion.Shape == "Exchange rate on a document"));
    }

    [TestMethod]
    public void A_project_wide_choice_is_offered_on_the_table_that_holds_it()
    {
        var database = Database(Employee());
        var project = Project(("ApiAuth", "true"));
        var user = DatabaseShapeDetector.Of(database, project).Single(d => d.Suggestion.Shape == "User table");
        Assert.AreEqual("Employee", user.Table);
        Assert.IsTrue(user.ProjectWide);
        Assert.IsTrue(ShapeMenu.Suggestions(database, "Employee", project).Any(s => s.Shape == "User table"), "the menu of the user table offers it");
        Assert.IsFalse(DatabaseShapeDetector.Of(database, project).Where(d => d.Suggestion.Shape != "User table").Any(d => d.ProjectWide));
    }

    [TestMethod]
    public void A_subscription_table_without_a_plan_table_setting_only_looks_like_it()
    {
        var plan = Sample.Table("Plan", [Key("PlanId"), Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2)]);
        var tenant = Sample.Table("Tenant", [Key("TenantId"), Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2)]);
        var subscription = Sample.Table("Subscription",
        [
            Key("SubscriptionId"), Sample.Column("TenantId", SqlDbType.Int, ordinal: 2), Sample.Column("PlanId", SqlDbType.Int, ordinal: 3), Sample.Column("Status", SqlDbType.NVarChar, characters: 20, ordinal: 4)
        ], [Sample.ForeignKey("TenantId", "Tenant", "TenantId", "Name"), Sample.ForeignKey("PlanId", "Plan", "PlanId", "Name")]);
        var found = DatabaseShapeDetector.Of(Database(tenant, plan, subscription), Project(("ApiAuth", "true"), ("TenantTable", "Tenant"))).Single(d => d.Suggestion.Shape == "Subscription table");
        Assert.AreEqual("Subscription", found.Table);
        Assert.IsTrue(found.ProjectWide);
        Assert.IsFalse(found.Suggestion.Fits);
        StringAssert.Contains(found.Suggestion.Note, "PlanTable=Plan");
    }
}
