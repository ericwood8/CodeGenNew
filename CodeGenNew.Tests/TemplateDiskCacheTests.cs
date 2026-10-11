using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> Compiled templates kept on disk: a second run reads them back instead of calling the compiler. </summary>
[TestClass]
public class TemplateDiskCacheTests
{
    private const string Header = "<#@ template language=\"C#\" #>\n<#@ parameter name=\"Project\" type=\"CodeGenNew.Core.ProjectSettings\" #>\n";

    private static ProjectSettings Project() => ProjectSettings.FromValues([new("ProjectName", "Acme")]);

    [TestMethod]
    public async Task A_second_cache_reads_the_compiled_template_back_and_an_edited_template_is_compiled_again()
    {
        using var temp = new TempFolder();
        string template = temp.File("Hello_v1.tt", Header + "Hello <#= Project.ProjectName #>");
        string store = System.IO.Path.Combine(temp.Path, "store");

        using var first = TemplateCache.InFolder(store);
        var written = await first.RunAsync(template, Project());
        using var second = TemplateCache.InFolder(store);
        var read = await second.RunAsync(template, Project());

        Assert.AreEqual(1, first.CompilerRuns);
        Assert.AreEqual(0, second.CompilerRuns, "the second cache found the compiled template on disk");
        Assert.AreEqual("Hello Acme", written.GeneratedText);
        Assert.AreEqual(written.GeneratedText, read.GeneratedText);

        temp.File("Hello_v1.tt", Header + "two");
        using var third = TemplateCache.InFolder(store);

        Assert.AreEqual("two", (await third.RunAsync(template, Project())).GeneratedText);
        Assert.AreEqual(1, third.CompilerRuns);
    }

    [TestMethod]
    public async Task A_template_that_does_not_compile_is_not_kept()
    {
        using var temp = new TempFolder();
        string store = System.IO.Path.Combine(temp.Path, "store");
        string template = temp.File("Broken_v1.tt", Header + "<# NoSuchMethod(); #>");

        using var first = TemplateCache.InFolder(store);
        Assert.IsFalse((await first.RunAsync(template, Project())).Success);
        using var second = TemplateCache.InFolder(store);
        Assert.IsFalse((await second.RunAsync(template, Project())).Success);

        Assert.AreEqual(1, second.CompilerRuns, "a failed compile is tried again");
        Assert.IsFalse(Directory.Exists(store) && Directory.EnumerateFiles(store).Any());
    }

    [TestMethod]
    public async Task A_cache_without_disk_compiles_every_time()
    {
        using var temp = new TempFolder();
        string template = temp.File("Hello_v1.tt", Header + "x");
        using var first = TemplateCache.WithoutDisk();
        using var second = TemplateCache.WithoutDisk();
        await first.RunAsync(template, Project());
        await second.RunAsync(template, Project());

        Assert.AreEqual(1, second.CompilerRuns);
    }
}
