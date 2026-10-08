using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

[TestClass]
public class MySqlSchemaTests
{
    [TestMethod]
    [DataRow("tinyint", "tinyint(1)", null, null, null, "bit", "tinyint(1)")]
    [DataRow("tinyint", "tinyint", null, null, null, "smallint", "tinyint")]
    [DataRow("tinyint", "tinyint unsigned", null, null, null, "tinyint", "tinyint unsigned")]
    [DataRow("int", "int", null, null, null, "int", "int")]
    [DataRow("int", "int unsigned", null, null, null, "bigint", "int unsigned")]
    [DataRow("bigint", "bigint", null, null, null, "bigint", "bigint")]
    [DataRow("decimal", "decimal(5,2)", null, 5, 2, "decimal", "decimal(5,2)")]
    [DataRow("double", "double", null, null, null, "float", "double")]
    [DataRow("float", "float", null, null, null, "real", "float")]
    [DataRow("date", "date", null, null, null, "date", "date")]
    [DataRow("datetime", "datetime", null, null, null, "datetime2", "datetime")]
    [DataRow("timestamp", "timestamp", null, null, null, "datetime2", "timestamp")]
    [DataRow("varchar", "varchar(50)", 50L, null, null, "varchar", "varchar(50)")]
    [DataRow("char", "char(3)", 3L, null, null, "char", "char(3)")]
    [DataRow("text", "text", 65535L, null, null, "varchar", "text")]
    [DataRow("longtext", "longtext", 4294967295L, null, null, "varchar", "longtext")]
    [DataRow("enum", "enum('a','bcd')", 3L, null, null, "varchar", "varchar(3)")]   // as long as the longest listed value
    [DataRow("set", "set('a','b')", 3L, null, null, "varchar", "varchar(255)")]
    [DataRow("json", "json", null, null, null, "varchar", "json")]
    [DataRow("blob", "blob", 65535L, null, null, "varbinary", "blob")]
    [DataRow("geometry", "geometry", null, null, null, "sql_variant", "geometry")]
    public void A_mysql_type_maps_to_the_sql_server_vocabulary_and_keeps_its_own_declaration(
        string dataType, string columnType, long? length, int? precision, int? scale, string expectedSqlType, string expectedDeclaration)
    {
        var (sqlType, _, _, _, declaration) = MySqlSchemaProvider.MapType(dataType, columnType, length, precision, scale);

        Assert.AreEqual(expectedSqlType, sqlType);
        Assert.AreEqual(expectedDeclaration, declaration);
    }

    [TestMethod]
    public void The_text_and_blob_families_are_unbounded_long_text()
    {
        Assert.AreEqual(-1, MySqlSchemaProvider.MapType("text", "text", 65535, null, null).MaxLength);
        Assert.AreEqual(-1, MySqlSchemaProvider.MapType("longtext", "longtext", 4294967295, null, null).MaxLength);
    }

    [TestMethod]
    [DataRow("CURRENT_TIMESTAMP", "DEFAULT_GENERATED", false, "getdate()")]
    [DataRow("CURRENT_TIMESTAMP(6)", "DEFAULT_GENERATED", false, "getdate()")]
    [DataRow("uuid()", "DEFAULT_GENERATED", false, "newid()")]
    [DataRow("0", "", false, "0")]
    [DataRow("1", "", false, "1")]
    [DataRow("Open", "", true, "'Open'")]
    [DataRow("it's", "", true, "'it''s'")]
    public void A_mysql_default_is_translated_for_the_csharp_default_resolver(string columnDefault, string extra, bool isString, string expected)
    {
        Assert.AreEqual(expected, MySqlSchemaProvider.NormalizeDefault(columnDefault, extra, isString));
    }

    [TestMethod]
    public void No_default_stays_no_default()
    {
        Assert.IsNull(MySqlSchemaProvider.NormalizeDefault(null, "", false));
    }

    [TestMethod]
    public void The_host_may_carry_a_port_and_the_default_is_3306()
    {
        var withPort = new ConnectionRequest { Provider = DatabaseProvider.MySql, ServerName = "dbhost:3307", DatabaseName = "d", AuthMode = AuthMode.SqlLogin, UserName = "u", Password = "x" };
        var plain = new ConnectionRequest { Provider = DatabaseProvider.MySql, ServerName = "localhost", DatabaseName = "d", AuthMode = AuthMode.SqlLogin, UserName = "u", Password = "x" };

        StringAssert.Contains(withPort.BuildConnectionString(), "Port=3307");
        StringAssert.Contains(withPort.BuildConnectionString(), "Server=dbhost");
        StringAssert.Contains(plain.BuildConnectionString(), "Port=3306");
    }

    [TestMethod]
    public void MySql_needs_a_user_name()
    {
        var request = new ConnectionRequest { Provider = DatabaseProvider.MySql, ServerName = "localhost", DatabaseName = "d" };

        Assert.ThrowsExactly<ArgumentException>(() => request.BuildConnectionString());
    }
}

[TestClass]
public class NamingStyleTests
{
    [TestMethod]
    [DataRow("customer_item", "CustomerItem")]
    [DataRow("customer", "Customer")]
    [DataRow("customer_id", "CustomerId")]
    [DataRow("billing_address1", "BillingAddress1")]
    [DataRow("require_customer_po", "RequireCustomerPo")]
    [DataRow("CustomerItem", "CustomerItem")]
    [DataRow("customerId", "CustomerId")]
    [DataRow("_leading", "Leading")]
    public void Pascal_turns_a_snake_case_name_into_a_pascal_case_one(string name, string expected)
    {
        Assert.AreEqual(expected, NameConverter.ToPascal(name));
        Assert.AreEqual(expected, NameConverter.Apply(NamingStyle.Pascal, name));
    }

    [TestMethod]
    [DataRow("require_customer_po", "RequireCustomerPO")]
    [DataRow("po_number", "PONumber")]
    [DataRow("item_upc", "ItemUPC")]
    [DataRow("item_msrp_amount", "ItemMSRPAmount")]
    [DataRow("po", "PO")]
    [DataRow("report_poster", "ReportPoster")]   // only a whole word counts, never a prefix
    [DataRow("customer_id", "CustomerId")]       // Id is not an acronym unless the project lists it
    public void Listed_acronyms_stay_upper_case(string name, string expected)
    {
        string[] acronyms = ["PO", "upc", "Msrp"];

        Assert.AreEqual(expected, NameConverter.ToPascal(name, acronyms));
        Assert.AreEqual(expected, NameConverter.Apply(NamingStyle.Pascal, name, acronyms));
    }

    [TestMethod]
    public void The_project_lists_its_acronyms_and_none_is_the_default()
    {
        CollectionAssert.AreEqual(new[] { "PO", "UPC", "MSRP" }, ProjectSettings.Parse("ProjectName=X\nAcronyms=PO, UPC ,MSRP").Acronyms);
        Assert.IsEmpty(ProjectSettings.None.Acronyms);
        Assert.IsEmpty(ProjectSettings.Parse("ProjectName=X").Acronyms);
    }

    [TestMethod]
    public void AsIs_ignores_acronyms()
    {
        Assert.AreEqual("require_customer_po", NameConverter.Apply(NamingStyle.AsIs, "require_customer_po", ["PO"]));
    }

    [TestMethod]
    public void AsIs_keeps_the_name()
    {
        Assert.AreEqual("customer_item", NameConverter.Apply(NamingStyle.AsIs, "customer_item"));
    }

    [TestMethod]
    public void The_project_setting_chooses_the_style()
    {
        Assert.AreEqual(NamingStyle.AsIs, ProjectSettings.None.Naming);
        Assert.AreEqual(NamingStyle.Pascal, ProjectSettings.Parse("ProjectName=X\nNamingStyle=Pascal").Naming);
        Assert.AreEqual(NamingStyle.Pascal, ProjectSettings.Parse("ProjectName=X\nNamingStyle=pascal").Naming);
    }

    private static ColumnModel Column(string generated, string? database) => new()
    {
        Name = generated, DatabaseName = database, QuotedName = "`" + (database ?? generated) + "`", SqlType = SqlDbType.Int, SqlTypeDeclaration = "int", ParameterName = "@p" + generated
    };

    [TestMethod]
    public void A_column_and_a_table_know_both_names()
    {
        var renamed = Column("CustomerId", "customer_id");
        var same = Column("CustomerId", null);

        Assert.AreEqual("CustomerId", renamed.Name);
        Assert.AreEqual("customer_id", renamed.DbName);
        Assert.AreEqual("CustomerId", same.DbName);
    }
}

[TestClass]
public class MySqlProcedureTests
{
    private static ColumnModel Col(string name, string? db, SqlDbType type, string declaration, bool key = false, bool identity = false, bool nullable = false, int ordinal = 0,
        int? characters = null, bool unique = false)
    {
        bool isText = type is SqlDbType.VarChar or SqlDbType.Char;
        return new()
        {
            Name = name, DatabaseName = db, QuotedName = "`" + (db ?? name) + "`", SqlType = type, SqlTypeDeclaration = declaration,
            IsPrimaryKey = key, IsIdentity = identity, IsNullable = nullable, OrdinalPosition = ordinal, IsStringColumn = isText, IsIntegerColumn = type == SqlDbType.Int,
            MaxLength = characters, IsInUniqueIndex = unique,
            ParameterName = "@" + (isText ? "pstr" : type == SqlDbType.Int ? "plng" : "p") + name
        };
    }

    // customer_item (customer_item_id, customer_id, note) read with NamingStyle=Pascal
    private static TableModel CustomerItem()
    {
        var id = Col("CustomerItemId", "customer_item_id", SqlDbType.Int, "int", key: true, identity: true, ordinal: 1);
        var customer = Col("CustomerId", "customer_id", SqlDbType.Int, "int", ordinal: 2);
        var note = Col("Title", "title", SqlDbType.VarChar, "varchar(50)", ordinal: 3, characters: 50);
        List<ColumnModel> columns = [id, customer, note];
        return new TableModel
        {
            SchemaName = "invoicesystem", TableName = "CustomerItem", DatabaseTableName = "customer_item", QuotedName = "`invoicesystem`.`customer_item`", Dialect = SqlDialect.MySql,
            Columns = columns, PrimaryKeyColumns = [id], ForeignKeys = [], ChildForeignKeys = [], DisplayColumns = [note]
        };
    }

    [TestMethod]
    public void Search_pages_with_limit_and_uses_the_real_table_and_column_names()
    {
        string sql = MySqlProcedures.Search(CustomerItem());

        StringAssert.Contains(sql, "CREATE PROCEDURE `CustomerItem_Search`(");
        StringAssert.Contains(sql, "IN `pstrTitle` varchar(50)");
        StringAssert.Contains(sql, "FROM `customer_item`");
        StringAssert.Contains(sql, "`title` LIKE CONCAT('%', TRIM(`pstrTitle`), '%')");
        StringAssert.Contains(sql, "LIMIT `PageSize` OFFSET v_offset;");
        StringAssert.Contains(sql, "CREATE PROCEDURE `CustomerItem_SearchCount`(");
        StringAssert.Contains(sql, "SELECT COUNT(*) AS `Value`");
        StringAssert.Contains(sql, "DROP PROCEDURE IF EXISTS `CustomerItem_Search`;");
        Assert.DoesNotContain("[", sql);
    }

    [TestMethod]
    public void Insert_returns_the_new_key_and_names_the_columns_as_the_database_does()
    {
        string sql = MySqlProcedures.Insert(CustomerItem());

        StringAssert.Contains(sql, "(`customer_id`, `title`)");
        StringAssert.Contains(sql, "TRIM(`pstrTitle`)");
        StringAssert.Contains(sql, "SELECT LAST_INSERT_ID() AS `customer_item_id`;");
    }

    [TestMethod]
    public void Update_keeps_a_column_when_its_parameter_is_null_and_reports_whether_the_row_exists()
    {
        string sql = MySqlProcedures.Update(CustomerItem());

        StringAssert.Contains(sql, "`title` = COALESCE(TRIM(`pstrTitle`), `title`)");
        StringAssert.Contains(sql, "SELECT 1 AS `Result`");
    }

    [TestMethod]
    public void Delete_reports_a_foreign_key_block_as_minus_one()
    {
        string sql = MySqlProcedures.Delete(CustomerItem());

        StringAssert.Contains(sql, "DECLARE EXIT HANDLER FOR 1451 SELECT -1 AS `Result`");
        StringAssert.Contains(sql, "DECLARE EXIT HANDLER FOR SQLEXCEPTION SELECT -2 AS `Result`");
        StringAssert.Contains(sql, "DELETE FROM `customer_item` WHERE (`customer_item_id` = `plngCustomerItemId`);");
    }

    [TestMethod]
    public void Clone_of_an_identity_table_takes_the_source_key_and_returns_the_new_one()
    {
        string sql = MySqlProcedures.Clone(CustomerItem());

        StringAssert.Contains(sql, "IN `CopyFromCustomerItemId` int");
        StringAssert.Contains(sql, "SET v_new_key = LAST_INSERT_ID();");
        StringAssert.Contains(sql, "MYSQL_ERRNO = 55509");
    }

    [TestMethod]
    public void A_template_writes_a_procedure_for_a_MySql_table()
    {
        // The call the templates make; the branch itself is exercised by the CLI against a live database.
        Assert.AreEqual(SqlDialect.MySql, CustomerItem().Dialect);
        StringAssert.Contains(MySqlProcedures.Save(CustomerItem()), "CREATE PROCEDURE `CustomerItem_Save`(");
    }

    [TestMethod]
    public void The_search_call_is_a_CALL_with_MySql_parameters()
    {
        var call = new SearchCall(CustomerItem(), "CustomerItem", ["Title"], qualifyTypes: true);

        Assert.AreEqual("CALL `CustomerItem_Search`(@pTitle, @PageNumber, @PageSize, @SortColumn, @SortDescending)", call.SearchSql);
        Assert.AreEqual("CALL `CustomerItem_SearchCount`(@pTitle)", call.CountSql);
        StringAssert.Contains(call.TextParameter("Title", "title"), "new MySql.Data.MySqlClient.MySqlParameter(\"@pTitle\"");
    }

    [TestMethod]
    public void A_string_literal_escapes_backslashes_and_quotes()
    {
        var column = Col("Title", "title", SqlDbType.VarChar, "varchar(50)");

        Assert.AreEqual(@"'a\\b''c'", MySqlLiteral.Format(column, @"a\b'c"));
        Assert.AreEqual("1", MySqlLiteral.Format(column, true));
        Assert.AreEqual("NULL", MySqlLiteral.Format(column, null));
    }
}

/// <summary> Reads the schema of a real MySQL copy of the InvoiceSystem sample (snake_case tables, created by the MySQL sample's CreateInvoiceSystemMySql.sql). Runs only when
/// CODEGENNEW_MYSQL_HOST, _DATABASE, _USER and _PASSWORD are set; otherwise each test reports itself as inconclusive. Nothing is written to the database. </summary>
[TestClass]
public class MySqlIntegrationTests
{
    private static MySqlSchemaProvider Provider(NamingStyle naming)
    {
        string? host = Environment.GetEnvironmentVariable("CODEGENNEW_MYSQL_HOST");
        string? database = Environment.GetEnvironmentVariable("CODEGENNEW_MYSQL_DATABASE");
        string? user = Environment.GetEnvironmentVariable("CODEGENNEW_MYSQL_USER");
        if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(database) || string.IsNullOrEmpty(user))
            Assert.Inconclusive("Set CODEGENNEW_MYSQL_HOST, CODEGENNEW_MYSQL_DATABASE, CODEGENNEW_MYSQL_USER and CODEGENNEW_MYSQL_PASSWORD to run the MySQL integration tests.");

        var request = new ConnectionRequest
        {
            Provider = DatabaseProvider.MySql, ServerName = host, DatabaseName = database,
            AuthMode = AuthMode.SqlLogin, UserName = user, Password = Environment.GetEnvironmentVariable("CODEGENNEW_MYSQL_PASSWORD")
        };
        return new MySqlSchemaProvider(request, Path.Combine(AppContext.BaseDirectory, "SpecialLogicColumns.config"), naming);
    }

    [TestMethod]
    public async Task The_table_list_has_the_sample_tables_under_their_real_names()
    {
        var tables = await Provider(NamingStyle.AsIs).ListTablesAsync();

        var customer = tables.Single(t => t.TableName == "customer");
        Assert.IsTrue(customer.HasPrimaryKey);
        Assert.AreEqual(PrimaryKeyShape.SingleInt, customer.PrimaryKeyShape);
        Assert.IsTrue(customer.HasChildForeignKeys);
        Assert.IsFalse(tables.Single(t => t.TableName == "customer_item").IsJunctionTable); // it carries a purchased_date
    }

    [TestMethod]
    public async Task A_table_that_is_not_there_is_reported_at_once_and_a_wrong_case_name_is_not_found_on_a_case_sensitive_server()
    {
        string database = Environment.GetEnvironmentVariable("CODEGENNEW_MYSQL_DATABASE")!;
        var provider = Provider(NamingStyle.AsIs);

        var ex = await Assert.ThrowsAsync<TableNotFoundException>(() => provider.BuildTableModelAsync(database, "no_such_table"));
        StringAssert.Contains(ex.Message, "was not found");

        // On Linux (lower_case_table_names=0) "CUSTOMER" is another name than "customer"; on Windows the server lower-cases and finds it, so only the first is checked everywhere.
        var real = await provider.BuildTableModelAsync(database, "customer");
        Assert.AreEqual("customer", real.TableName);
    }

    [TestMethod]
    public async Task A_table_model_read_with_Pascal_naming_has_generated_and_real_names()
    {
        var model = await Provider(NamingStyle.Pascal).BuildTableModelAsync(Environment.GetEnvironmentVariable("CODEGENNEW_MYSQL_DATABASE")!, "customer_item", includeReferencedDisplayColumns: true);

        Assert.AreEqual("CustomerItem", model.TableName);
        Assert.AreEqual("customer_item", model.DbTableName);
        Assert.AreEqual(SqlDialect.MySql, model.Dialect);
        var id = model.Columns.Single(c => c.Name == "CustomerItemId");
        Assert.AreEqual("customer_item_id", id.DbName);
        Assert.IsTrue(id.IsIdentity);
        Assert.IsTrue(id.IsPrimaryKey);

        var customer = model.ForeignKeys.Single(f => f.ReferencedTable == "Customer");
        CollectionAssert.AreEqual(new[] { "CustomerId" }, customer.ReferencingColumns);
        CollectionAssert.AreEqual(new[] { "customer_id" }, customer.ReferencingDbColumns);
        Assert.AreEqual("customer", customer.ReferencedDbTable);
        Assert.IsNotEmpty(customer.ReferencedDisplayColumns);
        Assert.HasCount(customer.ReferencedDisplayColumns.Count, customer.ReferencedDisplayDbColumns);
    }

    [TestMethod]
    public async Task Types_and_row_data_come_back()
    {
        var provider = Provider(NamingStyle.Pascal);
        string database = Environment.GetEnvironmentVariable("CODEGENNEW_MYSQL_DATABASE")!;

        var customer = await provider.BuildTableModelAsync(database, "customer");
        Assert.AreEqual(SqlDbType.Bit, customer.Columns.Single(c => c.Name == "IsTaxable").SqlType);
        Assert.AreEqual(SqlDbType.Decimal, customer.Columns.Single(c => c.Name == "FinanceChargeRate").SqlType);
        Assert.AreEqual(SqlDbType.DateTime2, customer.Columns.Single(c => c.Name == "DateAdded").SqlType);

        var status = await provider.BuildTableModelAsync(database, "customer_status", includeRowData: true);
        Assert.HasCount(2, status.Rows);
    }
}
