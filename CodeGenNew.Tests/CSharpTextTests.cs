using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The string literals and initializers every C# template writes. </summary>
[TestClass]
public class CSharpTextTests
{
    [TestMethod]
    public void A_quote_and_a_backslash_are_escaped_and_the_literal_is_wrapped_in_quotes()
    {
        Assert.AreEqual(@"say \""hi\"" in C:\\temp", CSharpText.Escape(@"say ""hi"" in C:\temp"));
        Assert.AreEqual(@"""a\""b""", CSharpText.Quote(@"a""b"));
    }

    [TestMethod]
    public void A_not_null_string_or_byte_array_is_initialized_and_nothing_else_is()
    {
        Assert.AreEqual(" = \"\";", CSharpText.NotNullInitializer(Sample.Column("Title", SqlDbType.NVarChar, characters: 40)));
        Assert.AreEqual(" = [];", CSharpText.NotNullInitializer(Sample.Column("Photo", SqlDbType.VarBinary)));
        Assert.AreEqual("", CSharpText.NotNullInitializer(Sample.Column("Title", SqlDbType.NVarChar, characters: 40, nullable: true)));
        Assert.AreEqual("", CSharpText.NotNullInitializer(Sample.Column("Total", SqlDbType.Int)));
    }
}
