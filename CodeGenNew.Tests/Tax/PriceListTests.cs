using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Price;
using CodeGenNew.Core.Python;
using CodeGenNew.Core.Rust;

namespace CodeGenNew.Tests.Tax;

/// <summary> PriceListTables: price rows with a minimum quantity in lists that have a currency, a period and an optional customer group. </summary>
[TestClass]
public class PriceListTests
{
    private static TableModel Lists(bool nullableFrom = false, bool group = true) => Sample.Table("PriceList",
    [
        Sample.Column("PriceListId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 80, ordinal: 2),
        Sample.Column("CurrencyCode", SqlDbType.Char, characters: 3, ordinal: 3),
        Sample.Column("ValidFrom", SqlDbType.Date, nullable: nullableFrom, ordinal: 4),
        Sample.Column("ValidTo", SqlDbType.Date, nullable: true, ordinal: 5),
        .. group ? new[] { Sample.Column("CustomerGroup", SqlDbType.NVarChar, nullable: true, characters: 40, ordinal: 6) } : Array.Empty<ColumnModel>()
    ], []);

    private static TableModel Prices(SqlDbType price = SqlDbType.Decimal, bool unique = false) => Sample.Table("PriceListItem",
    [
        Sample.Column("PriceListItemId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("PriceListId", SqlDbType.Int, ordinal: 2),
        Sample.Column("ItemId", SqlDbType.Int, ordinal: 3),
        Sample.Column("MinQuantity", SqlDbType.Decimal, precision: 18, scale: 3, ordinal: 4),
        Sample.Column("Price", price, precision: 18, scale: 4, ordinal: 5),
    ], [], indexes: unique ? [new IndexModel("UX_Break", true, ["PriceListId", "ItemId", "MinQuantity"])] : []);

    private static DatabaseModel Database(TableModel? lists = null, TableModel? prices = null) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [lists ?? Lists(), prices ?? Prices()] };

    private static ProjectSettings Project(string setting = "PriceListItem:PriceListId:ItemId:MinQuantity:Price:PriceList:CurrencyCode:ValidFrom:ValidTo:CustomerGroup") =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["PriceListTables"] = setting, ["AccessMode"] = "Ef" });

    [TestMethod]
    public void The_table_alone_finds_the_default_columns()
    {
        var database = Database();
        var project = Project("PriceListItem");
        var shape = PriceListPlan.For(database.Tables[1], project, database)!;
        Assert.AreEqual("PriceListId", shape.List.Name);
        Assert.AreEqual("ItemId", shape.Item.Name);
        Assert.AreEqual("MinQuantity", shape.MinQuantity.Name);
        Assert.AreEqual("Price", shape.Price.Name);
        Assert.AreEqual("CurrencyCode", shape.Currency.Name);
        Assert.AreEqual("ValidTo", shape.ValidTo.Name);
        Assert.IsNull(shape.Group, "the group is used only when the entry names it");
        Assert.HasCount(0, RowShapePlan.Problems(database, project));
        Assert.IsTrue(RowShapePlan.Has(database.Tables[1], project) && RowShapeWriter.HasSaveCheck(database.Tables[1], project));
    }

    [TestMethod]
    public void The_entry_names_the_group_of_the_lists()
    {
        var database = Database();
        Assert.AreEqual("CustomerGroup", PriceListPlan.For(database.Tables[1], Project(), database)!.Group!.Name);
    }

    [TestMethod]
    [DataRow("Missing", "is not a table of the database")]
    [DataRow("PriceListItem:Price", "whole-number column that cannot be NULL, of the same type as the key")]
    [DataRow("PriceListItem:PriceListId:Price", "must be a whole-number column")]
    [DataRow("PriceListItem:PriceListId:ItemId:Nothing", "must be a number column")]
    [DataRow("PriceListItem:PriceListId:ItemId:MinQuantity:MinQuantity:PriceList", "must be four different columns")]
    [DataRow("PriceListItem:PriceListId:ItemId:MinQuantity:Price:Nothing", "is not a table of the database")]
    [DataRow("PriceListItem:PriceListId:ItemId:MinQuantity:Price:PriceList:Nothing:ValidFrom:ValidTo", "must be a text column that cannot be NULL")]
    [DataRow("PriceListItem:PriceListId:ItemId:MinQuantity:Price:PriceList:CurrencyCode:ValidFrom:ValidTo:Missing", "must be a text column (the customer group")]
    public void A_setting_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_first_day_that_allows_null_and_a_price_that_is_not_fractional_are_reported()
    {
        Assert.IsTrue(RowShapePlan.Problems(Database(Lists(nullableFrom: true)), Project()).Any(p => p.Contains("first day of the list")));
        Assert.IsTrue(RowShapePlan.Problems(Database(prices: Prices(SqlDbType.Int)), Project()).Any(p => p.Contains("decimal, double or float column")));
    }

    [TestMethod]
    public void The_list_table_is_dated_by_currency_and_group()
    {
        CollectionAssert.Contains(Project().EffectiveDatedTables, "PriceList:ValidFrom:ValidTo:CurrencyCode+CustomerGroup");
        CollectionAssert.Contains(Project("PriceListItem").EffectiveDatedTables, "PriceList:ValidFrom:ValidTo:CurrencyCode");
        var own = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["PriceListTables"] = "PriceListItem", ["EffectiveDatedTables"] = "PriceList:ValidFrom:ValidTo:CurrencyCode+CreatedBy", ["AccessMode"] = "Ef" });
        Assert.AreEqual(1, own.EffectiveDatedTables.Count(i => i.StartsWith("PriceList:")), "a table the project lists itself is left alone");
    }

    [TestMethod]
    public void The_api_checks_the_quantity_the_price_and_the_break_and_serves_the_price_in_force()
    {
        string text = RowShapeWriter.File(Database(), Project(), "ShopContext", []);
        Expect.Contains(text, "CheckPriceAsync(ShopContext context, PriceListItem row)");
        Expect.Contains(text, "if ((decimal)row.MinQuantity <= 0)");
        Expect.Contains(text, "if ((decimal)row.Price < 0)");
        Expect.Contains(text, "x.PriceListId == row.PriceListId && x.ItemId == row.ItemId");
        Expect.Contains(text, "RowShapeProblems.PriceTaken(\"" + PriceWords.Taken + "\")");
        Expect.Contains(text, "l.CurrencyCode.ToUpper() == currency && l.ValidFrom <= day && (l.ValidTo == null || l.ValidTo >= day)");
        Expect.Contains(text, "listed.Where(l => l.CustomerGroup != null && l.CustomerGroup!.ToUpper() == held)");
        Expect.Contains(text, ".OrderByDescending(x => (decimal)x.MinQuantity).ThenByDescending(x => x.PriceListItemId)");
        Expect.Contains(text, "app.MapGet(\"/api/pricelistitems/price\"");
        Expect.Contains(text, PriceWords.TakenType);
        Expect.Contains(text, PriceWords.MissingType);
    }

    [TestMethod]
    public void The_api_of_a_list_with_no_group_reads_no_group()
    {
        var database = Database(Lists(group: false));
        string text = RowShapeWriter.File(database, Project("PriceListItem"), "ShopContext", []);
        Expect.DoesNotContain(text, "customerGroup");
        Expect.Contains(text, "FindPriceAsync(context, item.Value, wanted, (day ?? DateTime.Today).Date, asked, null)");
    }

    [TestMethod]
    public void A_table_without_a_unique_index_on_the_break_is_warned_about()
    {
        Assert.HasCount(1, PriceListPlan.MissingIndexes(Database(), Project()));
        Assert.HasCount(0, PriceListPlan.MissingIndexes(Database(prices: Prices(unique: true)), Project()));
        Assert.HasCount(0, PriceListPlan.MissingIndexes(Database(), Project("Other")));
    }

    [TestMethod]
    public void The_unique_index_on_the_break_is_remembered_so_the_api_answers_it_as_the_rule_does()
    {
        var database = Database(prices: Prices(unique: true));
        var project = Project();
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        var indexes = PriceListPlan.PairIndexes(project);
        Assert.HasCount(1, indexes);
        Assert.AreEqual("UX_Break", indexes[0].Name);
    }

    [TestMethod]
    public void The_python_api_carries_the_same_rule_and_route()
    {
        var names = new PythonPriceWriter.Names("PriceListItem", "price_list_item_id", "price_list_id", "item_id", "int", "min_quantity", "price", "PriceListItemSchema", "PriceList", "price_list_id", "currency_code", "valid_from", "valid_to", "customer_group");
        string python = PythonPriceWriter.Helper(names) + PythonPriceWriter.Route(names, "/api/pricelistitems", "", "");
        Expect.Contains(python, "def check_price(session: Session, row) -> None:");
        Expect.Contains(python, "if row.min_quantity is None or row.min_quantity <= 0:");
        Expect.Contains(python, "if row.price is None or row.price < 0:");
        Expect.Contains(python, PriceWords.TakenType);
        Expect.Contains(python, "PriceListItem.min_quantity <= quantity");
        Expect.Contains(python, "(func.upper(PriceList.customer_group) == held) if held is not None else PriceList.customer_group.is_(None)");
        Expect.Contains(python, "@router.get(\"/api/pricelistitems/price\", response_model=PriceListItemSchema)");
        Expect.Contains(python, "group: str | None = Query(None)");
        string plain = PythonPriceWriter.Route(names with { Group = null }, "/api/pricelistitems", "", "");
        Expect.DoesNotContain(plain, "group");
        Expect.Contains(plain, "asked, None)");
        Assert.AreEqual("pair = (row.price_list_id, row.item_id, row.min_quantity)", PythonPriceWriter.PairLine(names).Trim());
    }

    [TestMethod]
    public void The_rust_api_carries_the_same_rule_and_route()
    {
        var names = new RustPriceWriter.Names("PriceListItem", "\"PriceListItem\"", "*", "\"PriceListItemId\"", "i32", "price_list_id", "\"PriceListId\"", "item_id", "\"ItemId\"", "i32", "min_quantity", "\"MinQuantity\"", "0.0", "price", "0.0",
            "\"PriceList\"", "\"PriceListId\"", "\"CurrencyCode\"", "\"ValidFrom\"", "\"ValidTo\"", "\"CustomerGroup\"");
        string rust = RustPriceWriter.Code(names) + RustPriceWriter.RouteLine("/api/pricelistitems", "");
        Expect.Contains(rust, "async fn check_price(pool: &Pool, row_id: Option<i32>, row: &mut PriceListItem)");
        Expect.Contains(rust, "if row.min_quantity <= 0.0 {");
        Expect.Contains(rust, "if row.price < 0.0 {");
        Expect.Contains(rust, PriceWords.TakenType);
        Expect.Contains(rust, "UPPER(\"CustomerGroup\") = ");
        Expect.Contains(rust, "\"CustomerGroup\" IS NULL");
        Expect.Contains(rust, ".route(\"/api/pricelistitems/price\", get(price_on))");
        Expect.Contains(rust, "group: Option<String>,");
        string plain = RustPriceWriter.Code(names with { GroupColumn = null });
        Expect.DoesNotContain(plain, "CustomerGroup");
        Expect.Contains(plain, "let group: Option<String> = None;");
        Expect.Contains(RustPriceWriter.InsertLines, "price_refused(&pool, None, &mut row, error)");
    }

    private static ProjectSettings Owned(string owned, string setting = "PriceListItem") =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["PriceListTables"] = setting, ["AccessMode"] = "Ef", ["ApiAuth"] = "true", ["OwnedTables"] = owned });

    private static TableModel OwnedLists() => Sample.Table("PriceList",
    [
        Sample.Column("PriceListId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("EmployeeId", SqlDbType.Int, ordinal: 2),
        Sample.Column("CurrencyCode", SqlDbType.Char, characters: 3, ordinal: 3),
        Sample.Column("ValidFrom", SqlDbType.Date, ordinal: 4),
        Sample.Column("ValidTo", SqlDbType.Date, nullable: true, ordinal: 5),
    ], []);

    [TestMethod]
    public void Price_rows_that_hang_off_an_owned_list_are_served_and_read_the_lists_owner()
    {
        var lists = OwnedLists();
        var prices = Sample.Table("PriceListItem", Prices().Columns.ToList(), [Sample.ForeignKey("PriceListId", "PriceList", "PriceListId")], indexes: [new IndexModel("UX_Break", true, ["PriceListId", "ItemId", "MinQuantity"])]);
        var database = Database(lists, prices);
        var project = Owned("PriceList");
        Assert.HasCount(0, PriceListPlan.Problems(database, project));
        var shape = PriceListPlan.For(prices, project, database)!;
        Assert.IsTrue(shape.IsLimited);
        Assert.IsNull(shape.Owner);
        Assert.AreEqual("EmployeeId", shape.ListOwner!.Name);
        string text = RowShapeWriter.File(database, project, "ShopContext", []);
        Expect.Contains(text, "HttpContext http, [FromServices] ShopContext context");
        Expect.Contains(text, "listed = listed.Where(l => visible.Contains(l.EmployeeId));");
        Expect.Contains(text, "GetRequiredService<IOwnerScope>().VisibleOwnersAsync");
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        CollectionAssert.Contains(project.EffectiveDatedTables, "PriceList:ValidFrom:ValidTo:CurrencyCode+EmployeeId");
    }

    [TestMethod]
    public void Price_rows_that_are_not_limited_to_the_owner_of_an_owned_list_are_refused()
    {
        var database = Database(OwnedLists(), Prices());
        var problems = PriceListPlan.Problems(database, Owned("PriceList"));
        Assert.IsTrue(problems.Any(p => p.Contains("must be limited the same way")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_price_shapes_are_found_through_the_table_shapes()
    {
        var database = Database();
        var project = Project();
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        Assert.IsNotNull(TableShapes.For(database.Tables[1], project, database).PriceList);
        Assert.IsNull(TableShapes.For(database.Tables[0], project, database).PriceList);
        Assert.AreSame(database.Tables[1], TableShapes.For(database.Tables[0], project, database).PriceListHead!.Prices.Table);
        Assert.IsNull(TableShapes.For(database.Tables[1], project, database).PriceListHead);
        Assert.IsNull(TableShapes.For(database.Tables[1], Project("Other"), database).PriceList);
    }

    [TestMethod]
    public void The_detector_suggests_price_lists_for_a_price_table_with_a_list_that_has_a_currency_and_a_period()
    {
        var lists = Lists();
        var prices = Sample.Table("PriceListItem", Prices().Columns.ToList(), [Sample.ForeignKey("PriceListId", "PriceList", "PriceListId")]);
        var database = Database(lists, prices);
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" });
        var found = DatabaseShapeDetector.Of(database, project).Where(d => d.Suggestion.Setting == "PriceListTables").ToList();
        Assert.HasCount(1, found);
        Assert.AreEqual("PriceListItem", found[0].Table);
        Assert.AreEqual("PriceListItem:PriceListId:ItemId:MinQuantity:Price:PriceList:CurrencyCode:ValidFrom:ValidTo", found[0].Suggestion.Item.Replace(":CustomerGroup", ""));
        Assert.IsTrue(found[0].Suggestion.Fits, found[0].Suggestion.Note);
        Assert.IsEmpty(DatabaseShapeDetector.Of(database, Project()).Where(d => d.Suggestion.Setting == "PriceListTables"), "a listed table is not suggested again");
    }

    [TestMethod]
    public void The_overlap_rule_compares_a_text_group_without_regard_to_case_in_every_stack()
    {
        var database = Database();
        string text = RowShapeWriter.File(database, Project(), "ShopContext", []);
        Expect.Contains(text, "var group0 = row.CurrencyCode?.ToUpper();");
        Expect.Contains(text, "others = others.Where(x => x.CurrencyCode!.ToUpper() == group0);");
        var names = new PythonEffectiveWriter.Names("PriceList", "price_list_id", "valid_from", "valid_to", "date", [new("currency_code", "currencyCode", "str"), new("rank", "rank", "int")], "", "PriceListSchema");
        string python = PythonEffectiveWriter.CurrentRoute(names, "/api/pricelists", "", "");
        Expect.Contains(python, "func.upper(PriceList.currency_code) == currency_code.upper()");
        Expect.Contains(python, "PriceList.rank == rank");
        Expect.Contains(PythonEffectiveWriter.GroupArgument(names, a => a, ("tenant_id", "user.tenant_id")), "\"tenant_id\": user.tenant_id");
        Assert.IsTrue(EffectiveDatePlan.FoldsCase(OwnedLists().Columns.First(c => c.Name == "CurrencyCode")));
        Assert.IsFalse(EffectiveDatePlan.FoldsCase(OwnedLists().Columns.First(c => c.Name == "EmployeeId")));
    }

    [TestMethod]
    public void An_owner_with_a_column_of_his_own_on_the_list_and_the_rows_keeps_lists_and_rows_apart()
    {
        var prices = Sample.Table("PriceListItem", [.. Prices().Columns, Sample.Column("EmployeeId", SqlDbType.Int, ordinal: 6)], [Sample.ForeignKey("PriceListId", "PriceList", "PriceListId")], indexes: [new IndexModel("UX_Break", true, ["PriceListId", "ItemId", "MinQuantity"])]);
        var database = Database(OwnedLists(), prices);
        var project = Owned("PriceList,PriceListItem");
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        var shape = PriceListPlan.For(prices, project, database)!;
        Assert.AreEqual("EmployeeId", shape.Owner!.Name);
        Assert.AreEqual("EmployeeId", shape.ListOwner!.Name);
        CollectionAssert.Contains(project.EffectiveDatedTables, "PriceList:ValidFrom:ValidTo:CurrencyCode+EmployeeId");
        string text = RowShapeWriter.File(database, project, "ShopContext", []);
        Expect.Contains(text, "l.PriceListId == row.PriceListId && l.EmployeeId == row.EmployeeId");
    }

    [TestMethod]
    public void The_rust_period_check_of_a_limited_table_is_only_a_check_and_reads_the_scope_as_a_group()
    {
        var owner = new RustEffectiveWriter.Group("employee_id", "employeeId", "\"EmployeeId\"", "i32", false);
        var groups = RustEffectiveWriter.WithScope([new("currency_code", "currencyCode", "\"CurrencyCode\"", "String", false)], owner, null);
        Assert.HasCount(2, groups);
        Assert.HasCount(2, RustEffectiveWriter.WithScope(groups, owner));
        var names = new RustEffectiveWriter.Names("PriceList", "price_list_id", "\"PriceListId\"", "i32", "valid_from", "\"ValidFrom\"", "valid_to", "\"ValidTo\"", true, "NaiveDate", groups, "\"PriceList\"", "*", true);
        string code = RustEffectiveWriter.Code(names, "");
        Expect.Contains(code, "async fn check_effective(");
        Expect.Contains(code, "UPPER(\"CurrencyCode\") = ");
        Expect.Contains(code, "AND \"EmployeeId\" = ");
        Expect.DoesNotContain(code, "async fn current(");
        Expect.Contains(RustEffectiveWriter.Code(names with { CheckOnly = false }, ""), "async fn current(");
    }
}
