using CodeGenNew.Core;

namespace CodeGenNew.Tests;

[TestClass]
public class FeatureCoverageTests
{
    private static ProjectSettings Project(params (string, string)[] values) =>
        ProjectSettings.FromValues(values.ToDictionary(v => v.Item1, v => v.Item2));

    [TestMethod]
    [DataRow("PY_TestSubscriptions", "Python", "Subscription", true)]
    [DataRow("RS_Subscriptions", "Rust", "Subscription", false)]
    [DataRow("TSX_Approvals", "React", "Approval", false)]
    [DataRow("TS_People", "Angular", "People", false)]
    [DataRow("WinUI3_TestSubscriptions", "WinUI3", "Subscription", true)]
    [DataRow("API_Queue", "API", "Queue", false)]
    [DataRow("RS_Test", "Rust", "Crud", true)]
    [DataRow("RS_TestBodies", "Rust", "Crud", true)]
    [DataRow("BLZ_PeopleTest", "Blazor", "People", true)]
    [DataRow("WinUI3_DashboardPage", "WinUI3", "Dashboard", false)]
    public void A_template_name_gives_its_stack_and_feature(string template, string stack, string feature, bool isTest)
    {
        Assert.AreEqual((stack, feature, isTest), FeatureCoverage.Classify(template));
    }

    [TestMethod]
    public void A_template_no_stack_owns_is_left_out()
    {
        Assert.IsNull(FeatureCoverage.Classify("SP_LogIndex"));
        Assert.IsNull(FeatureCoverage.Classify("CS_Faker"));
    }

    [TestMethod]
    public void A_queue_is_served_by_the_API_Python_and_Rust_and_not_by_the_front_ends()
    {
        var row = FeatureCoverage.Rows(Project(("QueueTables", "Job:Status"))).Single(r => r.Feature == "Queue");

        Assert.AreEqual("S", row.Cells["API"]);
        Assert.AreEqual("S", row.Cells["Python"]);
        Assert.AreEqual("S", row.Cells["Rust"]);
        Assert.AreEqual(".", row.Cells["React"]);
        StringAssert.EndsWith(FeatureCoverage.Text(Project(("QueueTables", "Job:Status"))).Split('\n').Single(l => l.StartsWith("Queue")), "<");
    }

    [TestMethod]
    public void A_project_with_no_feature_says_so()
    {
        Expect.Contains(FeatureCoverage.Text(Project(("ApiAuth", "false"), ("Dashboard", "false"))), "names no feature");
    }
}
