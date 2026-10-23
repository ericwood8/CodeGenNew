using System.Data;
using System.Text.RegularExpressions;
using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> The MySQL set, bit(n), year, tinyint and spatial columns: what they are to the generator, how a value is checked and how each stack reads and writes them. </summary>
[TestClass]
public class MySqlTypeNotationTests
{
    private static readonly string[] Spatial = ["geometry", "point", "linestring", "polygon", "multipoint", "multilinestring", "multipolygon", "geometrycollection"];

    private static bool Whole(string pattern, string value) => Regex.IsMatch(value, "^(?:" + pattern + ")$");

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    // ---- the types ----

    [TestMethod]
    public void A_wide_bit_column_is_a_whole_number_and_a_single_bit_is_a_flag()
    {
        Assert.AreEqual(("bit", 0, 0, 0, "bit(1)"), MySqlSchemaProvider.MapType("bit", "bit(1)", null, 1, null));
        Assert.AreEqual(("bigint", 0, 0, 0, "bigint"), MySqlSchemaProvider.MapType("bit", "bit(8)", null, 8, null));
        Assert.AreEqual(("bigint", 0, 0, 0, "bigint"), MySqlSchemaProvider.MapType("bit", "bit(63)", null, 63, null));
        Assert.AreEqual(("decimal", 0, 20, 0, "decimal(20,0)"), MySqlSchemaProvider.MapType("bit", "bit(64)", null, 64, null));
    }

    [TestMethod]
    public void A_set_is_text_as_long_as_all_its_members_with_commas_between_them()
    {
        var (type, length, _, _, declaration) = MySqlSchemaProvider.MapType("set", "set('wifi','parking','pool')", null, null, null);

        Assert.AreEqual("varchar", type);
        Assert.AreEqual("wifi".Length + "parking".Length + "pool".Length + 2, length);
        Assert.AreEqual($"varchar({length})", declaration);
        CollectionAssert.AreEqual(new[] { "wifi", "parking", "pool" }, MySqlSchemaProvider.ParseSetValues("set('wifi','parking','pool')")!);
        CollectionAssert.AreEqual(new[] { "it's", "b" }, MySqlSchemaProvider.ParseSetValues("set('it''s','b')")!);
        Assert.IsNull(MySqlSchemaProvider.ParseSetValues("enum('a','b')"));
        Assert.IsNull(MySqlSchemaProvider.ParseEnumValues("set('a','b')"), "a set is not a drop-down list");
    }

    [TestMethod]
    [DataRow("point", "geometry", 64)]
    [DataRow("polygon", "geometry", 4000)]
    [DataRow("geomcollection", "geometry", 4000)]
    public void A_spatial_column_is_text_and_a_year_stays_a_small_number(string dataType, string columnType, int length)
    {
        Assert.AreEqual(("varchar", length, 0, 0, $"varchar({length})"), MySqlSchemaProvider.MapType(dataType, columnType, null, null, null));
        Assert.AreEqual(("smallint", 0, 0, 0, "year"), MySqlSchemaProvider.MapType("year", "year", null, null, null));
        Assert.AreEqual("sql_variant", MySqlSchemaProvider.MapType("xyz", "xyz", null, null, null).SqlTypeName);
    }

    [TestMethod]
    [DataRow("tinyint", "-128 and 127")]
    [DataRow("smallint unsigned", "0 and 65535")]
    [DataRow("mediumint", "-8388608 and 8388607")]
    [DataRow("mediumint unsigned", "0 and 16777215")]
    [DataRow("int unsigned", "0 and 4294967295")]
    [DataRow("year", "1901 and 2155")]
    [DataRow("smallint", null)]
    [DataRow("int", null)]
    [DataRow("tinyint unsigned", null)]
    public void A_type_the_generator_widens_keeps_its_own_limits(string declaration, string? expected) =>
        Assert.AreEqual(expected, MySqlSchemaProvider.TypeLimit(Raw(declaration, null)));

    [TestMethod]
    public void A_bit_column_keeps_the_numbers_its_bits_can_make()
    {
        Assert.AreEqual("0 and 255", MySqlSchemaProvider.TypeLimit(Raw("bigint", 8)));
        Assert.AreEqual("0 and 1099511627775", MySqlSchemaProvider.TypeLimit(Raw("bigint", 40)));
        Assert.AreEqual("0 and 18446744073709551615", MySqlSchemaProvider.TypeLimit(Raw("decimal(20,0)", 64)));
    }

    private static RawColumn Raw(string declaration, int? bits) =>
        new("c", 1, "smallint", 0, 0, 0, true, false, null, null, null, null, false, false, DeclarationOverride: declaration, BitWidth: bits);

    [TestMethod]
    public void The_limit_is_a_range_the_check_parser_reads()
    {
        var range = CheckConstraintParser.ParseRange("(`level` between -128 and 127)", "level")!;
        Assert.AreEqual(-128, range.Min);
        Assert.AreEqual(127, range.Max);
        var wide = CheckConstraintParser.ParseRange("(`flags` between 0 and 18446744073709551615)", "flags")!;
        Assert.AreEqual(0, wide.Min);
        Assert.AreEqual(1.8446744073709552E19, wide.Max);
    }

    // ---- the spatial notation ----

    [TestMethod]
    public void The_sample_of_every_spatial_type_has_the_notation_its_pattern_asks_for()
    {
        foreach (string name in Spatial)
        {
            var type = MySqlSpatialType.FromDataType(name)!;
            Assert.IsTrue(Whole(type.Pattern, type.Sample), $"{name}: {type.Sample}");
            Assert.IsLessThanOrEqualTo(type.MaxLength, type.Sample.Length, name);
            Assert.IsFalse(type.Description.AsSpan().ContainsAny('{', '}'), name);
        }
        Assert.AreEqual("geometrycollection", MySqlSpatialType.FromDataType("geomcollection")!.Name);
        Assert.IsNull(MySqlSpatialType.FromDataType("varchar"));
    }

    [TestMethod]
    [DataRow("point", "POINT(1 2)", true)]
    [DataRow("point", "POINT( -1.5   2e3 )", true)]
    [DataRow("point", "POINT(1)", false)]
    [DataRow("point", "POINT(1 2 3)", false)]
    [DataRow("point", "point(1 2)", false)]
    [DataRow("point", "LINESTRING(0 0,1 1)", false)]
    [DataRow("linestring", "LINESTRING(0 0,1 1,2 5)", true)]
    [DataRow("linestring", "LINESTRING(0 0)", false)]
    [DataRow("polygon", "POLYGON((0 0,0 1,1 1,0 0))", true)]
    [DataRow("polygon", "POLYGON((0 0,0 4,4 4,4 0,0 0),(1 1,1 2,2 2,1 1))", true)]
    [DataRow("polygon", "POLYGON((0 0,1 1))", false)]
    [DataRow("multipoint", "MULTIPOINT((0 0),(1 1))", true)]
    [DataRow("multipoint", "MULTIPOINT(0 0,1 1)", true)]
    [DataRow("multilinestring", "MULTILINESTRING((0 0,1 1),(2 2,3 3))", true)]
    [DataRow("multipolygon", "MULTIPOLYGON(((0 0,0 1,1 1,0 0)),((5 5,5 6,6 6,5 5)))", true)]
    [DataRow("geometrycollection", "GEOMETRYCOLLECTION EMPTY", true)]
    [DataRow("geometrycollection", "GEOMETRYCOLLECTION(POINT(1 2),LINESTRING(0 0,1 1))", true)]
    [DataRow("geometrycollection", "POINT(1 2)", false)]
    [DataRow("geometry", "POINT(1 2)", true)]
    [DataRow("geometry", "MULTIPOLYGON(((0 0,0 1,1 1,0 0)))", true)]
    [DataRow("geometry", "GEOMETRYCOLLECTION(POINT(1 2))", true)]
    [DataRow("geometry", "CIRCLE(1 2)", false)]
    public void The_pattern_accepts_the_notation_and_refuses_the_rest(string type, string value, bool accepted) =>
        Assert.AreEqual(accepted, Whole(MySqlSpatialType.FromDataType(type)!.Pattern, value), $"{type}: {value}");

    [TestMethod]
    public void Every_spatial_type_converts_in_the_class_the_context_carries()
    {
        foreach (string name in new[] { "ToBinary", "ToText", "ReadShape", "WriteShape", "ReadBinary", "Format" })
            Assert.IsTrue(MySqlSpatialSource.Code.Contains(" " + name + "("), name);
        Assert.IsTrue(MySqlSpatialSource.Code.Contains("public static ValueConverter<string, byte[]> Converter(int srid)"));
        Assert.IsFalse(MySqlSpatialSource.Code.Contains("\"\"\""));
        foreach (string keyword in new[] { "POINT", "LINESTRING", "POLYGON", "MULTIPOINT", "MULTILINESTRING", "MULTIPOLYGON", "GEOMETRYCOLLECTION" })
            Assert.IsTrue(MySqlSpatialSource.Code.Contains($"\"{keyword}\""), keyword);
    }

    // ---- the set notation ----

    [TestMethod]
    public void A_set_holds_none_or_some_of_its_members_separated_by_commas()
    {
        var notation = MySqlSetNotation.Of(["wifi", "parking", "late checkout", "a.b"]);

        Assert.IsTrue(Whole(notation.Pattern, ""));
        Assert.IsTrue(Whole(notation.Pattern, "wifi"));
        Assert.IsTrue(Whole(notation.Pattern, "wifi,late checkout"));
        Assert.IsTrue(Whole(notation.Pattern, "a.b"));
        Assert.IsFalse(Whole(notation.Pattern, "aXb"), "a dot in a member is a dot");
        Assert.IsFalse(Whole(notation.Pattern, "wifi, parking"), "no spaces after the comma");
        Assert.IsFalse(Whole(notation.Pattern, "sauna"));
        Assert.IsFalse(Whole(notation.Pattern, "wifi,"));
        Assert.AreEqual("wifi", notation.Sample);
        Assert.IsTrue(Whole(notation.Pattern, notation.Sample));
        Assert.IsFalse(notation.Noun.AsSpan().ContainsAny('{', '}'));
    }

    private static ColumnModel SetColumn(bool nullable) =>
        Sample.Column("Perks", SqlDbType.VarChar, characters: 40, nullable: nullable, ordinal: 2, setValues: ["wifi", "pool"]);

    [TestMethod]
    public void A_set_column_is_checked_for_its_members_and_no_member_is_a_value_even_when_it_cannot_be_NULL()
    {
        var rule = SetColumn(nullable: false).RuleOf(Project())!;

        Assert.AreEqual(TextShape.Custom, rule.Shape);
        Assert.IsFalse(rule.Required, "an empty set is a value");
        Assert.AreEqual("wifi", ApiSampleValues.Text(SetColumn(false)));
        Assert.IsTrue(ApiSampleValues.HasFixedFormat(SetColumn(false), Project()));
        var attributes = ValidationAttributes.Of(SetColumn(false), Project(), "Place");
        Assert.Contains("[Required(AllowEmptyStrings = true)]", attributes);
    }

    // ---- the routines and the other stacks ----

    private static TableModel Place(params ColumnModel[] more) => Sample.Table("Place", [
        Sample.Column("PlaceId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("PlaceName", SqlDbType.VarChar, characters: 50, ordinal: 2),
        .. more]);

    private static TableModel AsMySql(TableModel t) => new()
    {
        SchemaName = "db", TableName = t.TableName, QuotedName = "`" + t.TableName + "`", Dialect = SqlDialect.MySql,
        Columns = t.Columns, PrimaryKeyColumns = t.PrimaryKeyColumns, ForeignKeys = t.ForeignKeys, ChildForeignKeys = t.ChildForeignKeys,
        DisplayColumns = t.DisplayColumns, LookupShape = t.LookupShape
    };

    private static ColumnModel Shape(string name = "Spot", string type = "point", int srid = 0) =>
        Sample.Column(name, SqlDbType.VarChar, characters: MySqlSpatialType.FromDataType(type)!.MaxLength, nullable: true, ordinal: 3, spatialType: type, srid: srid);

    [TestMethod]
    public void The_routines_write_a_spatial_column_from_its_text_in_its_reference_system_and_search_leaves_it_out()
    {
        var table = AsMySql(Place(Shape(), Shape("Spot2", "point", 4326)));

        string insert = MySqlProcedures.Insert(table), update = MySqlProcedures.Update(table), save = MySqlProcedures.Save(table), search = MySqlProcedures.Search(table);

        Expect.Contains(insert, "ST_GeomFromText(NULLIF(TRIM(`pSpot`), ''), 0, 'axis-order=long-lat')");
        Expect.Contains(insert, "ST_GeomFromText(NULLIF(TRIM(`pSpot2`), ''), 4326, 'axis-order=long-lat')");
        Expect.Contains(update, "`Spot` = COALESCE(ST_GeomFromText(NULLIF(TRIM(`pSpot`), ''), 0, 'axis-order=long-lat'), `Spot`)");
        Expect.Contains(save, "ST_GeomFromText(NULLIF(`pSpot2`, ''), 4326, 'axis-order=long-lat')");
        Expect.DoesNotContain(search, "pSpot");
        Expect.DoesNotContain(insert, "ST_GeomFromText(NULLIF(TRIM(`pPlaceName`)");
    }

    [TestMethod]
    public void Rust_reads_a_spatial_column_as_text_and_a_bit_column_as_a_number()
    {
        var table = AsMySql(Place(Shape("Spot", "point", 4326),
            Sample.Column("Flags", SqlDbType.BigInt, nullable: true, ordinal: 4, bitWidth: 8),
            Sample.Column("Wide", SqlDbType.Decimal, precision: 20, scale: 0, nullable: true, ordinal: 5, bitWidth: 64)));

        var rust = RustTable.Of(table, Project());

        Expect.Contains(rust.SelectList, "ST_AsText(`Spot`, 'axis-order=long-lat') AS `Spot`");
        Expect.Contains(rust.SelectList, "CAST(`Flags` AS SIGNED) AS `Flags`");
        Expect.Contains(rust.SelectList, "CAST(`Wide` AS DECIMAL(20,0)) AS `Wide`");
        Expect.Contains(rust.Insert, "ST_GeomFromText(NULLIF(?, ''), 4326, 'axis-order=long-lat')");
        Expect.Contains(rust.Update, "ST_GeomFromText(NULLIF(?, ''), 4326, 'axis-order=long-lat')");
    }

    [TestMethod]
    public void Python_wraps_a_spatial_column_and_a_bit_column_and_keeps_a_year()
    {
        Assert.AreEqual(("str", "MySqlSpatial(4326)"), PythonTable.TypeOf(Shape("Spot", "point", 4326), SqlDialect.MySql)!.Value);
        Assert.AreEqual(("int", "MySqlBit(False)"), PythonTable.TypeOf(Sample.Column("Flags", SqlDbType.BigInt, ordinal: 4, bitWidth: 8), SqlDialect.MySql)!.Value);
        Assert.AreEqual(("Decimal", "MySqlBit(True)"), PythonTable.TypeOf(Sample.Column("Wide", SqlDbType.Decimal, precision: 20, scale: 0, ordinal: 4, bitWidth: 64), SqlDialect.MySql)!.Value);
        Assert.AreEqual(("int", "YEAR"), PythonTable.TypeOf(Sample.Column("OpenYear", SqlDbType.SmallInt, ordinal: 4, sqlDeclaration: "year"), SqlDialect.MySql)!.Value);
    }

    // ---- the C# context ----

    private static DatabaseModel Database(params ColumnModel[] columns) => new()
    {
        DatabaseName = "Acme", SchemaName = "db", Dialect = SqlDialect.MySql,
        Tables = [AsMySql(Place(columns))]
    };

    [TestMethod]
    public async Task The_mysql_context_converts_each_spatial_column_and_carries_the_class()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_DbContext_v1.tt"), Database(Shape("Spot", "point", 4326), Shape("Area", "polygon")), Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string cs = result.GeneratedText!.Replace("\r\n", "\n");

        Expect.Contains(cs, "modelBuilder.Entity<Place>().Property(e => e.Spot).HasConversion(MySqlSpatial.Converter(4326), MySqlSpatial.Comparer);");
        Expect.Contains(cs, "modelBuilder.Entity<Place>().Property(e => e.Area).HasConversion(MySqlSpatial.Converter(0), MySqlSpatial.Comparer);");
        Expect.Contains(cs, "internal static class MySqlSpatial");
        Expect.Contains(cs, "using Microsoft.EntityFrameworkCore.Storage.ValueConversion;");
        Expect.DoesNotContain(cs, "HasColumnType");
        Expect.DoesNotContain(cs, "internal static class PgText");
    }

    [TestMethod]
    public async Task With_configuration_classes_the_converter_is_in_the_table_s_configuration()
    {
        var table = Database(Shape("Spot", "point", 4326)).Tables[0];
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_EfConfiguration_v1.tt"), table, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));

        Expect.Contains(result.GeneratedText!.Replace("\r\n", "\n"), ".HasConversion(MySqlSpatial.Converter(4326), MySqlSpatial.Comparer)");
    }

    [TestMethod]
    public void A_spatial_column_is_not_sorted_by()
    {
        var table = Place(Shape());

        CollectionAssert.DoesNotContain(SearchSort.Entries(table).Select(e => e.Name).ToList(), "Spot");
        CollectionAssert.DoesNotContain(table.SearchableColumns.Select(c => c.Name).ToList(), "Spot");
    }
}
