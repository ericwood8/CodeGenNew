using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

[TestClass]
public class SqlLiteralTests
{
    private static ColumnModel Col(SqlDbType type, int? characters = null) => Sample.Column("C", type, characters: characters);

    [TestMethod]
    public void Null_and_DBNull_are_NULL()
    {
        Assert.AreEqual("NULL", SqlLiteral.Format(Col(SqlDbType.Int), null));
        Assert.AreEqual("NULL", SqlLiteral.Format(Col(SqlDbType.Int), DBNull.Value));
    }

    [TestMethod]
    public void Text_has_its_quotes_doubled_and_unicode_text_is_N_prefixed()
    {
        Assert.AreEqual("'O''Brien'", SqlLiteral.Format(Col(SqlDbType.VarChar, 20), "O'Brien"));
        Assert.AreEqual("N'O''Brien'", SqlLiteral.Format(Col(SqlDbType.NVarChar, 20), "O'Brien"));
    }

    [TestMethod]
    public void Booleans_and_numbers_are_language_independent()
    {
        Assert.AreEqual("1", SqlLiteral.Format(Col(SqlDbType.Bit), true));
        Assert.AreEqual("0", SqlLiteral.Format(Col(SqlDbType.Bit), false));
        Assert.AreEqual("42", SqlLiteral.Format(Col(SqlDbType.Int), 42));

        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.AreEqual("12.5", SqlLiteral.Format(Col(SqlDbType.Decimal), 12.5m));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    [TestMethod]
    public void Dates_use_an_ISO_form_that_ignores_DATEFORMAT()
    {
        string literal = SqlLiteral.Format(Col(SqlDbType.DateTime), new DateTime(2025, 12, 25, 13, 30, 0));

        Assert.StartsWith("'2025-12-25", literal);
        Assert.EndsWith("'", literal);
    }

    [TestMethod]
    public void Binary_is_hex_and_a_guid_is_upper_case()
    {
        Assert.AreEqual("0x0AFF", SqlLiteral.Format(Col(SqlDbType.VarBinary), new byte[] { 0x0A, 0xFF }));
        Assert.AreEqual("'6F9619FF-8B86-D011-B42D-00C04FC964FF'",
            SqlLiteral.Format(Col(SqlDbType.UniqueIdentifier), Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff")));
    }
}

[TestClass]
public class DisplayColumnSelectorTests
{
    private static ColumnModel Display(string name, int rank) => Copy(Sample.Column(name, SqlDbType.NVarChar, characters: 50), rank);

    // ColumnModel is init-only: rebuild it with a display rank
    private static ColumnModel Copy(ColumnModel c, int rank) => new()
    {
        Name = c.Name, QuotedName = c.QuotedName, SqlType = c.SqlType, SqlTypeDeclaration = c.SqlTypeDeclaration,
        MaxLength = c.MaxLength, IsStringColumn = c.IsStringColumn, ParameterName = c.ParameterName, DisplayRank = rank
    };

    [TestMethod]
    public void Columns_matching_the_display_rule_are_chosen_in_table_order()
    {
        var columns = new List<ColumnModel>
        {
            Sample.Column("Id", SqlDbType.Int, primaryKey: true),
            Display("Description", 6),
            Display("Name", 2)
        };

        var chosen = DisplayColumnSelector.Select(columns, []);

        CollectionAssert.AreEqual(new[] { "Description", "Name" }, chosen.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void A_key_or_foreign_key_column_is_never_a_display_column()
    {
        // RegionCode matches a "*Code" pattern, but it IS the id of the parent: the real description must win
        var columns = new List<ColumnModel>
        {
            Display("RegionCode", 8),
            Sample.Column("Label", SqlDbType.NVarChar, characters: 50)
        };

        var chosen = DisplayColumnSelector.Select(columns, ["RegionCode"]);

        CollectionAssert.AreEqual(new[] { "Label" }, chosen.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void With_no_match_the_first_ordinary_text_column_is_used_and_a_table_without_text_gives_nothing()
    {
        var withText = new List<ColumnModel> { Sample.Column("Id", SqlDbType.Int, primaryKey: true), Sample.Column("Reason", SqlDbType.NVarChar, characters: 50) };
        var withoutText = new List<ColumnModel> { Sample.Column("Id", SqlDbType.Int, primaryKey: true), Sample.Column("Hours", SqlDbType.Int) };

        Assert.AreEqual("Reason", DisplayColumnSelector.Select(withText, []).Single().Name);
        Assert.IsEmpty(DisplayColumnSelector.Select(withoutText, []));
    }
}
