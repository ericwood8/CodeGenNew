using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Catalog;
using CodeGenNew.Core.Sql;

namespace CodeGenNew.Tests.Tax;

/// <summary> FacetTables: an attribute table (product, name, value) read back as facets by the product table. </summary>
[TestClass]
public class CatalogTests
{
    private static TableModel Products(SqlDialect dialect = SqlDialect.SqlServer) => Sample.Table("Product",
    [
        Sample.Column("ProductId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CatalogNumber", SqlDbType.VarChar, characters: 40, ordinal: 2),
        Sample.Column("Name", SqlDbType.VarChar, characters: 120, ordinal: 3),
        Sample.Column("Description", SqlDbType.VarChar, nullable: true, characters: 255, ordinal: 4),
        Sample.Column("Manufacturer", SqlDbType.VarChar, characters: 60, ordinal: 5),
    ], [], dialect: dialect);

    private static TableModel Attributes(bool indexed = false, bool nullableName = false, string valueName = "Value", bool number = false, SqlDialect dialect = SqlDialect.SqlServer) => Sample.Table("ProductAttribute",
    [
        Sample.Column("ProductAttributeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("ProductId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Name", SqlDbType.VarChar, nullable: nullableName, characters: 40, ordinal: 3),
        Sample.Column(valueName, SqlDbType.VarChar, nullable: true, characters: 80, ordinal: 4),
        .. number ? new[] { Sample.Column("NumberValue", SqlDbType.Decimal, nullable: true, ordinal: 5) } : [],
    ], [Sample.ForeignKey("ProductId", "Product", "ProductId")],
    indexes: indexed ? [new IndexModel("IX_Product_Name", false, ["ProductId", "Name"])] : [], dialect: dialect);

    private static DatabaseModel Database(TableModel? attributes = null) =>
        new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Products(), attributes ?? Attributes()] };

    private static ProjectSettings Project(string value = "ProductAttribute") =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["FacetTables"] = value, ["AccessMode"] = "Ef" });

    [TestMethod]
    public void The_product_table_and_the_columns_are_found_from_the_foreign_key_and_the_names()
    {
        var database = Database();
        var project = Project();
        var shape = CatalogPlan.ForProduct(database.Tables[0], project, database)!;
        Assert.AreEqual("Product", shape.Product.TableName);
        Assert.AreEqual("ProductAttribute", shape.Attributes.TableName);
        Assert.AreEqual("ProductId", shape.AttributeProduct.Name);
        Assert.AreEqual("Name", shape.AttributeName.Name);
        Assert.AreEqual("Value", shape.AttributeValue.Name);
        Assert.AreEqual("Name", shape.Title!.Name);
        Assert.AreEqual("CatalogNumber", shape.Code!.Name);
        Assert.IsNotNull(shape.Detail);
        Assert.HasCount(4, shape.SearchColumns);
        Assert.IsTrue(CatalogPlan.IsAttributeTable("ProductAttribute", project));
        Assert.IsNull(CatalogPlan.ForProduct(database.Tables[1], project, database), "the attribute table is not the product table");
        Assert.HasCount(0, CatalogPlan.Problems(database, project));
    }

    [TestMethod]
    public void The_columns_can_be_named_and_the_text_the_name_and_value_are_found_by_other_words()
    {
        var database = Database(Attributes(valueName: "AttributeValue"));
        Assert.AreEqual("AttributeValue", CatalogPlan.ForProduct(database.Tables[0], Project(), database)!.AttributeValue.Name);
        Assert.AreEqual("Name", CatalogPlan.ForProduct(database.Tables[0], Project("ProductAttribute:ProductId:Name:AttributeValue"), database)!.AttributeName.Name);
    }

    [TestMethod]
    public void A_problem_is_named_for_a_missing_table_a_nullable_name_and_no_foreign_key()
    {
        Assert.Contains("is not a table of the database", CatalogPlan.Problems(Database(), Project("Missing")).Single());
        Assert.Contains("two text columns", CatalogPlan.Problems(Database(Attributes(nullableName: true)), Project()).Single());
        var noKey = Sample.Table("ProductAttribute",
        [
            Sample.Column("ProductAttributeId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("ProductId", SqlDbType.Int, ordinal: 2),
            Sample.Column("Name", SqlDbType.VarChar, characters: 40, ordinal: 3),
            Sample.Column("Value", SqlDbType.VarChar, characters: 80, ordinal: 4),
        ], []);
        Assert.Contains("exactly one foreign key", CatalogPlan.Problems(Database(noKey), Project()).Single());
    }

    [TestMethod]
    public void An_attribute_table_without_an_index_on_the_product_and_name_is_a_warning()
    {
        Assert.HasCount(1, CatalogPlan.MissingIndexes(Database(), Project()));
        Assert.HasCount(0, CatalogPlan.MissingIndexes(Database(Attributes(indexed: true)), Project()));
    }

    [TestMethod]
    public void The_csharp_routes_count_each_attribute_without_its_own_filter_and_search_in_any_case()
    {
        var database = Database();
        var shape = CatalogPlan.ForProduct(database.Tables[0], Project(), database)!;
        string code = CatalogWriter.Code(shape, "ShopContext");
        Assert.Contains("Matching(context, q, filters, ranges, name)", code, "an attribute is counted without its own filter");
        Assert.Contains(".ToLower().Contains(term)", code);
        Assert.Contains("facet-filter-invalid", code);
        Assert.Contains("context.Set<ProductAttribute>().Any(a => a.ProductId == x.ProductId && a.Name == attribute && chosen.Contains(a.Value!))", code);
        string maps = CatalogWriter.MapLines(shape);
        Assert.Contains("§R/facets", maps);
        Assert.Contains("§R/narrow", maps);
    }

    [TestMethod]
    public void The_react_box_reads_the_two_routes_and_the_test_ticks_a_value()
    {
        var database = Database();
        var project = Project();
        Assert.IsTrue(CatalogScreens.HasBox(database.Tables[0], ProjectWithMemory(database, project)));
        string api = string.Join("\n", CatalogScreens.ReactApiLines());
        Assert.Contains("catalogFacets", api);
        Assert.Contains("/facets?", api);
        Assert.Contains("/narrow?", api);
        string box = string.Join("\n", CatalogScreens.ReactBox());
        Assert.Contains("aria-label=\"Catalog keyword\"", box);
        Assert.Contains("toggleFacet(group.name, v.value)", box);
        string routed = string.Join("\n", CatalogScreens.ReactBoxTests("productApi", "ProductPage", true));
        Assert.Contains("<MemoryRouter><ProductPage /></MemoryRouter>", routed);
        Assert.DoesNotContain("<MemoryRouter>", string.Join("\n", CatalogScreens.ReactBoxTests("productApi", "ProductPage")));
        Assert.DoesNotContain("waitFor", routed, "the test needs no import beyond the ones every page test has");
    }

    private static ProjectSettings ProjectWithMemory(DatabaseModel database, ProjectSettings project)
    {
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        return project;
    }

    [TestMethod]
    public void A_project_with_facet_tables_has_a_row_shape_for_the_product_table()
    {
        var database = Database();
        var project = Project();
        CodeGenNew.Core.Rate.RatePlan.Remember(database, project);
        Assert.IsTrue(RowShapePlan.Any(project));
        Assert.IsTrue(RowShapePlan.Has(database.Tables[0], project));
        Assert.IsFalse(RowShapePlan.Has(database.Tables[1], project));
    }

    [TestMethod]
    public void The_csharp_engine_is_shared_by_the_routes_and_the_desktop_list()
    {
        var database = Database();
        var shape = CatalogPlan.ForProduct(database.Tables[0], Project(), database)!;
        string engine = CatalogWriter.Engine(shape, "ShopContext");
        Assert.Contains("FacetsCoreAsync(ShopContext context", engine);
        Assert.Contains("NarrowCoreAsync(ShopContext context", engine);
        Assert.DoesNotContain("IResult", engine, "the engine answers data; the routes wrap it");
        Assert.Contains(engine, CatalogWriter.Code(shape, "ShopContext"));
        string desktop = CatalogScreens.WinUI3Members(shape, "_context", "ShopContext");
        Assert.Contains(engine, desktop);
        Assert.Contains("FacetsCoreAsync(_context, query, filters", desktop);
    }

    [TestMethod]
    public void The_python_routes_count_each_attribute_without_its_own_filter()
    {
        var database = Database();
        var project = Project();
        var shape = CatalogPlan.ForProduct(database.Tables[0], project, database)!;
        var names = Core.Python.PythonCatalogWriter.NamesFor(shape, Core.Python.PythonTable.Of(database.Tables[0], project), project)!;
        string helper = Core.Python.PythonCatalogWriter.Helper(names);
        string routes = Core.Python.PythonCatalogWriter.Routes(names, "/api/products", "", "");
        Assert.Contains("func.lower(Product.catalog_number).contains(term, autoescape=True)", helper);
        Assert.Contains("facet-filter-invalid", helper);
        Assert.Contains("@router.get(\"/api/products/facets\"", routes);
        Assert.Contains("@router.get(\"/api/products/narrow\"", routes);
        Assert.Contains("catalog_matching(q, chosen, bounds, name)", routes, "an attribute is counted without its own filter");
        Assert.Contains("size = min(25 if page_size is None else page_size, 200)", routes);
    }

    [TestMethod]
    public void The_rust_routes_build_the_query_for_the_database()
    {
        var database = Database();
        var project = Project();
        var shape = CatalogPlan.ForProduct(database.Tables[0], project, database)!;
        string code = Core.Rust.RustCatalogWriter.Code(shape, Core.Rust.RustTable.Of(database.Tables[0], project), project, DialectInfo.For(SqlDialect.PostgreSql));
        Assert.Contains("ESCAPE '!'", code);
        Assert.Contains("LOWER(", code);
        Assert.Contains("COUNT(DISTINCT", code);
        Assert.Contains("catalog_where(&mut builder, &query, Some(name))", code, "an attribute is counted without its own filter");
        Assert.Contains("facet-filter-invalid", code);
        string lines = Core.Rust.RustCatalogWriter.RouteLines("/api/products", "");
        Assert.Contains("/api/products/facets", lines);
        Assert.Contains("/api/products/narrow", lines);
    }

    [TestMethod]
    public void The_angular_blazor_and_desktop_boxes_read_the_two_routes()
    {
        string service = string.Join("\n", CatalogScreens.AngularServiceLines("apiUrl"));
        Assert.Contains("catalogFacets(query: string, filters: string[])", service);
        Assert.Contains("/narrow`", service);
        string box = string.Join("\n", CatalogScreens.AngularBox());
        Assert.Contains("(change)=\"toggleFacet(group.name, v.value)\"", box);
        Assert.Contains("aria-label=\"Catalog keyword\"", box);
        Assert.Contains("this.service.catalogNarrow(query, this.facetFilters, pageNumber)", string.Join("\n", CatalogScreens.AngularHandlers()));
        Assert.Contains("component.findProducts()", CatalogScreens.AngularBoxTest());

        string client = string.Join("\n", CatalogScreens.CsClientLines("{Url}"));
        Assert.Contains("CatalogFacetsAsync(string query, IEnumerable<string> filters)", client);
        Assert.Contains("$\"{Url}/narrow{CatalogQuery(query, filters, page)}\"", client);
        Assert.Contains("@onchange=\"() => ToggleFacetAsync(choice.Name, choice.Value)\"", string.Join("\n", CatalogScreens.BlazorBox()));
        Assert.Contains("Client.CatalogNarrowAsync(query, FacetFilters, pageNumber)", string.Join("\n", CatalogScreens.BlazorHandlers("Client")));
        Assert.Contains("filter=Material%3ACopper", CatalogScreens.BlazorBoxTest());

        Assert.Contains("Click=\"OnNarrowByClick\"", CatalogScreens.DesktopBox());
        Assert.Contains("new ToggleMenuFlyoutItem", CatalogScreens.DesktopHandlers());
        Assert.Contains("Client.CatalogFacetsAsync(query, CatalogFilters)", CatalogScreens.UnoMembers());
    }

    [TestMethod]
    public void The_keyword_reads_the_attribute_values_and_a_number_column_makes_ranges()
    {
        var database = Database(Attributes(number: true));
        var shape = CatalogPlan.ForProduct(database.Tables[0], Project(), database)!;
        Assert.AreEqual("NumberValue", shape.AttributeNumber!.Name);
        string code = CatalogWriter.Code(shape, "ShopContext");
        Assert.Contains("a.Value!.ToLower().Contains(term)", code, "the keyword reads the attribute values too");
        Assert.Contains("decimal? low = await numbers.MinAsync(", code);
        Assert.Contains("(decimal?)a.NumberValue >= low", code);
        Assert.Contains("[FromQuery(Name = \"range\")]", CatalogWriter.MapLines(shape));
        Assert.IsNull(CatalogPlan.ForProduct(Database().Tables[0], Project(), Database())!.AttributeNumber, "no number column, no range");
        Assert.DoesNotContain("MinAsync", CatalogWriter.Code(CatalogPlan.ForProduct(Database().Tables[0], Project(), Database())!, "ShopContext"));
    }

    [TestMethod]
    public void A_number_column_can_be_named_and_a_text_column_is_refused()
    {
        var database = Database(Attributes(number: true));
        Assert.AreEqual("NumberValue", CatalogPlan.ForProduct(database.Tables[0], Project("ProductAttribute:ProductId:Name:Value:NumberValue"), database)!.AttributeNumber!.Name);
        Assert.Contains("not a numeric column", CatalogPlan.Problems(database, Project("ProductAttribute:ProductId:Name:Value:Name")).Single());
    }

    [TestMethod]
    public void Full_text_makes_the_keyword_a_contains_condition_on_sql_server_only()
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["FacetTables"] = "ProductAttribute", ["FacetFullText"] = "true", ["AccessMode"] = "Ef" });
        var database = Database();
        var shape = CatalogPlan.ForProduct(database.Tables[0], project, database)!;
        Assert.IsTrue(shape.FullText);
        string code = CatalogWriter.Code(shape, "ShopContext");
        Assert.Contains("EF.Functions.Contains(x.CatalogNumber, query)", code);
        Assert.Contains("EF.Functions.Contains(a.Value, query)", code);
        Assert.Contains("FullTextQuery(text)", code);
        Assert.DoesNotContain(".ToLower().Contains(term)", code);
        var other = new DatabaseModel { DatabaseName = "Shop", SchemaName = "main", Tables = [Products(SqlDialect.Sqlite), Attributes(dialect: SqlDialect.Sqlite)] };
        Assert.Contains("SQL Server only", CatalogPlan.Problems(other, project).Single());
    }

    [TestMethod]
    public void The_generated_python_and_rust_tests_are_written_for_a_product_table_they_can_fill()
    {
        var database = Database(Attributes(number: true));
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["FacetTables"] = "ProductAttribute", ["AccessMode"] = "Ef", ["PythonTests"] = "true", ["RustTests"] = "true" });
        var plan = CatalogTestPlan.For(database, project);
        Assert.IsNotNull(plan);
        Assert.AreEqual("name", plan.TitleJson);
        Assert.Contains("catalogNumber", plan.ProductBody);
        string python = Core.Python.PythonCatalogTests.File(database, project);
        Assert.Contains("def test_a_range_keeps_the_products", python);
        Assert.Contains("def test_the_keyword_reads_the_attribute_values_in_any_case", python);
        string rust = Core.Rust.RustCatalogTests.File(database, project);
        Assert.Contains("async fn a_range_keeps_the_products", rust);
        Assert.Contains("facet-filter-invalid", python + rust);
        Assert.Contains("PY_TestCatalog", project.ImpliedPlanTemplates);
    }
}
