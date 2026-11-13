using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> DefaultRowTables: one row of each group is the default. </summary>
[TestClass]
public class DefaultRowTests
{
    private static TableModel Address(bool nullableFlag = false, SqlDbType flagType = SqlDbType.Bit) => Sample.Table("CustomerAddress",
    [
        Sample.Column("CustomerAddressId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2),
        Sample.Column("AddressType", SqlDbType.VarChar, characters: 10, ordinal: 3),
        Sample.Column("Line1", SqlDbType.VarChar, characters: 200, ordinal: 4),
        Sample.Column("IsDefault", flagType, nullable: nullableFlag, ordinal: 5),
    ], []);

    private static DatabaseModel Database(TableModel? address = null) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [address ?? Address()] };

    private static ProjectSettings Project(string setting = "CustomerAddress:IsDefault:CustomerId+AddressType")
    {
        var values = new Dictionary<string, string> { ["ProjectName"] = "Shop", ["DefaultRowTables"] = setting, ["AccessMode"] = "Ef" };
        return ProjectSettings.FromValues(values);
    }

    [TestMethod]
    public void The_setting_names_the_flag_and_the_group_columns()
    {
        var shape = DefaultRowPlan.For(Address(), Project())!;
        Assert.AreEqual("IsDefault", shape.Flag.Name);
        CollectionAssert.AreEqual(new[] { "CustomerId", "AddressType" }, shape.Groups.Select(g => g.Name).ToArray());
        Assert.HasCount(0, RowShapePlan.Problems(Database(), Project()));
        Assert.IsTrue(RowShapePlan.Has(Address(), Project()));
    }

    [TestMethod]
    [DataRow("CustomerAddress", "yes/no column and the group columns")]
    [DataRow("CustomerAddress:IsDefault", "yes/no column and the group columns")]
    [DataRow("CustomerAddress:Line1:CustomerId", "yes/no column")]
    [DataRow("CustomerAddress:IsDefault:Nothing", "not a column")]
    [DataRow("CustomerAddress:IsDefault:IsDefault", "cannot be made of the key or of")]
    [DataRow("CustomerAddress:IsDefault:CustomerAddressId", "cannot be made of the key or of")]
    public void A_setting_that_cannot_work_is_reported(string setting, string expected)
    {
        var problems = RowShapePlan.Problems(Database(), Project(setting));
        Assert.IsTrue(problems.Any(p => p.Contains(expected)), string.Join(" | ", problems));
    }

    [TestMethod]
    public void A_flag_that_allows_null_is_reported()
    {
        var problems = RowShapePlan.Problems(Database(Address(nullableFlag: true)), Project());
        Assert.IsTrue(problems.Any(p => p.Contains("cannot be NULL")), string.Join(" | ", problems));
    }

    [TestMethod]
    public void The_api_checks_the_default_on_save_and_on_delete_and_maps_make_default()
    {
        string code = RowShapeWriter.File(Database(), Project(), "ShopContext", []);
        Expect.Contains(code, "public static class DefaultRows");
        Expect.Contains(code, "CheckDefaultAsync(ShopContext context, CustomerAddress row)");
        Expect.Contains(code, "others = others.Where(x => x.CustomerId == group0);");
        Expect.Contains(code, "others = others.Where(x => x.AddressType == group1);");
        Expect.Contains(code, "DefaultRows.InSaveTransactionAsync(context");
        Expect.Contains(code, "CheckDefaultDeleteAsync(ShopContext context, int id)");
        Expect.Contains(code, "/make-default\", (ShopContext context, int id) => MakeDefaultAsync(context, id)");
        Assert.IsTrue(RowShapeWriter.HasSaveCheck(Address(), Project()));
        Assert.IsTrue(RowShapeWriter.HasDeleteCheck(Address(), Project()));
    }

    [TestMethod]
    public void The_mark_is_handed_on_before_a_soft_delete_flags_the_row()
    {
        var address = Sample.Table("CustomerAddress",
        [
            Sample.Column("CustomerAddressId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("CustomerId", SqlDbType.Int, ordinal: 2),
            Sample.Column("AddressType", SqlDbType.VarChar, characters: 10, ordinal: 3),
            Sample.Column("IsDefault", SqlDbType.Bit, ordinal: 4),
            Sample.Column("IsDeleted", SqlDbType.Bit, ordinal: 5),
        ], []);
        var values = new Dictionary<string, string> { ["ProjectName"] = "Shop", ["DefaultRowTables"] = "CustomerAddress:IsDefault:CustomerId+AddressType", ["SoftDeleteTables"] = "CustomerAddress", ["AccessMode"] = "Ef" };
        string code = RowShapeWriter.File(Database(address), ProjectSettings.FromValues(values), "ShopContext", []);
        int handOn = code.IndexOf("CheckDefaultDeleteAsync(context", StringComparison.Ordinal);
        int soft = code.IndexOf("SoftDeleteAsync(context", StringComparison.Ordinal);
        Assert.IsTrue(handOn >= 0 && soft >= 0, "both calls are written");
        Assert.IsLessThan(soft, handOn, "the mark is handed on before the row is flagged");
    }

    [TestMethod]
    public void A_copy_of_the_default_row_would_be_a_second_default_so_the_table_has_no_clone()
    {
        Assert.IsTrue(CloneShape.CanClone(Address(), ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop" })));
        Assert.IsFalse(CloneShape.CanClone(Address(), Project()));
        Assert.Contains("second default", CloneShape.WhyNot(Address(), Project())!);
    }

    [TestMethod]
    public void The_generated_tests_play_the_rule()
    {
        var project = Project();
        var plan = ApiTestPlan.For(Address(), project)!;
        string tests = RowShapeTests.Methods(plan, Address(), project);
        Expect.Contains(tests, "A_row_saved_with_the_default_mark_takes_it_from_the_others_of_its_group");
        Expect.Contains(tests, "Make_default_moves_the_mark_to_the_row");
        Expect.Contains(tests, "b[\"customerId\"] = first[\"customerId\"]?.DeepClone();");
    }
}
