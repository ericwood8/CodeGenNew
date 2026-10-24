using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> A column named like a C# reserved word (AdventureWorks has Product.Class) must still give code that compiles. </summary>
[TestClass]
public class KeywordColumnTests
{
    [TestMethod]
    public void A_reserved_word_gets_the_at_sign_and_other_names_do_not()
    {
        Assert.AreEqual("@class", TemplateHelpers.CSharpIdentifier("class"));
        Assert.AreEqual("@event", TemplateHelpers.CSharpIdentifier("event"));
        Assert.AreEqual("style", TemplateHelpers.CSharpIdentifier("style"));
        Assert.AreEqual("Class", TemplateHelpers.CSharpIdentifier("Class"));
    }

    [TestMethod]
    public async Task The_repository_search_takes_a_column_called_Class_as_a_parameter()
    {
        var table = Sample.Table("Product",
        [
            Sample.Column("ProductId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
            Sample.Column("Class", SqlDbType.NChar, nullable: true, characters: 2, ordinal: 3)
        ]);
        var result = await Repo.Cache.RunAsync(Repo.Template("CS_Repo_v1.tt"), table, ProjectSettings.FromValues([new("ProjectName", "Acme")]));

        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.Contains(result.GeneratedText!, "string? @class = null");
        Expect.DoesNotContain(result.GeneratedText!, "string? class =");
    }
}
