using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The one place text with <c>§</c> tokens is filled. </summary>
[TestClass]
public class TemplateTextTests
{
    [TestMethod]
    public void A_token_that_starts_another_is_not_cut()
    {
        Assert.AreEqual("a-b", TemplateText.Fill("§CL-§CLAIMED", ("§CL", "a"), ("§CLAIMED", "b")));
    }

    [TestMethod]
    public void Fill_may_leave_a_token_for_a_later_call()
    {
        Assert.AreEqual("a §B", TemplateText.Fill("§A §B", ("§A", "a")));
    }

    [TestMethod]
    public void FillAll_refuses_a_token_that_is_left()
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(() => TemplateText.FillAll("§A §B", ("§A", "a")));
        StringAssert.Contains(error.Message, "§B");
    }
}
