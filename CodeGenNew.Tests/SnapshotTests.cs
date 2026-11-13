using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> SnapshotColumns: a child row keeps what its parents looked like when it was written. </summary>
[TestClass]
public class SnapshotTests
{
    private const string Setting = "OrderItem:ProductName<-Product.Name+UnitPrice<-Product.Price+TaxRate<-TaxCategory.Rate:LineTotal=Quantity*UnitPrice";

    private static ForeignKeyModel Fk(string column, string table, string key) => new()
    {
        ConstraintName = $"FK_OrderItem_{table}", ReferencingColumns = [column], ReferencedSchema = "dbo", ReferencedTable = table, ReferencedColumns = [key]
    };

    private static TableModel Product(int nameLength = 100) => Sample.Table("Product",
    [
        Sample.Column("ProductId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.VarChar, characters: nameLength, ordinal: 2),
        Sample.Column("Price", SqlDbType.Decimal, ordinal: 3),
    ], []);

    private static TableModel Tax() => Sample.Table("TaxCategory",
    [
        Sample.Column("TaxCategoryId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Rate", SqlDbType.Decimal, ordinal: 2),
    ], []);

    private static TableModel Item(bool secondProductKey = false) => Sample.Table("OrderItem",
    [
        Sample.Column("OrderItemId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ProductId", SqlDbType.Int, ordinal: 2),
        Sample.Column("TaxCategoryId", SqlDbType.Int, nullable: true, ordinal: 3),
        Sample.Column("ProductName", SqlDbType.VarChar, characters: 100, ordinal: 4),
        Sample.Column("UnitPrice", SqlDbType.Decimal, ordinal: 5),
        Sample.Column("TaxRate", SqlDbType.Decimal, nullable: true, ordinal: 6),
        Sample.Column("Quantity", SqlDbType.Int, ordinal: 7),
        Sample.Column("LineTotal", SqlDbType.Decimal, ordinal: 8),
        Sample.Column("AltProductId", SqlDbType.Int, ordinal: 9),
    ], secondProductKey ? [Fk("ProductId", "Product", "ProductId"), Fk("AltProductId", "Product", "ProductId"), Fk("TaxCategoryId", "TaxCategory", "TaxCategoryId")]
        : [Fk("ProductId", "Product", "ProductId"), Fk("TaxCategoryId", "TaxCategory", "TaxCategoryId")]);

    private static DatabaseModel Database(TableModel? item = null, TableModel? product = null) =>
        new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [product ?? Product(), Tax(), item ?? Item()] };

    private static ProjectSettings Project(string setting = Setting) =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["SnapshotColumns"] = setting, ["AccessMode"] = "Ef" });

    [TestMethod]
    public void The_setting_names_the_copies_and_the_total()
    {
        var database = Database();
        var shape = SnapshotPlan.For(database.Tables[2], Project(), database)!;
        CollectionAssert.AreEqual(new[] { "ProductName", "UnitPrice", "TaxRate" }, shape.Copies.Select(c => c.Target.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "Product", "Product", "TaxCategory" }, shape.Copies.Select(c => c.Parent.TableName).ToArray());
        Assert.AreEqual("LineTotal", shape.Total!.Target.Name);
        Assert.HasCount(0, RowShapePlan.Problems(database, Project()));
        Assert.IsTrue(RowShapePlan.Has(database.Tables[2], Project()));
        Assert.IsTrue(RowShapeWriter.HasSaveCheck(database.Tables[2], Project()));
    }

    [TestMethod]
    public void The_forms_leave_the_copies_and_the_total_to_the_server()
    {
        var item = Item();
        foreach (string name in new[] { "ProductName", "UnitPrice", "TaxRate", "LineTotal" })
            Assert.IsTrue(SnapshotPlan.IsOwnedColumn(item, Project(), item.Columns.First(c => c.Name == name)), name);
        foreach (string name in new[] { "ProductId", "Quantity" })
            Assert.IsFalse(SnapshotPlan.IsOwnedColumn(item, Project(), item.Columns.First(c => c.Name == name)), name);
    }

    [TestMethod]
    [DataRow("OrderItem", "needs the columns to copy")]
    [DataRow("OrderItem:Nothing<-Product.Name", "is not a column of the table")]
    [DataRow("OrderItem:ProductName", "is not Column<-Parent.Column")]
    [DataRow("OrderItem:ProductName<-Nowhere.Name", "is not a table of the database")]
    [DataRow("OrderItem:ProductName<-Product.Nothing", "is not a column of Product")]
    [DataRow("OrderItem:OrderItemId<-Product.ProductId", "the key or is worked out")]
    [DataRow("OrderItem:ProductId<-Product.ProductId", "is the foreign key to Product")]
    [DataRow("OrderItem:ProductName<-Product.Name+ProductName<-Product.Name", "is copied twice")]
    [DataRow("OrderItem:UnitPrice<-Product.Name", "cannot hold Product.Name")]
    [DataRow("OrderItem:ProductName<-Product.Name:LineTotal", "is not Column=FactorA*FactorB")]
    [DataRow("OrderItem:ProductName<-Product.Name:LineTotal=Quantity*Nothing", "is not a column of the table")]
    [DataRow("OrderItem:ProductName<-Product.Name:LineTotal=Quantity*ProductName", "must be number columns")]
    [DataRow("OrderItem:ProductName<-Product.Name:ProductName=Quantity*UnitPrice", "must be number columns")]
    public void A_setting_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_text_copy_cannot_be_shorter_than_its_source_and_a_null_source_needs_a_null_target()
    {
        var problems = RowShapePlan.Problems(Database(product: Product(nameLength: 200)), Project());
        Assert.IsTrue(problems.Any(p => p.Contains("cannot hold Product.Name")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void Two_foreign_keys_to_the_parent_are_ambiguous()
    {
        var problems = RowShapePlan.Problems(Database(Item(secondProductKey: true)), Project());
        Assert.IsTrue(problems.Any(p => p.Contains("exactly one single-column foreign key to Product")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_table_that_is_not_in_the_database_is_reported()
    {
        var problems = RowShapePlan.Problems(Database(), Project("Missing:Name<-Product.Name"));
        Assert.IsTrue(problems.Any(p => p.Contains("Missing is not a table of the database")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_api_fills_the_copies_on_create_keeps_them_on_update_and_works_the_total_out()
    {
        var database = Database();
        string code = RowShapeWriter.File(database, Project(), "ShopContext", []);
        Expect.Contains(code, "CheckSnapshotAsync(ShopContext context, OrderItem row, bool isNew)");
        Expect.Contains(code, "context.Set<Product>().Where(x => x.ProductId == row.ProductId)");
        Expect.Contains(code, "row.ProductName = parent0.Name;");
        Expect.Contains(code, "if (row.TaxCategoryId is { } link1)");
        Expect.Contains(code, "row.TaxRate = parent1.Rate;");
        Expect.Contains(code, "row.UnitPrice = stored.UnitPrice;");
        Expect.Contains(code, "row.LineTotal = (decimal)(row.Quantity * row.UnitPrice);");
        Expect.Contains(code, "RowShapeProblems.SnapshotParent(\"The Product the row refers to does not exist.\")");
        Expect.Contains(code, "if (await CheckSnapshotAsync(context, row, isNew) is { } snapshotProblem)");
    }

    [TestMethod]
    public void The_python_and_rust_writers_use_the_same_words()
    {
        var shape = SnapshotPlan.For(Item(), Project(), Database())!;
        Assert.IsNotNull(shape);
        var python = new PythonSnapshotWriter.Names([new("product_id", false, "Product", "product", "product_id", "Product", [new("product_name", "name")])], new("line_total", "quantity", "unit_price"));
        string py = PythonSnapshotWriter.Helper(python);
        Expect.Contains(py, RowShapeWords.SnapshotParentType);
        Expect.Contains(py, RowShapeWords.SnapshotParentMissing("Product"));
        Expect.Contains(py, "row.line_total = row.quantity * row.unit_price");
        var rust = new RustSnapshotWriter.Names("OrderItem", [new("product_id", false, "\"Product\"", "\"ProductId\"", "Product", [new("product_name", "\"Name\"")])], null);
        string rs = RustSnapshotWriter.Code(rust);
        Expect.Contains(rs, RowShapeWords.SnapshotParentType);
        Expect.Contains(rs, RowShapeWords.SnapshotParentMissing("Product"));
        Expect.Contains(rs, "row.product_name = parent.try_get(0)?;");
    }

    [TestMethod]
    public void The_setting_is_known_to_the_project()
    {
        Assert.HasCount(1, Project().SnapshotColumns);
        Assert.IsTrue(RowShapePlan.Any(Project()));
        Assert.IsTrue(ProjectSettings.Keys.Contains("SnapshotColumns"));
    }
}
