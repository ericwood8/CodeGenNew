using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A content table that is also in RowApprovals: publishing asks for a second person, the approval publishes. </summary>
[TestClass]
public class ContentApprovalTests
{
    private static TableModel Post() => Sample.Table("Post",
    [
        Sample.Column("PostId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 150, ordinal: 2),
        Sample.Column("Slug", SqlDbType.NVarChar, characters: 150, ordinal: 3),
        Sample.Column("Status", SqlDbType.NVarChar, characters: 12, ordinal: 4),
        Sample.Column("PublishedOn", SqlDbType.DateTime, nullable: true, ordinal: 5),
    ], []);

    private static ProjectSettings Project(bool approvals = true) => ProjectSettings.FromValues(new Dictionary<string, string>
    {
        ["ProjectName"] = "Shop", ["ContentTables"] = "Post", ["AccessMode"] = "Ef", ["ApiAuth"] = "true", ["RowApprovals"] = approvals ? "Post" : ""
    });

    [TestMethod]
    public void A_content_table_in_RowApprovals_needs_approval_and_another_one_does_not()
    {
        Assert.IsTrue(ContentPlan.For(Post(), Project())!.NeedsApproval);
        Assert.IsFalse(ContentPlan.For(Post(), Project(approvals: false))!.NeedsApproval);
        CollectionAssert.AreEqual(new[] { "Post" }, ContentPlan.ApprovalTables(Project()).ToArray());
        Assert.AreEqual(0, ContentPlan.ApprovalTables(Project(approvals: false)).Count);
    }

    [TestMethod]
    public void The_publish_route_asks_for_approval_and_the_other_routes_are_unchanged()
    {
        string text = RowShapeWriter.File(new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Post()] }, Project(), "ShopContext", []);
        Assert.Contains("PublishAsync(context, caller, id)", text);
        Assert.Contains("PostApprovals.RequestAsync(context, caller, id, null)", text);
        Assert.Contains("MarkPendingAsync", text);
        Assert.Contains("PublishPendingAsync", text);
        Assert.Contains("LeaveAsync(context, caller, id, \"archived\")", text);
        string plain = RowShapeWriter.File(new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [Post()] }, Project(approvals: false), "ShopContext", []);
        Assert.Contains("SetStatusAsync(context, id, \"published\")", plain);
        Assert.DoesNotContain("MarkPendingAsync", plain);
    }

    [TestMethod]
    public void A_pending_row_is_not_offered_Publish_again()
    {
        var content = ContentPlan.For(Post(), Project())!;
        Assert.AreEqual("row.status !== 'published' && row.status !== 'pending'", ContentScreens.ShownTs(content, ContentScreens.Publish, "row"));
        Assert.AreEqual("row.status === 'published'", ContentScreens.ShownTs(content, ContentScreens.Unpublish, "row"));
        Assert.AreEqual("e.Status != \"published\" && e.Status != \"pending\"", ContentScreens.ShownCs(ContentScreens.Publish, "e", "Status", content));
        Assert.AreEqual("e.Status != \"published\"", ContentScreens.ShownCs(ContentScreens.Publish, "e", "Status", ContentPlan.For(Post(), Project(approvals: false))));
    }

    [TestMethod]
    public void A_desktop_app_that_reads_the_database_asks_through_the_approval_rules()
    {
        var content = ContentPlan.For(Post(), Project())!;
        string publish = string.Join("\n", ContentScreens.ApprovalLines(content, ContentScreens.Publish, "id", "_context", "return;"));
        Assert.Contains("PostApprovals.RequestAsync(_context, id, null)", publish);
        Assert.IsTrue(ContentScreens.AsksForApproval(content, ContentScreens.Publish));
        Assert.IsFalse(ContentScreens.AsksForApproval(content, ContentScreens.Archive));
        Assert.Contains("WithdrawWaitingAsync(_context, id)", string.Join("\n", ContentScreens.ApprovalLines(content, ContentScreens.Archive, "id", "_context", "return;")));
    }
}
