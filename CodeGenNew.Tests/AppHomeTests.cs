using CodeGenNew.Core;

namespace CodeGenNew.Tests;

[TestClass]
public class AppHomeTests
{
    private const string Data = @"C:\Users\someone\AppData\Roaming";

    [TestMethod]
    [DataRow(@"C:\Users\someone\.dotnet\tools\.store\codegennew.cli\0.1.0\codegennew.cli\0.1.0\tools\net10.0\any\")]
    [DataRow(@"D:\tools\.store\codegennew.cli\0.1.0\codegennew.cli\0.1.0\tools\net10.0\any\")]
    [DataRow("/home/someone/.dotnet/tools/.store/codegennew.cli/0.1.0/codegennew.cli/0.1.0/tools/net10.0/any/")]
    public void An_installed_tool_uses_the_users_own_folder(string baseDirectory)
    {
        Assert.IsTrue(AppHome.IsInstalledTool(baseDirectory));
        Assert.AreEqual(Path.Combine(Data, "CodeGenNew"), AppHome.Resolve(baseDirectory, null, Data));
    }

    [TestMethod]
    public void A_build_or_a_plain_copy_keeps_its_files_beside_the_program()
    {
        const string build = @"C:\Github\CodeGenNew\CodeGenNew.Cli\bin\Debug\net10.0\";

        Assert.IsFalse(AppHome.IsInstalledTool(build));
        Assert.AreEqual(build, AppHome.Resolve(build, null, Data));
    }

    [TestMethod]
    public void The_environment_variable_wins_over_both()
    {
        string home = Path.Combine(Path.GetTempPath(), "codegen-home");

        Assert.AreEqual(home, AppHome.Resolve(@"C:\x\.store\y\", home, Data));
        Assert.AreEqual(home, AppHome.Resolve(@"C:\build\", home, Data));
        Assert.AreEqual(@"C:\build\", AppHome.Resolve(@"C:\build\", "  ", Data), "a blank value is ignored");
    }
}
