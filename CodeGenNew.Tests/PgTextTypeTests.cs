using System.Data;
using System.Text.RegularExpressions;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> The PostgreSQL array, interval, range and geometric columns: held as text in PostgreSQL's own notation by every generated stack (PgTextType). </summary>
[TestClass]
public class PgTextTypeTests
{
    private static readonly string[] Udts =
    [
        "_text", "_varchar", "_bpchar", "_int2", "_int4", "_int8", "_numeric", "_float4", "_float8", "_bool", "_uuid", "_date",
        "interval", "int4range", "int8range", "numrange", "daterange", "point", "line", "lseg", "box", "path", "polygon", "circle"
    ];

    private static bool Whole(PgTextType type, string value) => Regex.IsMatch(value, "^(?:" + type.Pattern + ")$");

    [TestMethod]
    [DataRow("_int4", "int4[]", PgTextFamily.Array)]
    [DataRow("_text", "text[]", PgTextFamily.Array)]
    [DataRow("_uuid", "uuid[]", PgTextFamily.Array)]
    [DataRow("interval", "interval", PgTextFamily.Interval)]
    [DataRow("daterange", "daterange", PgTextFamily.Range)]
    [DataRow("numrange", "numrange", PgTextFamily.Range)]
    [DataRow("point", "point", PgTextFamily.Geometric)]
    [DataRow("polygon", "polygon", PgTextFamily.Geometric)]
    public void A_catalog_type_is_named_as_a_cast_writes_it(string udt, string name, PgTextFamily family)
    {
        var type = PgTextType.FromUdt(udt)!;

        Assert.AreEqual(name, type.Name);
        Assert.AreEqual(family, type.Family);
        Assert.AreEqual(name, PgTextType.FromName(name)!.Name, "the cast name finds the type again");
    }

    [TestMethod]
    [DataRow("int4")]
    [DataRow("varchar")]
    [DataRow("jsonb")]
    [DataRow("geometry")]
    [DataRow("tsrange")]
    [DataRow("_timestamp")]
    [DataRow("inet")]
    public void Any_other_type_is_not_one_of_them(string udt)
    {
        Assert.IsNull(PgTextType.FromUdt(udt));
        Assert.IsNull(PgTextType.FromName(udt));
    }

    [TestMethod]
    public void The_sample_of_every_type_has_the_notation_the_pattern_asks_for()
    {
        foreach (string udt in Udts)
        {
            var type = PgTextType.FromUdt(udt)!;
            Assert.IsTrue(Whole(type, type.Sample), $"{type.Name}: {type.Sample}");
            Assert.IsLessThanOrEqualTo(type.MaxLength, type.Sample.Length, type.Name);
        }
    }

    [TestMethod]
    [DataRow("_int4", "{1,2,3}", true)]
    [DataRow("_int4", "{}", true)]
    [DataRow("_int4", "{ -1 , 2 }", true)]
    [DataRow("_int4", "{1,x}", false)]
    [DataRow("_int4", "{1,NULL}", false)]
    [DataRow("_int4", "1,2", false)]
    [DataRow("_int4", "{1,2", false)]
    [DataRow("_text", "{a,\"b c\",\"d,e\"}", true)]
    [DataRow("_text", "{a,NULL}", true)]
    [DataRow("_text", "{\"open", false)]
    [DataRow("_bool", "{t,f,true,false}", true)]
    [DataRow("_bool", "{yes}", false)]
    [DataRow("_numeric", "{1.5,-2,3e4}", true)]
    [DataRow("_date", "{2024-01-02}", true)]
    [DataRow("_date", "{02/01/2024}", false)]
    [DataRow("_uuid", "{3f2504e0-4f89-11d3-9a0c-0305e82c3301}", true)]
    [DataRow("interval", "1 day 02:03:04", true)]
    [DataRow("interval", "90 minutes", true)]
    [DataRow("interval", "-1 days -02:00:00", true)]
    [DataRow("interval", "36:00:00.25", true)]
    [DataRow("interval", "3 months", false)]
    [DataRow("interval", "soon", false)]
    [DataRow("int4range", "[1,5)", true)]
    [DataRow("int4range", "(,5]", true)]
    [DataRow("int4range", "empty", true)]
    [DataRow("int4range", "[a,b)", false)]
    [DataRow("int4range", "1-5", false)]
    [DataRow("daterange", "[2024-01-01,2024-02-01)", true)]
    [DataRow("numrange", "[1.5,2.5]", true)]
    [DataRow("point", "(1.5,-2)", true)]
    [DataRow("point", "1,2", false)]
    [DataRow("box", "(2,2),(1,1)", true)]
    [DataRow("circle", "<(0,0),0.5>", true)]
    [DataRow("lseg", "[(0,0),(3,4)]", true)]
    [DataRow("line", "{1,-1,0.5}", true)]
    [DataRow("path", "[(1,1),(2,2),(3,1)]", true)]
    [DataRow("path", "((1,1),(2,2))", true)]
    [DataRow("polygon", "((0,0),(0,1),(1,1))", true)]
    [DataRow("polygon", "((0,0)", false)]
    public void The_pattern_accepts_the_notation_and_refuses_the_rest(string udt, string value, bool accepted) =>
        Assert.AreEqual(accepted, Whole(PgTextType.FromUdt(udt)!, value), $"{udt}: {value}");

    [TestMethod]
    public void The_message_never_has_a_brace_that_a_format_string_would_read()
    {
        foreach (string udt in Udts)
            Assert.IsFalse(PgTextType.FromUdt(udt)!.Description.AsSpan().ContainsAny('{', '}'), udt);
    }

    [TestMethod]
    public void Every_converter_the_context_names_is_in_the_class_it_carries()
    {
        foreach (string udt in Udts)
        {
            string converter = PgTextType.FromUdt(udt)!.Converter(nullable: true);
            string member = Regex.Match(converter, @"^PgText\.(\w+)").Groups[1].Value;
            Assert.IsTrue(Regex.IsMatch(PgTextSource.Code, @"public static ValueConverter<string, [^>]+(<[^>]+>)?> " + member + @"(<T>)?\("), $"{udt}: {converter}");
        }
    }

    [TestMethod]
    public void The_converter_of_an_array_says_whether_a_blank_is_NULL_or_empty()
    {
        Assert.AreEqual("PgText.Array<int>(true)", PgTextType.FromUdt("_int4")!.Converter(nullable: true));
        Assert.AreEqual("PgText.TextArray(false)", PgTextType.FromUdt("_varchar")!.Converter(nullable: false));
        Assert.AreEqual("PgText.Range<DateOnly>()", PgTextType.FromUdt("daterange")!.Converter(nullable: true));
        Assert.AreEqual("PgText.LineSegment()", PgTextType.FromUdt("lseg")!.Converter(nullable: true));
    }

    [TestMethod]
    public void The_helper_class_has_no_run_of_three_quotes_that_would_end_its_source_early()
    {
        Assert.IsFalse(PgTextSource.Code.Contains("\"\"\""));
        Assert.IsTrue(PgTextSource.Code.StartsWith("/// <summary>"));
    }

    // ---- the column and its rule ----

    private static ColumnModel Column(string name, string type, bool nullable = true) =>
        Sample.Column(name, SqlDbType.VarChar, characters: PgTextType.FromName(type)!.MaxLength, nullable: nullable, ordinal: 2, textType: type);

    [TestMethod]
    public void A_text_type_column_is_checked_for_its_notation()
    {
        var column = Column("Scores", "int4[]");
        var rule = column.RuleOf(ProjectSettings.FromValues([new("ProjectName", "Acme")]))!;

        Assert.AreEqual(TextShape.Custom, rule.Shape);
        Assert.AreEqual(PgTextType.FromName("int4[]")!.Pattern, rule.Pattern);
        Assert.AreEqual("an array of whole numbers (items between braces, separated by commas)", ColumnRules.NounOf(rule));
        Assert.AreEqual(255, rule.MaxLength);
        Assert.IsFalse(rule.Required);
        Assert.IsTrue(Column("Tags", "text[]", nullable: false).RuleOf(ProjectSettings.FromValues([new("ProjectName", "Acme")]))!.Required);
    }

    [TestMethod]
    public void The_data_annotation_holds_a_value_to_the_pattern_and_its_message_is_no_format_string()
    {
        var table = Sample.Table("Place", [Sample.Column("PlaceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Column("Scores", "int4[]")]);
        var project = ProjectSettings.FromValues([new("ProjectName", "Acme")]);

        string attributes = string.Join("\n", ValidationAttributes.Of(table.Columns[1], project, table.TableName));

        Expect.Contains(attributes, "[RegularExpression(@\"^(?:" + PgTextType.FromName("int4[]")!.Pattern);
        Assert.AreEqual(-1, attributes.IndexOf("ErrorMessage = \"{", StringComparison.Ordinal));
    }

    [TestMethod]
    public void The_sample_value_of_a_request_is_the_notation()
    {
        Assert.AreEqual("\"{1,2}\"", ApiSampleValues.Value(Column("Scores", "int4[]"), ProjectSettings.FromValues([new("ProjectName", "Acme")])));
        Assert.AreEqual("\"1 day 02:03:04\"", ApiSampleValues.Value(Column("Duration", "interval"), ProjectSettings.FromValues([new("ProjectName", "Acme")])));
    }

    [TestMethod]
    public void The_column_is_searched_through_its_text()
    {
        var table = Sample.Table("Place",
        [
            Sample.Column("PlaceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("PlaceName", SqlDbType.VarChar, characters: 50, ordinal: 2),
            Column("Tags", "text[]")
        ]);

        CollectionAssert.Contains(table.SearchableColumns.Select(c => c.Name).ToList(), "Tags");
        Assert.IsTrue(table.Columns[2].DbCastType == "text[]");
    }

    // ---- the PostgreSQL functions ----

    private static TableModel AsPostgres(TableModel t) => new()
    {
        SchemaName = "public", TableName = t.TableName, QuotedName = t.QuotedName, Dialect = SqlDialect.PostgreSql,
        Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys, ChildForeignKeys = t.ChildForeignKeys,
        DisplayColumns = t.DisplayColumns, LookupShape = t.LookupShape
    };

    [TestMethod]
    public void The_functions_cast_the_text_to_the_type_and_a_blank_is_NULL_or_an_empty_array()
    {
        var pg = AsPostgres(Sample.Table("Place",
        [
            Sample.Column("PlaceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("PlaceName", SqlDbType.VarChar, characters: 50, ordinal: 2),
            Column("Tags", "text[]", nullable: false),
            Column("Scores", "int4[]"),
            Column("Duration", "interval")
        ]));

        string insert = PostgresProcedures.Insert(pg), update = PostgresProcedures.Update(pg), save = PostgresProcedures.Save(pg);

        Expect.Contains(insert, "COALESCE(NULLIF(btrim(\"pTags\"), '')::text[], '{}')");
        Expect.Contains(insert, "NULLIF(btrim(\"pScores\"), '')::int4[]");
        Expect.Contains(insert, "NULLIF(btrim(\"pDuration\"), '')::interval");
        Expect.Contains(insert, "\"pDuration\" varchar(64) DEFAULT NULL");
        Expect.Contains(insert, "\"pTags\" varchar(255) DEFAULT NULL");   // '' is no value of an array
        Expect.Contains(update, "COALESCE(NULLIF(btrim(\"pScores\"), '')::int4[], \"Scores\")");
        Expect.Contains(save, "\"Duration\" = NULLIF(\"pDuration\", '')::interval");
        Expect.DoesNotContain(insert, "btrim(\"pPlaceName\")::");
    }

    // ---- the C# context ----

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static DatabaseModel Database(SqlDialect dialect) => new()
    {
        DatabaseName = "Acme",
        SchemaName = "public",
        Dialect = dialect,
        Tables =
        [
            new TableModel
            {
                SchemaName = "public", TableName = "Place", QuotedName = "\"Place\"", Dialect = dialect,
                Columns =
                [
                    Sample.Column("PlaceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                    Column("Tags", "text[]", nullable: false),
                    Column("Scores", "int4[]"),
                    Column("Duration", "interval"),
                    Column("Budget", "int4range"),
                    Column("Spot", "point")
                ],
                PrimaryKeyColumns = [Sample.Column("PlaceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)],
                ForeignKeys = [], ChildForeignKeys = []
            }
        ]
    };

    [TestMethod]
    public async Task The_postgres_context_converts_each_text_type_column_and_carries_the_converters()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_DbContext_v1.tt"), Database(SqlDialect.PostgreSql), Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string cs = result.GeneratedText!.Replace("\r\n", "\n");

        Expect.Contains(cs, "modelBuilder.Entity<Place>().Property(e => e.Tags).HasConversion(PgText.TextArray(false), PgText.Comparer).HasColumnType(\"text[]\");");
        Expect.Contains(cs, "modelBuilder.Entity<Place>().Property(e => e.Scores).HasConversion(PgText.Array<int>(true), PgText.Comparer).HasColumnType(\"int4[]\");");
        Expect.Contains(cs, "HasConversion(PgText.Interval(), PgText.Comparer).HasColumnType(\"interval\");");
        Expect.Contains(cs, "HasConversion(PgText.Range<int>(), PgText.Comparer).HasColumnType(\"int4range\");");
        Expect.Contains(cs, "HasConversion(PgText.Point(), PgText.Comparer).HasColumnType(\"point\");");
        Expect.Contains(cs, "internal static class PgText");
        Expect.Contains(cs, "using NpgsqlTypes;");
        Expect.Contains(cs, "using Microsoft.EntityFrameworkCore.Storage.ValueConversion;");
        Expect.Contains(cs, "OnModelCreatingPartial(modelBuilder);");
    }

    [TestMethod]
    public async Task A_context_without_such_a_column_has_neither_the_converters_nor_an_OnModelCreating()
    {
        var plain = new DatabaseModel
        {
            DatabaseName = "Acme", SchemaName = "public", Dialect = SqlDialect.PostgreSql,
            Tables = [new TableModel
            {
                SchemaName = "public", TableName = "Place", QuotedName = "\"Place\"", Dialect = SqlDialect.PostgreSql,
                Columns = [Sample.Column("PlaceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)],
                PrimaryKeyColumns = [Sample.Column("PlaceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)],
                ForeignKeys = [], ChildForeignKeys = []
            }]
        };

        var result = await Repo.Cache.RunAsync(Repo.Template("CS_DbContext_v1.tt"), plain, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));

        Expect.DoesNotContain(result.GeneratedText!, "PgText");
        Expect.DoesNotContain(result.GeneratedText!, "OnModelCreating");
    }

    [TestMethod]
    public async Task With_configuration_classes_the_converter_is_in_the_table_s_configuration()
    {
        var database = Database(SqlDialect.PostgreSql);
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_EfConfiguration_v1.tt"), database.Tables[0], Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string cs = result.GeneratedText!.Replace("\r\n", "\n");

        Expect.Contains(cs, ".HasColumnType(\"int4[]\").HasConversion(PgText.Array<int>(true), PgText.Comparer)");
        Expect.Contains(cs, ".HasColumnType(\"interval\").HasConversion(PgText.Interval(), PgText.Comparer)");
    }

    // ---- the other stacks ----

    [TestMethod]
    public void Rust_reads_the_text_of_the_column_and_casts_what_it_writes()
    {
        var pg = AsPostgres(Sample.Table("Place",
        [
            Sample.Column("PlaceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("PlaceName", SqlDbType.VarChar, characters: 50, ordinal: 2),
            Column("Scores", "int4[]")
        ]));
        var table = RustTable.Of(pg, Project());

        Expect.Contains(table.SelectList, "\"Scores\"::text AS \"Scores\"");
        Expect.Contains(string.Join("\n", table.Insert), "CAST(NULLIF($");
        Expect.Contains(string.Join("\n", table.Update), "::text, '') AS int4[])");
    }

    [TestMethod]
    public void Python_wraps_the_column_in_the_type_that_casts_both_ways()
    {
        var column = Column("Scores", "int4[]");

        var type = PythonTable.TypeOf(column, SqlDialect.PostgreSql)!.Value;

        Assert.AreEqual("str", type.Annotation);
        Assert.AreEqual("PgText(\"int4[]\")", type.SqlAlchemy);
    }
}
