using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A table that looks like a shape of item 143 is suggested, never applied: the suggestion fits when the shape's own planner accepts the table listed. </summary>
[TestClass]
public class TableShapeDetectorTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" }.Concat(values.Select(v => KeyValuePair.Create(v.Key, v.Value))).ToDictionary());

    private static ColumnModel Key(string name = "Id") => Sample.Column(name, SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1);

    private static ShapeSuggestion? Find(TableModel table, string shape, params (string, string)[] project) =>
        TableShapeDetector.Of(table, Project(project)).FirstOrDefault(s => s.Shape == shape);

    [TestMethod]
    public void A_table_with_one_nullable_key_to_itself_is_a_tree()
    {
        var table = Sample.Table("Category", [Key("CategoryId"), Sample.Column("Name", SqlDbType.VarChar, characters: 40, ordinal: 2), Sample.Column("ParentId", SqlDbType.Int, nullable: true, ordinal: 3)],
            [Sample.ForeignKey("ParentId", "Category", "CategoryId")]);
        var tree = Find(table, "Tree")!;
        Assert.IsTrue(tree.Fits);
        Assert.AreEqual("TreeTables", tree.Setting);
        Assert.AreEqual("Category", tree.Item);
    }

    [TestMethod]
    public void Two_keys_to_itself_are_ambiguous_and_name_each_column()
    {
        var table = Sample.Table("Person", [Key("PersonId"), Sample.Column("ManagerId", SqlDbType.Int, nullable: true, ordinal: 2), Sample.Column("MentorId", SqlDbType.Int, nullable: true, ordinal: 3)],
            [Sample.ForeignKey("ManagerId", "Person", "PersonId"), Sample.ForeignKey("MentorId", "Person", "PersonId")]);
        var tree = Find(table, "Tree")!;
        Assert.IsFalse(tree.Fits);
        StringAssert.Contains(tree.Note, "ManagerId, MentorId");
    }

    [TestMethod]
    public void A_required_key_to_itself_is_not_a_tree()
    {
        var table = Sample.Table("Category", [Key("CategoryId"), Sample.Column("ParentId", SqlDbType.Int, ordinal: 2)], [Sample.ForeignKey("ParentId", "Category", "CategoryId")]);
        Assert.IsNull(Find(table, "Tree"));
    }

    [TestMethod]
    public void A_table_the_project_already_lists_is_not_suggested_again()
    {
        var table = Sample.Table("Category", [Key("CategoryId"), Sample.Column("ParentId", SqlDbType.Int, nullable: true, ordinal: 2)], [Sample.ForeignKey("ParentId", "Category", "CategoryId")]);
        Assert.IsNull(Find(table, "Tree", ("TreeTables", "Category")));
    }

    [TestMethod]
    [DataRow("IsDeleted", "Customer")]
    [DataRow("IsRemoved", "Customer:IsRemoved")]
    [DataRow("Voided", "Customer:Voided")]
    public void A_required_deletion_flag_is_soft_delete_under_its_own_name(string flag, string item)
    {
        var table = Sample.Table("Customer", [Key("CustomerId"), Sample.Column("Name", SqlDbType.VarChar, characters: 40, ordinal: 2), Sample.Column(flag, SqlDbType.Bit, ordinal: 3)]);
        var soft = Find(table, "Soft delete")!;
        Assert.IsTrue(soft.Fits);
        Assert.AreEqual(item, soft.Item);
    }

    [TestMethod]
    public void A_nullable_deleted_stamp_without_a_flag_only_looks_like_soft_delete()
    {
        var table = Sample.Table("Customer", [Key("CustomerId"), Sample.Column("DeletedOn", SqlDbType.DateTime, nullable: true, ordinal: 2)]);
        var soft = Find(table, "Soft delete")!;
        Assert.IsFalse(soft.Fits);
        StringAssert.Contains(soft.Note, "yes/no column");
    }

    [TestMethod]
    public void A_nullable_flag_is_short_of_soft_delete_and_the_note_says_why()
    {
        var table = Sample.Table("Customer", [Key("CustomerId"), Sample.Column("IsDeleted", SqlDbType.Bit, nullable: true, ordinal: 2)]);
        var soft = Find(table, "Soft delete")!;
        Assert.IsFalse(soft.Fits);
        StringAssert.Contains(soft.Note, "cannot be NULL");
    }

    [TestMethod]
    public void A_log_table_is_never_soft_deleted()
    {
        var table = Sample.Table("ErrorLog", [Key("ErrorLogId"), Sample.Column("IsDeleted", SqlDbType.Bit, ordinal: 2), Sample.Column("LoggedOn", SqlDbType.DateTime, ordinal: 3)]);
        Assert.IsNull(Find(table, "Soft delete", ("LogTables", "ErrorLog")));
    }

    [TestMethod]
    public void A_default_flag_beside_one_foreign_key_and_a_type_is_a_default_row()
    {
        var table = Sample.Table("CustomerAddress", [Key("CustomerAddressId"), Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2), Sample.Column("AddressType", SqlDbType.VarChar, characters: 10, ordinal: 3),
            Sample.Column("IsDefault", SqlDbType.Bit, ordinal: 4)], [Sample.ForeignKey("CustomerId", "Customer", "CustomerId")]);
        var row = Find(table, "Default row")!;
        Assert.IsTrue(row.Fits);
        Assert.AreEqual("CustomerAddress:IsDefault:CustomerId+AddressType", row.Item);
    }

    [TestMethod]
    public void A_default_flag_beside_two_foreign_keys_asks_for_the_group()
    {
        var table = Sample.Table("CustomerAddress", [Key("CustomerAddressId"), Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2), Sample.Column("CountryId", SqlDbType.Int, ordinal: 3),
            Sample.Column("IsPreferred", SqlDbType.Bit, ordinal: 4)], [Sample.ForeignKey("CustomerId", "Customer", "CustomerId"), Sample.ForeignKey("CountryId", "Country", "CountryId")]);
        var row = Find(table, "Default row")!;
        Assert.IsFalse(row.Fits);
        StringAssert.Contains(row.Note, "CustomerId and CountryId");
    }

    [TestMethod]
    public void A_default_flag_with_no_foreign_key_has_no_group_and_is_not_suggested()
    {
        var table = Sample.Table("Setting", [Key("SettingId"), Sample.Column("IsDefault", SqlDbType.Bit, ordinal: 2)]);
        Assert.IsNull(Find(table, "Default row"));
    }

    [TestMethod]
    public void A_period_under_one_parent_is_effective_dated_rows()
    {
        var table = Sample.Table("Price", [Key("PriceId"), Sample.Column("ItemId", SqlDbType.Int, ordinal: 2), Sample.Column("ValidFrom", SqlDbType.Date, ordinal: 3), Sample.Column("ValidTo", SqlDbType.Date, nullable: true, ordinal: 4)],
            [Sample.ForeignKey("ItemId", "Item", "ItemId")]);
        var period = Find(table, "Effective dated rows")!;
        Assert.IsTrue(period.Fits);
        Assert.AreEqual("Price:ValidFrom:ValidTo:ItemId", period.Item);
    }

    [TestMethod]
    public void A_check_constraint_that_orders_two_dates_finds_a_period_the_names_do_not_show()
    {
        var table = Sample.Table("Rate", [Key("RateId"), Sample.Column("ItemId", SqlDbType.Int, ordinal: 2), Sample.Column("Opens", SqlDbType.Date, ordinal: 3), Sample.Column("Closes", SqlDbType.Date, nullable: true, ordinal: 4)],
            [Sample.ForeignKey("ItemId", "Item", "ItemId")], checks: [new CheckConstraintModel("CK_Rate_Period", "CHECK ([Closes] IS NULL OR [Opens]<=[Closes])")]);
        var period = Find(table, "Effective dated rows")!;
        Assert.AreEqual("Rate:Opens:Closes:ItemId", period.Item);
        StringAssert.Contains(period.Text, "check constraint");
    }

    [TestMethod]
    public void A_period_with_no_check_constraint_does_not_mention_one()
    {
        var table = Sample.Table("Price", [Key("PriceId"), Sample.Column("ItemId", SqlDbType.Int, ordinal: 2), Sample.Column("ValidFrom", SqlDbType.Date, ordinal: 3), Sample.Column("ValidTo", SqlDbType.Date, nullable: true, ordinal: 4)],
            [Sample.ForeignKey("ItemId", "Item", "ItemId")]);
        Assert.IsFalse(Find(table, "Effective dated rows")!.Text.Contains("check constraint", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void A_unique_filtered_index_on_the_default_flag_is_mentioned()
    {
        var table = Sample.Table("CustomerAddress", [Key("CustomerAddressId"), Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2), Sample.Column("IsDefault", SqlDbType.Bit, ordinal: 3)],
            [Sample.ForeignKey("CustomerId", "Customer", "CustomerId")], partialIndexes: [new PartialIndexModel("UX_Default", true, ["CustomerId"], [], "([IsDefault]=(1))")]);
        StringAssert.Contains(Find(table, "Default row")!.Text, "unique filtered index");
    }

    [TestMethod]
    public void A_period_with_no_parent_is_not_suggested()
    {
        var table = Sample.Table("Event", [Key("EventId"), Sample.Column("StartDate", SqlDbType.Date, ordinal: 2), Sample.Column("EndDate", SqlDbType.Date, nullable: true, ordinal: 3)]);
        Assert.IsNull(Find(table, "Effective dated rows"));
    }

    [TestMethod]
    public void A_period_of_two_date_types_is_short_of_it()
    {
        var table = Sample.Table("Price", [Key("PriceId"), Sample.Column("ItemId", SqlDbType.Int, ordinal: 2), Sample.Column("ValidFrom", SqlDbType.Date, ordinal: 3), Sample.Column("ValidTo", SqlDbType.DateTime, nullable: true, ordinal: 4)],
            [Sample.ForeignKey("ItemId", "Item", "ItemId")]);
        Assert.IsFalse(Find(table, "Effective dated rows")!.Fits);
    }

    private static TableModel Queue(string status = "Status", string retry = "RetryCount", string claim = "ClaimedOn") => Sample.Table("Mail",
    [
        Key("MailId"), Sample.Column(status, SqlDbType.VarChar, characters: 20, ordinal: 2), Sample.Column(retry, SqlDbType.Int, ordinal: 3), Sample.Column("CreatedOn", SqlDbType.DateTime, ordinal: 4),
        Sample.Column(claim, SqlDbType.DateTime, nullable: true, ordinal: 5), Sample.Column("ProcessedOn", SqlDbType.DateTime, nullable: true, ordinal: 6)
    ]);

    [TestMethod]
    public void A_table_with_status_retries_a_claim_and_an_end_stamp_is_a_work_queue()
    {
        var queue = Find(Queue(), "Work queue")!;
        Assert.IsTrue(queue.Fits);
        Assert.AreEqual("Mail", queue.Item);
    }

    [TestMethod]
    public void A_queue_under_other_names_carries_them_in_the_item()
    {
        var queue = Find(Queue("State", "Attempts"), "Work queue")!;
        Assert.IsTrue(queue.Fits);
        Assert.AreEqual("Mail:State:Attempts", queue.Item);
    }

    [TestMethod]
    public void A_file_kept_in_a_binary_column_is_named_as_the_opposite_of_stored_file_metadata()
    {
        var table = Sample.Table("Document", [Key("DocumentId"), Sample.Column("FileName", SqlDbType.VarChar, characters: 100, ordinal: 2), Sample.Column("ContentType", SqlDbType.VarChar, characters: 50, ordinal: 3),
            Sample.Column("FileSize", SqlDbType.Int, ordinal: 4), Sample.Column("Content", SqlDbType.VarBinary, ordinal: 5)]);
        var asset = Find(table, "Stored file metadata")!;
        Assert.IsFalse(asset.Fits);
        StringAssert.Contains(asset.Note, "Content");
        StringAssert.Contains(asset.Note, "outside the database");
    }

    [TestMethod]
    [DataRow("LockedOn")]
    [DataRow("StartedAt")]
    public void A_queue_with_a_locked_or_started_stamp_fits(string claim) => Assert.IsTrue(Find(Queue(claim: claim), "Work queue")!.Fits);

    [TestMethod]
    public void A_table_without_a_retry_count_is_not_a_queue()
    {
        var table = Sample.Table("Mail", [Key("MailId"), Sample.Column("Status", SqlDbType.VarChar, characters: 20, ordinal: 2), Sample.Column("ClaimedOn", SqlDbType.DateTime, nullable: true, ordinal: 3),
            Sample.Column("ProcessedOn", SqlDbType.DateTime, nullable: true, ordinal: 4)]);
        Assert.IsNull(Find(table, "Work queue"));
    }

    [TestMethod]
    public void A_key_that_is_also_a_foreign_key_is_an_extension_that_needs_no_setting()
    {
        var table = Sample.Table("CustomerProfile", [Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("Notes", SqlDbType.VarChar, characters: 100, ordinal: 2)],
            [Sample.ForeignKey("CustomerId", "Customer", "CustomerId")]);
        var extension = Find(table, "One-to-one extension")!;
        Assert.IsTrue(extension.Fits);
        Assert.IsFalse(extension.CanBeApplied);
    }

    [TestMethod]
    public void Adding_a_suggestion_to_a_project_file_keeps_every_other_line()
    {
        string text = "# my project\r\nProjectName=Shop\r\n\r\nTreeTables=Category\r\nAccessMode=Ef\r\n";
        string changed = ProjectFileEdit.AddListItem(text, "TreeTables", "Comment");
        Assert.AreEqual("# my project\r\nProjectName=Shop\r\n\r\nTreeTables=Category,Comment\r\nAccessMode=Ef\r\n", changed);
        Assert.AreEqual(changed, ProjectFileEdit.AddListItem(changed, "TreeTables", "comment"));
        Assert.AreEqual("# my project\r\nProjectName=Shop\r\nSoftDeleteTables=Customer\r\n", ProjectFileEdit.AddListItem("# my project\r\nProjectName=Shop\r\n\r\n", "SoftDeleteTables", "Customer"));
    }
}
