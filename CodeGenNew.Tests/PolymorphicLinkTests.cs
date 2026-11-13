using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> PolymorphicLinks: a table whose rows attach to a row of any of several tables, through a record type and an id. </summary>
[TestClass]
public class PolymorphicLinkTests
{
    private static TableModel Keyed(string name) => Sample.Table(name,
    [
        Sample.Column(name + "Id", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 2),
    ], []);

    private static TableModel Attachment(int typeLength = 30, bool idForeignKey = false) => Sample.Table("Attachment",
    [
        Sample.Column("AttachmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("OwnerType", SqlDbType.NVarChar, characters: typeLength, ordinal: 2),
        Sample.Column("OwnerId", SqlDbType.Int, ordinal: 3),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 100, ordinal: 4),
    ], idForeignKey ? [new ForeignKeyModel { ConstraintName = "FK_Attachment_Product", ReferencingColumns = ["OwnerId"], ReferencedSchema = "dbo", ReferencedTable = "Product", ReferencedColumns = ["ProductId"], ReferencedDisplayColumns = ["Name"] }] : []);

    private static DatabaseModel Database(TableModel? attachment = null) =>
        new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Keyed("Product"), Keyed("Customer"), Keyed("Opportunity"), attachment ?? Attachment()] };

    private static ProjectSettings Project(string links = "Attachment:OwnerType:OwnerId:Product|Customer|Opportunity", params (string Key, string Value)[] extra)
    {
        var values = new Dictionary<string, string> { ["ProjectName"] = "Shop", ["PolymorphicLinks"] = links, ["AccessMode"] = "Ef" };
        foreach (var (key, value) in extra)
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    private static string Written(DatabaseModel database, ProjectSettings project) => RowShapeWriter.File(database, project, "ShopContext", []);

    [TestMethod]
    public void The_setting_names_the_link_table_its_columns_and_the_tables_the_type_may_name()
    {
        var project = Project();
        var link = PolymorphicPlan.LinkOf(Attachment(), project, Database())!;
        Assert.AreEqual("OwnerType", link.TypeColumn.Name);
        Assert.AreEqual("OwnerId", link.IdColumn.Name);
        CollectionAssert.AreEqual(new[] { "Product", "Customer", "Opportunity" }, link.Targets.Select(t => t.Table.TableName).ToArray());
        Assert.IsFalse(link.RemoveWithTarget);
        Assert.HasCount(0, RowShapePlan.Problems(Database(), project));
        Assert.IsTrue(PolymorphicPlan.IsTarget("Customer", project));
        Assert.IsFalse(PolymorphicPlan.IsTarget("Attachment", project));
        Assert.IsTrue(PolymorphicPlan.RefusesDeleteOf("Product", project));
        Assert.IsFalse(PolymorphicPlan.RemovesLinksOf("Product", project));
        Assert.IsTrue(PolymorphicPlan.RemovesLinksOf("Product", Project("Attachment:OwnerType:OwnerId:Product|Customer:remove")));
    }

    [TestMethod]
    [DataRow("Attachment:OwnerType:OwnerId:Product", "at least two tables")]
    [DataRow("Attachment:OwnerType:OwnerId:Product|Nothing", "Nothing is not a table")]
    [DataRow("Attachment:OwnerType:OwnerId:Product|Product", "twice")]
    [DataRow("Attachment:OwnerType:OwnerId:Product|Attachment", "link to itself")]
    [DataRow("Attachment:OwnerId:OwnerId:Product|Customer", "text column for the record type")]
    [DataRow("Attachment:OwnerType:Title:Product|Customer", "whole-number column for the record id")]
    [DataRow("Attachment:OwnerType:OwnerId", "at least two tables")]
    [DataRow("Gone:OwnerType:OwnerId:Product|Customer", "not a table of the database")]
    public void A_setting_that_cannot_be_served_says_why(string links, string reason)
    {
        var problems = RowShapePlan.Problems(Database(), Project(links));
        Assert.IsTrue(problems.Any(p => p.Contains(reason)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_id_column_cannot_have_a_foreign_key_and_the_type_column_needs_room_for_the_longest_name()
    {
        Assert.IsTrue(RowShapePlan.Problems(Database(Attachment(idForeignKey: true)), Project()).Any(p => p.Contains("has a foreign key")));
        Assert.IsTrue(RowShapePlan.Problems(Database(Attachment(typeLength: 5)), Project()).Any(p => p.Contains("holds 5 characters")));
    }

    [TestMethod]
    public void Removing_the_links_of_a_row_cannot_be_combined_with_files_or_a_soft_delete()
    {
        var withAsset = Project("Attachment:OwnerType:OwnerId:Product|Customer:remove", ("AssetTables", "Attachment:Title:Title:Title:OwnerId"));
        Assert.IsTrue(PolymorphicPlan.Problems(Database(), withAsset).Count > 0 || RowShapePlan.Problems(Database(), withAsset).Count > 0);
        var soft = Project("Attachment:OwnerType:OwnerId:Product|Customer:remove", ("SoftDeleteTables", "Product"));
        Assert.IsTrue(PolymorphicPlan.Problems(Database(), soft).Any(p => p.Contains("SoftDeleteTables")));
    }

    [TestMethod]
    public void The_api_checks_the_type_and_the_row_on_a_save_and_refuses_the_delete_of_a_linked_row()
    {
        string code = Written(Database(), Project());
        Expect.Contains(code, "public static class LinkProblems");
        Expect.Contains(code, "public static async Task<IResult?> CheckLinkAsync(ShopContext context, Attachment row)");
        Expect.Contains(code, "switch ((row.OwnerType ?? \"\").Trim().ToLowerInvariant())");
        Expect.Contains(code, "case \"customer\":\n                row.OwnerType = \"Customer\";\n                found = await context.Set<Customer>().AnyAsync(x => x.CustomerId == row.OwnerId);");
        Expect.Contains(code, "return LinkProblems.UnknownType(\"The record type must be one of: Product, Customer, Opportunity.\");");
        Expect.Contains(code, "if (await CheckLinkAsync(context, row) is { } linkProblem)");
        Expect.Contains(code, "public static class ProductShape");
        Expect.Contains(code, "if (await context.Set<Attachment>().AnyAsync(x => x.OwnerType == \"Product\" && x.OwnerId == id))\n            return LinkProblems.InUse(\"Attachment\");");
        Expect.Contains(code, "if (await CheckLinksAsync(context, id) is { } linkedProblem)");
        Expect.DoesNotContain(code, "ExecuteDeleteAsync");
    }

    [TestMethod]
    public void With_remove_the_links_go_when_the_row_goes()
    {
        string code = Written(Database(), Project("Attachment:OwnerType:OwnerId:Product|Customer:remove"));
        Expect.Contains(code, "public static async Task AfterDeleteAsync(ShopContext context, int id)");
        Expect.Contains(code, "await context.Set<Attachment>().Where(x => x.OwnerType == \"Product\" && x.OwnerId == id).ExecuteDeleteAsync();");
        Expect.DoesNotContain(code, "CheckLinksAsync");
        Assert.IsTrue(RowShapeWriter.HasAfterDelete(Keyed("Product"), Project("Attachment:OwnerType:OwnerId:Product|Customer:remove")));
        Assert.IsFalse(RowShapeWriter.HasDeleteCheck(Keyed("Product"), Project("Attachment:OwnerType:OwnerId:Product|Customer:remove")));
        Assert.IsFalse(RowShapeWriter.HasAfterDelete(Keyed("Product"), Project()));
    }

    [TestMethod]
    public void The_python_api_registers_events_on_the_link_table_and_the_listed_tables()
    {
        string code = PythonLinkWriter.File(PythonLinkWriter.Links(Database(), Project()), Project());
        Expect.Contains(code, "ATTACHMENT_TARGETS = {");
        Expect.Contains(code, "\"customer\": (\"Customer\", Customer, \"customer_id\"),");
        Expect.Contains(code, "event.listen(Attachment, \"before_insert\", check_attachment)");
        Expect.Contains(code, "@event.listens_for(Product, \"before_delete\")");
        Expect.Contains(code, "raise ApiError(409, \"Still linked\"");
        string removing = PythonLinkWriter.File(PythonLinkWriter.Links(Database(), Project("Attachment:OwnerType:OwnerId:Product|Customer:remove")), Project("Attachment:OwnerType:OwnerId:Product|Customer:remove"));
        Expect.Contains(removing, "@event.listens_for(Product, \"after_delete\")");
        Expect.Contains(removing, "connection.execute(delete(Attachment).where(Attachment.owner_type == \"Product\", Attachment.owner_id == target.product_id))");
    }

    [TestMethod]
    public void The_rust_api_has_a_check_for_the_link_table_and_a_refusal_for_each_listed_table()
    {
        var project = Project();
        string code = RustLinkWriter.File(RustLinkWriter.Links(Database(), project), project, SqlDialect.Sqlite);
        Expect.Contains(code, "pub async fn check_attachment(pool: &Pool, row: &mut Attachment) -> ApiResult<()> {");
        Expect.Contains(code, "\"customer\" => (\"Customer\",");
        Expect.Contains(code, "pub async fn refuse_linked_product(pool: &Pool, id: i32) -> ApiResult<()> {");
        Expect.Contains(code, "ApiError::Rejected(409, \"/problems/link-target-in-use\"");
    }

    [TestMethod]
    public void The_record_type_column_lists_the_listed_tables_so_a_form_shows_a_drop_down()
    {
        var database = Database();
        PolymorphicPlan.ApplyChoices(database, Project());
        CollectionAssert.AreEqual(new[] { "Product", "Customer", "Opportunity" }, database.Tables.Single(t => t.TableName == "Attachment").Columns.Single(c => c.Name == "OwnerType").Choices);
        Assert.IsNull(database.Tables.Single(t => t.TableName == "Product").Columns.Single(c => c.Name == "Name").Choices);
        var own = Database();
        own.Tables.Single(t => t.TableName == "Attachment").Columns.Single(c => c.Name == "OwnerType").Choices = ["Product", "Customer"];
        PolymorphicPlan.ApplyChoices(own, Project());
        Assert.HasCount(2, own.Tables.Single(t => t.TableName == "Attachment").Columns.Single(c => c.Name == "OwnerType").Choices!);
    }

    [TestMethod]
    public async Task The_generated_form_of_a_link_table_picks_the_record_type_from_a_drop_down()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_poly_" + Guid.NewGuid().ToString("N") + ".db");
        string output = Path.Combine(Path.GetTempPath(), "codegen_poly_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                foreach (string statement in new[]
                {
                    "CREATE TABLE Product (ProductId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100) NOT NULL, Sku VARCHAR(30) NOT NULL, Price DECIMAL(10,2) NOT NULL DEFAULT 0, Notes VARCHAR(200) NULL)",
                    "CREATE TABLE Customer (CustomerId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(100) NOT NULL, Email VARCHAR(100) NULL, City VARCHAR(60) NULL, Notes VARCHAR(200) NULL)",
                    "CREATE TABLE Attachment (AttachmentId INTEGER PRIMARY KEY AUTOINCREMENT, OwnerType VARCHAR(30) NOT NULL, OwnerId INTEGER NOT NULL, Title VARCHAR(120) NOT NULL, Url VARCHAR(300) NULL, Caption VARCHAR(200) NULL)"
                })
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = statement;
                    command.ExecuteNonQuery();
                }
            }
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Api,React", ["Dashboard"] = "false", ["AccessMode"] = "Ef",
                ["PolymorphicLinks"] = "Attachment:OwnerType:OwnerId:Product|Customer", ["Screens"] = "Attachment"
            };
            var request = new CodeGenNew.Connections.ConnectionRequest { Provider = CodeGenNew.Connections.DatabaseProvider.Sqlite, ServerName = "", DatabaseName = path };
            var provider = CodeGenNew.SchemaIntrospection.SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new CodeGenNew.Generation.GenerateOptions { Project = ProjectSettings.FromValues(values), Stacks = ["Api", "React"], OutputDirectory = output, DatabaseName = path, Schema = "main" });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            string page = File.ReadAllText(Directory.GetFiles(output, "AttachmentPage.tsx", SearchOption.AllDirectories).Single());
            Expect.Contains(page, "<option value=\"Product\">Product</option>");
            Expect.Contains(page, "<option value=\"Customer\">Customer</option>");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    [TestMethod]
    public void A_listed_table_gets_a_grid_of_the_rows_that_name_it()
    {
        var database = Database();
        var project = Project();
        PolymorphicPlan.ApplyChoices(database, project);
        PolymorphicPlan.ApplyChoices(database, project);
        var product = database.Tables.First(t => t.TableName == "Product");
        Assert.HasCount(1, product.ChildForeignKeys);
        Assert.IsTrue(product.HasAtLeastOneChildForeignKey);
        Assert.HasCount(0, database.Tables.First(t => t.TableName == "Attachment").ChildForeignKeys);
        var grid = ChildGrid.For(product, project).Single();
        Assert.AreEqual("Attachments", grid.Title);
        Assert.AreEqual("ownerId", grid.ForeignKeyJson);
        Assert.AreEqual("ownerType", grid.TypeJson);
        Assert.AreEqual("Product", grid.TypeValue);
        CollectionAssert.Contains(grid.HiddenColumns.ToList(), "OwnerType");
        Assert.AreEqual("OwnerType", DesktopChild.For(product, project, true).Single().TypeProp);
    }

    [TestMethod]
    public void A_link_table_without_a_screen_gives_no_grid()
    {
        var database = Database();
        var project = Project("Attachment:OwnerType:OwnerId:Product|Customer|Opportunity", ("Screens", "Product,Customer,Opportunity"));
        PolymorphicPlan.ApplyChoices(database, project);
        Assert.HasCount(0, database.Tables.First(t => t.TableName == "Product").ChildForeignKeys);
    }
}
