using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The parts of a <c>Table:Part:Part</c> list setting. </summary>
[TestClass]
public class SettingPartsTests
{
    [TestMethod]
    public void An_item_is_split_at_the_colons_and_trimmed()
    {
        var items = SettingParts.Items(["Job : Status : Tries", "Mail"]);
        CollectionAssert.AreEqual(new[] { "Job", "Status", "Tries" }, items[0]);
        CollectionAssert.AreEqual(new[] { "Mail" }, items[1]);
    }

    [TestMethod]
    public void An_item_with_no_table_is_dropped()
    {
        Assert.AreEqual(1, SettingParts.Items(["", ":Status", "Job"]).Count(p => p[0] == "Job"));
        Assert.AreEqual(1, SettingParts.Items(["", ":Status", "Job"]).Count);
    }

    [TestMethod]
    public void A_part_that_is_missing_or_empty_is_null()
    {
        string[] parts = ["Job", "", "Tries"];
        Assert.IsNull(SettingParts.At(parts, 1));
        Assert.AreEqual("Tries", SettingParts.At(parts, 2));
        Assert.IsNull(SettingParts.At(parts, 3));
    }
}
