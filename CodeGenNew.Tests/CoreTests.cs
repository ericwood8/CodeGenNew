using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

[TestClass]
public class AppSettingsLoadTests
{
    [TestMethod]
    public void A_missing_file_gives_fresh_defaults()
    {
        var settings = AppSettings.Load(Path.Combine(Path.GetTempPath(), "does-not-exist-" + Guid.NewGuid().ToString("N") + ".json"));

        Assert.AreEqual("Output", settings.OutputDirectory);
        Assert.AreEqual("Templates", settings.TemplatesDirectory);
    }

    [TestMethod]
    public void An_existing_file_is_parsed_case_insensitively()
    {
        using var temp = new TempFolder();
        string path = temp.File("Settings.json", """{ "outputDirectory": "MyOutput", "lastConnection": { "serverName": "ERICSMINIPC" } }""");

        var settings = AppSettings.Load(path);

        Assert.AreEqual("MyOutput", settings.OutputDirectory);
        Assert.AreEqual("ERICSMINIPC", settings.LastConnection.ServerName);
    }
}

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

        var chosen = columns.SelectDisplayColumns([]);

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

        var chosen = columns.SelectDisplayColumns(["RegionCode"]);

        CollectionAssert.AreEqual(new[] { "Label" }, chosen.Select(c => c.Name).ToArray());
    }

    [TestMethod]
    public void With_no_match_the_first_ordinary_text_column_is_used_and_a_table_without_text_gives_nothing()
    {
        var withText = new List<ColumnModel> { Sample.Column("Id", SqlDbType.Int, primaryKey: true), Sample.Column("Reason", SqlDbType.NVarChar, characters: 50) };
        var withoutText = new List<ColumnModel> { Sample.Column("Id", SqlDbType.Int, primaryKey: true), Sample.Column("Hours", SqlDbType.Int) };

        Assert.AreEqual("Reason", withText.SelectDisplayColumns([]).Single().Name);
        Assert.IsEmpty(withoutText.SelectDisplayColumns([]));
    }
}

[TestClass]
public class TableModelChildForeignKeysTests
{
    [TestMethod]
    public void A_table_with_no_children_reports_none()
    {
        var parent = Sample.Table("E_TimeSheet", [Sample.Column("E_TimeSheetId", SqlDbType.Int, primaryKey: true)]);

        Assert.IsFalse(parent.HasAtLeastOneChildForeignKey);
        Assert.IsEmpty(parent.ChildForeignKeys);
    }

    [TestMethod]
    public void A_child_tables_foreign_key_back_to_the_parent_is_reported_on_the_parent()
    {
        var parent = Sample.Table("E_TimeSheet",
            [Sample.Column("E_TimeSheetId", SqlDbType.Int, primaryKey: true)],
            childForeignKeys: [Sample.ChildForeignKey("E_TimeSheetDetail", "E_TimeSheetId", "E_TimeSheetId")]);

        Assert.IsTrue(parent.HasAtLeastOneChildForeignKey);
        var child = parent.ChildForeignKeys.Single();
        Assert.AreEqual("E_TimeSheetDetail", child.ReferencingTable);
        Assert.AreEqual("E_TimeSheetId", child.ReferencingColumns.Single());
        Assert.AreEqual("E_TimeSheetId", child.ReferencedColumns.Single());
    }

    [TestMethod]
    public void A_self_referencing_table_can_be_its_own_child()
    {
        // e.g. a Category table with a ParentCategoryId column pointing at its own key -- both
        // TableModel.ForeignKeys (outgoing) and ChildForeignKeys (incoming) are legitimately non-empty.
        var table = Sample.Table("Category",
            [Sample.Column("CategoryId", SqlDbType.Int, primaryKey: true), Sample.Column("ParentCategoryId", SqlDbType.Int, nullable: true)],
            foreignKeys: [Sample.ForeignKey("ParentCategoryId", "Category", "CategoryId")],
            childForeignKeys: [Sample.ChildForeignKey("Category", "ParentCategoryId", "CategoryId")]);

        Assert.IsTrue(table.HasAtLeastOneForeignKey);
        Assert.IsTrue(table.HasAtLeastOneChildForeignKey);
    }
}

[TestClass]
public class TableModelJunctionTableTests
{
    [TestMethod]
    public void A_composite_key_junction_table_is_recognized()
    {
        // e.g. UserRole(UserId, RoleId): the natural-composite-key many-to-many shape, two separate FKs.
        var userRole = Sample.CompositeKeyJunction();

        Assert.IsTrue(userRole.IsJunctionTable);
    }

    [TestMethod]
    public void A_surrogate_key_junction_table_is_recognized()
    {
        // The real-world shape found on ProvidenceOgas.dbo.NameBaseGroupXref: an identity PK plus two
        // plain FK columns and CreateDate/CreateUser audit columns -- confirmed against a live database.
        var xref = Sample.JunctionWithSurrogateKey();

        Assert.IsTrue(xref.IsJunctionTable);
        var fks = xref.JunctionForeignKeys;
        Assert.HasCount(2, fks);
        Assert.AreEqual("NameBase", fks[0].ReferencedTable);
        Assert.AreEqual("Groups", fks[1].ReferencedTable);
    }

    [TestMethod]
    public void A_self_referencing_many_to_many_edge_table_still_counts()
    {
        // e.g. UserFollows(FollowerId, FollowingId): both FKs point at the same parent table (User), but
        // it's still a many-to-many association, not a plain composite-key row.
        var follows = Sample.Table("UserFollows",
            [Sample.Column("FollowerId", SqlDbType.Int, primaryKey: true), Sample.Column("FollowingId", SqlDbType.Int, primaryKey: true)],
            foreignKeys: [Sample.ForeignKey("FollowerId", "User", "UserId"), Sample.ForeignKey("FollowingId", "User", "UserId")]);

        Assert.IsTrue(follows.IsJunctionTable);
    }

    [TestMethod]
    public void Extra_audit_columns_dont_disqualify_it_but_a_non_audit_extra_column_does()
    {
        // JunctionWithSurrogateKey already carries CreateDate/CreateUser and is still recognized (see
        // A_surrogate_key_junction_table_is_recognized). A genuinely unclassified extra column, though,
        // means there are three real "structural" columns, not two -- no longer a pure association table.
        var withUnclassifiedExtra = Sample.Table("UserRole",
            [
                Sample.Column("UserId", SqlDbType.Int, primaryKey: true),
                Sample.Column("RoleId", SqlDbType.Int, primaryKey: true),
                Sample.Column("Comment", SqlDbType.NVarChar, nullable: true, characters: 200)
            ],
            foreignKeys: [Sample.ForeignKey("UserId", "User", "UserId"), Sample.ForeignKey("RoleId", "Role", "RoleId")]);

        Assert.IsFalse(withUnclassifiedExtra.IsJunctionTable);
    }

    [TestMethod]
    public void A_composite_key_with_no_foreign_keys_at_all_is_not_a_junction_table()
    {
        Assert.IsFalse(Sample.CompositeKey().IsJunctionTable);
        Assert.IsEmpty(Sample.CompositeKey().JunctionForeignKeys);
    }

    [TestMethod]
    public void A_two_column_key_where_only_one_side_is_a_foreign_key_is_not_a_junction_table()
    {
        var table = Sample.Table("Junction",
            [Sample.Column("LeftId", SqlDbType.Int, primaryKey: true), Sample.Column("RightId", SqlDbType.Int, primaryKey: true)],
            foreignKeys: [Sample.ForeignKey("LeftId", "Left", "LeftId")]);

        Assert.IsFalse(table.IsJunctionTable);
    }

    [TestMethod]
    public void A_single_composite_fk_spanning_both_key_columns_is_not_a_junction_table()
    {
        // The key duplicates a parent's own 2-column composite key -- a different relationship entirely
        // from a many-to-many association, even though the PK shape (two columns) looks superficially similar.
        var table = Sample.Table("Junction",
            [Sample.Column("LeftId", SqlDbType.Int, primaryKey: true), Sample.Column("RightId", SqlDbType.Int, primaryKey: true)],
            foreignKeys:
            [
                new ForeignKeyModel
                {
                    ConstraintName = "FK_Junction_Parent",
                    ReferencingColumns = ["LeftId", "RightId"],
                    ReferencedSchema = "dbo",
                    ReferencedTable = "Parent",
                    ReferencedColumns = ["LeftId", "RightId"]
                }
            ]);

        Assert.IsFalse(table.IsJunctionTable);
    }

    [TestMethod]
    public void A_single_column_primary_key_is_never_a_junction_table()
    {
        Assert.IsFalse(Sample.DonateLeave().IsJunctionTable);
    }

    [TestMethod]
    public void A_three_column_key_is_not_a_junction_table_even_if_every_column_is_an_fk()
    {
        var table = Sample.Table("Triple",
            [
                Sample.Column("AId", SqlDbType.Int, primaryKey: true),
                Sample.Column("BId", SqlDbType.Int, primaryKey: true),
                Sample.Column("CId", SqlDbType.Int, primaryKey: true)
            ],
            foreignKeys:
            [
                Sample.ForeignKey("AId", "A", "AId"),
                Sample.ForeignKey("BId", "B", "BId"),
                Sample.ForeignKey("CId", "C", "CId")
            ]);

        Assert.IsFalse(table.IsJunctionTable);
    }
}

[TestClass]
public class TableModelPrimaryKeyShapeTests
{
    [TestMethod]
    public void No_primary_key_is_none()
    {
        var table = Sample.Table("Thing", [Sample.Column("Label", SqlDbType.NVarChar)]);

        Assert.AreEqual(PrimaryKeyShape.None, table.PrimaryKeyShape);
    }

    [TestMethod]
    public void Two_primary_key_columns_is_composite_regardless_of_type()
    {
        var table = Sample.CompositeKey();

        Assert.AreEqual(PrimaryKeyShape.Composite, table.PrimaryKeyShape);
    }

    [TestMethod]
    [DataRow(SqlDbType.Int)]
    [DataRow(SqlDbType.BigInt)]
    [DataRow(SqlDbType.SmallInt)]
    [DataRow(SqlDbType.TinyInt)]
    public void A_single_integer_family_column_is_single_int(SqlDbType type)
    {
        var table = Sample.Table("Thing", [Sample.Column("ThingId", type, primaryKey: true)]);

        Assert.AreEqual(PrimaryKeyShape.SingleInt, table.PrimaryKeyShape);
    }

    [TestMethod]
    public void A_single_uniqueidentifier_column_is_single_uniqueidentifier()
    {
        var table = Sample.AccountRef();

        Assert.AreEqual(PrimaryKeyShape.SingleUniqueIdentifier, table.PrimaryKeyShape);
    }

    [TestMethod]
    public void A_single_natural_text_key_is_single_other()
    {
        var table = Sample.NaturalKey();

        Assert.AreEqual(PrimaryKeyShape.SingleOther, table.PrimaryKeyShape);
    }
}

[TestClass]
public class TableModelIsNameActiveTableTests
{
    [TestMethod]
    public void A_not_null_Name_plus_a_not_null_IsActive_bit_is_name_active()
    {
        Assert.IsTrue(Sample.DepartmentTeam().IsNameActiveTable);
    }

    [TestMethod]
    public void A_table_with_no_Name_or_IsActive_column_is_not_name_active()
    {
        Assert.IsFalse(Sample.DonateLeave().IsNameActiveTable);
    }

    [TestMethod]
    public void A_nullable_Name_or_IsActive_column_does_not_count()
    {
        var nullableName = Sample.Table("Thing",
        [
            Sample.Column("ThingId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, nullable: true, characters: 50, ordinal: 2),
            Sample.Column("IsActive", SqlDbType.Bit, ordinal: 3)
        ]);
        var nullableIsActive = Sample.Table("Thing",
        [
            Sample.Column("ThingId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
            Sample.Column("IsActive", SqlDbType.Bit, nullable: true, ordinal: 3)
        ]);

        Assert.IsFalse(nullableName.IsNameActiveTable);
        Assert.IsFalse(nullableIsActive.IsNameActiveTable);
    }

    [TestMethod]
    public void Only_a_Name_or_only_an_IsActive_column_alone_does_not_count()
    {
        var nameOnly = Sample.Table("Thing",
        [
            Sample.Column("ThingId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2)
        ]);
        var activeOnly = Sample.Table("Thing",
        [
            Sample.Column("ThingId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("IsActive", SqlDbType.Bit, ordinal: 2)
        ]);

        Assert.IsFalse(nameOnly.IsNameActiveTable);
        Assert.IsFalse(activeOnly.IsNameActiveTable);
    }
}
