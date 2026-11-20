using CodeGenNew.Core;

namespace CodeGenNew.Tests;

[TestClass]
public sealed class DotNetTargetTests
{
    [TestMethod]
    [DataRow("Sqlite", "net11.0", "Microsoft.EntityFrameworkCore.Sqlite\" Version=\"11.0.0")]
    [DataRow("SqlServer", "net11.0", "Microsoft.EntityFrameworkCore.SqlServer\" Version=\"11.0.0")]
    [DataRow("MySql", "net11.0", "Microting.EntityFrameworkCore.MySql\" Version=\"11.0.0")]
    [DataRow("PostgreSql", "net10.0", "Npgsql.EntityFrameworkCore.PostgreSQL\" Version=\"10.0.0")]
    public async Task The_api_project_targets_net11_except_over_PostgreSQL(string dialect, string framework, string provider)
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["DatabaseProvider"] = dialect });
        var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(text, $"<TargetFramework>{framework}</TargetFramework>");
        Expect.Contains(text, provider);
        // the framework of .NET 11 carries the package, so a reference to it is only written for the .NET 10 output
        Assert.AreEqual(framework == "net10.0", text.Contains("System.Security.Cryptography.Pkcs"));
    }

    [TestMethod]
    [DataRow("Sqlite", "11.0")]
    [DataRow("PostgreSql", "10.0")]
    public async Task The_dockerfile_uses_the_images_of_the_target_framework(string dialect, string tag)
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["DatabaseProvider"] = dialect, ["ApiProduction"] = "true" });
        var result = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(text, $"FROM mcr.microsoft.com/dotnet/sdk:{tag} AS build");
        Expect.Contains(text, $"FROM mcr.microsoft.com/dotnet/aspnet:{tag}\n");
    }

    [TestMethod]
    public async Task The_uno_project_targets_net11_with_the_matching_sdk()
    {
        var project = ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop" });
        var result = await Repo.Cache.RunAsync(Repo.Template("UNO_EssentialProject_v1.tt"), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string text = result.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(text, "net11.0-desktop;net11.0-browserwasm;net11.0-windows10.0.26100");
        Expect.Contains(text, "\"version\": \"11.0.100\"");
    }
}
