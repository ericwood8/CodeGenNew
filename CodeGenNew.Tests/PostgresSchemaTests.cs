using CodeGenNew.Connections;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

[TestClass]
public class PostgresSchemaTests
{
    [TestMethod]
    public void A_bare_numeric_is_38_digits_with_4_places_and_keeps_its_own_name_in_the_sql()
    {
        var (sqlType, _, precision, scale, declaration) = PostgresSchemaProvider.MapType("numeric", null, null, null);

        Assert.AreEqual("decimal", sqlType);
        Assert.AreEqual((38, 4, "numeric"), (precision, scale, declaration));
        Assert.AreEqual("sql_variant", PostgresSchemaProvider.MapType("_text", null, null, null).SqlTypeName);   // an array is unsupported
    }

    [TestMethod]
    [DataRow("int2", null, null, null, "smallint", "smallint")]
    [DataRow("int4", null, null, null, "int", "integer")]
    [DataRow("int8", null, null, null, "bigint", "bigint")]
    [DataRow("bool", null, null, null, "bit", "boolean")]
    [DataRow("numeric", null, 5, 2, "decimal", "numeric(5,2)")]
    [DataRow("float8", null, null, null, "float", "double precision")]
    [DataRow("float4", null, null, null, "real", "real")]
    [DataRow("date", null, null, null, "date", "date")]
    [DataRow("timestamp", null, null, null, "datetime2", "timestamp")]
    [DataRow("timestamptz", null, null, null, "datetimeoffset", "timestamptz")]
    [DataRow("varchar", 50, null, null, "varchar", "varchar(50)")]
    [DataRow("bpchar", 3, null, null, "char", "char(3)")]
    [DataRow("text", null, null, null, "varchar", "text")]
    [DataRow("uuid", null, null, null, "uniqueidentifier", "uuid")]
    [DataRow("bytea", null, null, null, "varbinary", "bytea")]
    [DataRow("geometry", null, null, null, "sql_variant", "geometry")]
    public void A_postgres_type_maps_to_the_sql_server_vocabulary_and_keeps_its_own_declaration(
        string udtName, int? length, int? precision, int? scale, string expectedSqlType, string expectedDeclaration)
    {
        var (sqlType, _, _, _, declaration) = PostgresSchemaProvider.MapType(udtName, length, precision, scale);

        Assert.AreEqual(expectedSqlType, sqlType);
        Assert.AreEqual(expectedDeclaration, declaration);
    }

    [TestMethod]
    public void Unbounded_text_is_a_long_text_column()
    {
        var (_, maxLength, _, _, _) = PostgresSchemaProvider.MapType("text", null, null, null);

        Assert.AreEqual(-1, maxLength);
    }

    [TestMethod]
    [DataRow("now()", "getdate()")]
    [DataRow("CURRENT_TIMESTAMP", "getdate()")]
    [DataRow("gen_random_uuid()", "newid()")]
    [DataRow("true", "1")]
    [DataRow("false", "0")]
    [DataRow("0", "0")]
    [DataRow("(0)::numeric", "0")]
    [DataRow("'Open'::character varying", "'Open'")]
    [DataRow("0.00", "0.00")]
    public void A_postgres_default_is_translated_for_the_csharp_default_resolver(string pgDefault, string expected)
    {
        Assert.AreEqual(expected, PostgresSchemaProvider.NormalizeDefault(pgDefault));
    }

    [TestMethod]
    public void No_default_stays_no_default()
    {
        Assert.IsNull(PostgresSchemaProvider.NormalizeDefault(null));
        Assert.IsNull(PostgresSchemaProvider.NormalizeDefault("  "));
    }

    [TestMethod]
    public void The_host_may_carry_a_port()
    {
        var request = new ConnectionRequest
        {
            Provider = DatabaseProvider.PostgreSql, ServerName = "dbhost:6543", DatabaseName = "InvoiceSystemPg",
            AuthMode = AuthMode.SqlLogin, UserName = "dev", Password = "x"
        };

        string text = request.BuildConnectionString();

        StringAssert.Contains(text, "Host=dbhost");
        StringAssert.Contains(text, "Port=6543");
        StringAssert.Contains(text, "Database=InvoiceSystemPg");
    }

    [TestMethod]
    public void The_default_port_is_5432()
    {
        var request = new ConnectionRequest
        {
            Provider = DatabaseProvider.PostgreSql, ServerName = "localhost", DatabaseName = "d",
            AuthMode = AuthMode.SqlLogin, UserName = "dev", Password = "x"
        };

        StringAssert.Contains(request.BuildConnectionString(), "Port=5432");
    }

    [TestMethod]
    public void Postgres_needs_a_user_name()
    {
        var request = new ConnectionRequest { Provider = DatabaseProvider.PostgreSql, ServerName = "localhost", DatabaseName = "d" };

        Assert.ThrowsExactly<ArgumentException>(() => request.BuildConnectionString());
    }
}
