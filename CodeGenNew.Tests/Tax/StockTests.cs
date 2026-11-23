using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Python;
using CodeGenNew.Core.Rust;
using CodeGenNew.Core.Sql;
using CodeGenNew.Core.Stock;

namespace CodeGenNew.Tests.Tax;

/// <summary> StockTables: a stock level table, its ledger (the table that carries the stock routes) and its holds. </summary>
[TestClass]
public class StockTests
{
    private static TableModel Items() => Sample.Table("StockItem",
    [
        Sample.Column("StockItemId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.VarChar, characters: 60, ordinal: 2),
    ], []);

    private static TableModel Level(bool unique = false, SqlDbType onHand = SqlDbType.Int) => Sample.Table("StockLevel",
    [
        Sample.Column("StockLevelId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ItemId", SqlDbType.Int, ordinal: 2),
        Sample.Column("OnHand", onHand, ordinal: 3),
        Sample.Column("Reserved", onHand, ordinal: 4),
        Sample.Column("ReorderLevel", SqlDbType.Int, ordinal: 5),
    ], [Sample.ForeignKey("ItemId", "StockItem", "StockItemId")],
    indexes: unique ? [new IndexModel("UX_Item", true, ["ItemId"])] : []);

    private static TableModel Ledger() => Sample.Table("StockLedger",
    [
        Sample.Column("StockLedgerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ItemId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Kind", SqlDbType.VarChar, characters: 12, ordinal: 3),
        Sample.Column("Quantity", SqlDbType.Int, ordinal: 4),
        Sample.Column("OccurredOn", SqlDbType.DateTime, ordinal: 5),
        Sample.Column("Reference", SqlDbType.VarChar, nullable: true, characters: 60, ordinal: 6),
    ], [Sample.ForeignKey("ItemId", "StockItem", "StockItemId")]);

    private static TableModel Holds(bool expires = true) => Sample.Table("StockReservation",
    [
        Sample.Column("StockReservationId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ItemId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Quantity", SqlDbType.Int, ordinal: 3),
        Sample.Column("Status", SqlDbType.VarChar, characters: 10, ordinal: 4),
        Sample.Column("CreatedOn", SqlDbType.DateTime, ordinal: 5),
        .. (expires ? [Sample.Column("ExpiresOn", SqlDbType.DateTime, ordinal: 6)] : Array.Empty<ColumnModel>()),
    ], [Sample.ForeignKey("ItemId", "StockItem", "StockItemId")]);

    private static DatabaseModel Database(TableModel? level = null, TableModel? holds = null) =>
        new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Items(), level ?? Level(), Ledger(), holds ?? Holds()] };

    private static ProjectSettings Project(string value = "StockLevel") =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["StockTables"] = value, ["AccessMode"] = "Ef" });

    [TestMethod]
    public void The_defaults_find_the_ledger_and_the_holds_and_both_are_log_tables()
    {
        var database = Database();
        var project = Project();
        var shape = StockPlan.ForLevel(database.Tables.Single(t => t.TableName == "StockLevel"), project, database)!;
        Assert.AreEqual("StockLedger", shape.Ledger.Table.TableName);
        Assert.AreEqual("StockReservation", shape.Reservation.Table.TableName);
        Assert.AreEqual("ReorderLevel", shape.Reorder!.Name);
        Assert.AreEqual("StockItem", shape.ItemTable.TableName);
        Assert.HasCount(0, StockPlan.Problems(database, project));
        Assert.IsTrue(LogTablePlan.IsListed("StockLedger", project));
        Assert.IsTrue(LogTablePlan.IsListed("StockReservation", project));
        Assert.IsNotNull(StockPlan.ForLedger(shape.Ledger.Table, project, database), "the ledger carries the routes");
        Assert.IsTrue(RowShapeWriter.HasSaveCheck(shape.Level, project));
        Assert.IsTrue(RowShapeWriter.HasDeleteCheck(shape.Level, project));
    }

    [TestMethod]
    public void The_quantities_are_managed_and_a_test_sends_zero_for_them()
    {
        var project = Project();
        Assert.IsTrue(StockPlan.IsManagedQuantity("StockLevel", "OnHand", project));
        Assert.IsTrue(StockPlan.IsManagedQuantity("StockLevel", "Reserved", project));
        Assert.IsFalse(StockPlan.IsManagedQuantity("StockLevel", "ReorderLevel", project));
        var level = Level();
        Assert.AreEqual("0", level.Columns.Single(c => c.Name == "OnHand").ValidExample(project, "StockLevel"));
        Assert.AreEqual("1", level.Columns.Single(c => c.Name == "ReorderLevel").ValidExample(project, "StockLevel"));
    }

    [TestMethod]
    public void A_problem_is_named_for_a_missing_ledger_a_holds_table_without_an_end_and_unlike_quantities()
    {
        var noLedger = new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Items(), Level(), Holds()] };
        Assert.Contains("needs a ledger table", StockPlan.Problems(noLedger, Project()).Single());
        Assert.Contains("two date and time columns", StockPlan.Problems(Database(holds: Holds(expires: false)), Project()).Single());
        Assert.Contains("must be int or bigint columns of one type", StockPlan.Problems(Database(Level(onHand: SqlDbType.Decimal)), Project()).Single());
        Assert.Contains("is not a table of the database", StockPlan.Problems(Database(), Project("Missing")).Single());
    }

    [TestMethod]
    public void A_level_without_a_unique_index_on_the_item_is_a_warning()
    {
        Assert.HasCount(1, StockPlan.MissingIndexes(Database(), Project()));
        Assert.HasCount(0, StockPlan.MissingIndexes(Database(Level(unique: true)), Project()));
    }

    [TestMethod]
    public void The_csharp_routes_guard_the_update_and_write_ledger_rows()
    {
        var database = Database(Level(unique: true));
        var project = Project();
        var shape = StockPlan.ForLedger(database.Tables.Single(t => t.TableName == "StockLedger"), project, database)!;
        string code = StockWriter.Code(shape, "ShopContext", 30);
        Assert.Contains("x.OnHand - x.Reserved >= quantity", code);
        Assert.Contains("insufficient-stock", code);
        Assert.Contains("\"release\"", code);
        Assert.Contains("\"sale\"", code);
        Assert.Contains("\"adjustment\"", code);
        string maps = StockWriter.MapLines(shape);
        foreach (string route in new[] { "available", "low", "check", "receive", "reserve", "commit/{id}", "release/{id}", "expire", "adjust" })
            Assert.Contains($"§R/{route}", maps);
        Assert.Contains("stock-managed", StockWriter.LevelCode(shape, "ShopContext"));
    }

    [TestMethod]
    public void The_sweep_is_written_in_all_three_stacks_only_when_it_is_asked_for()
    {
        var database = Database(Level(unique: true));
        var plain = Project();
        var timed = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["StockTables"] = "StockLevel", ["AccessMode"] = "Ef", ["StockSweepMinutes"] = "5" });
        Assert.IsFalse(StockPlan.HasSweep(database, plain));
        Assert.IsTrue(StockPlan.HasSweep(database, timed));
        Assert.IsFalse(plain.WantsSchedule);
        Assert.IsTrue(timed.WantsSchedule);
        var shape = StockPlan.ForLedger(database.Tables.Single(t => t.TableName == "StockLedger"), timed, database)!;
        Assert.DoesNotContain("StartSweep", StockWriter.Code(shape, "ShopContext", 30));
        string code = StockWriter.Code(shape, "ShopContext", 30, 5);
        Assert.Contains("TimeSpan.FromMinutes(5)", code);
        Assert.Contains("ExpireAsync(scope.ServiceProvider.GetRequiredService<ShopContext>(), null)", code);
        Assert.Contains("StartSweep(app);", StockWriter.MapLines(shape, true));
        Assert.DoesNotContain("StartSweep", StockWriter.MapLines(shape));

        var names = PythonStockWriter.LedgerNames(shape, PythonTable.Of(shape.Ledger.Table, timed), timed)!;
        Assert.Contains("STOCK_SWEEP_MINUTES = 5", PythonStockWriter.LedgerHelper(names));
        Assert.DoesNotContain("STOCK_SWEEP_MINUTES", PythonStockWriter.LedgerHelper(PythonStockWriter.LedgerNames(shape, PythonTable.Of(shape.Ledger.Table, plain), plain)!));

        var dialect = DialectInfo.For(SqlDialect.Sqlite);
        Assert.Contains("STOCK_SWEEP_MINUTES: u64 = 5", RustStockWriter.Code(shape, RustTable.Of(shape.Ledger.Table, timed), timed, dialect));
        Assert.DoesNotContain("STOCK_SWEEP_MINUTES", RustStockWriter.Code(shape, RustTable.Of(shape.Ledger.Table, plain), plain, dialect));
    }

    [TestMethod]
    public void The_form_of_the_level_shows_what_is_on_hand_and_reserved_as_read_only_text()
    {
        var database = Database();
        var level = database.Tables.Single(t => t.TableName == "StockLevel");
        var form = FormModel.For(level, Project());
        Assert.IsTrue(form.Field(level.Columns.Single(c => c.Name == "OnHand")).IsSystemStampField);
        Assert.IsTrue(form.Field(level.Columns.Single(c => c.Name == "Reserved")).IsSystemStampField);
        Assert.IsFalse(form.Field(level.Columns.Single(c => c.Name == "ReorderLevel")).IsSystemStampField, "the reorder level is edited by hand");
    }

    [TestMethod]
    public void A_sweep_without_stock_tables_is_a_problem()
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["StockSweepMinutes"] = "5", ["AccessMode"] = "Ef" });
        Assert.Contains("StockSweepMinutes needs StockTables", StockPlan.Problems(Database(), project).Single());
    }

    [TestMethod]
    public void The_python_and_rust_routes_use_the_same_words()
    {
        var database = Database(Level(unique: true));
        var project = Project();
        var shape = StockPlan.ForLedger(database.Tables.Single(t => t.TableName == "StockLedger"), project, database)!;
        var names = PythonStockWriter.LedgerNames(shape, PythonTable.Of(shape.Ledger.Table, project), project)!;
        string python = PythonStockWriter.LedgerHelper(names) + PythonStockWriter.LedgerRoutes(names, "/api/stockledgers", "", "", "");
        Assert.Contains("insufficient-stock", python);
        Assert.Contains("on_hand - StockLevel.reserved >=", python);
        string rust = RustStockWriter.Code(shape, RustTable.Of(shape.Ledger.Table, project), project, DialectInfo.For(SqlDialect.Sqlite));
        Assert.Contains("insufficient-stock", rust);
        Assert.Contains("rows_affected()", rust);
        Assert.Contains("stock_reserve", RustStockWriter.RouteLines("/api/stockledgers", ""));
    }
}
