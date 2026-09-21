using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

[TestClass]
public class GeneratedFilesTests
{
    private static TemplateInfo Template(string name, string? outputName = null) => new()
    {
        FilePath = name + ".tt",
        Name = name,
        SubmenuGroup = name.Contains('_') ? name[..name.IndexOf('_')] : null,
        Config = new TemplateConfig { OutputName = outputName }
    };

    [TestMethod]
    public void Output_without_a_marker_is_one_ordinary_file()
    {
        Assert.IsFalse(GeneratedFiles.HasMarkers("CREATE PROCEDURE x\nAS\nSELECT 1\n"));
    }

    [TestMethod]
    public void Split_returns_each_file_with_its_own_text()
    {
        string text = "@@@FILE models/a.ts@@@\nexport interface A {}\n@@@FILE services/a.service.ts@@@\nline1\nline2\n";

        var files = GeneratedFiles.Split(text);

        Assert.HasCount(2, files);
        Assert.AreEqual("models/a.ts", files[0].RelativePath);
        Assert.AreEqual("export interface A {}\n", files[0].Content);
        Assert.AreEqual("services/a.service.ts", files[1].RelativePath);
        Assert.AreEqual("line1\nline2\n", files[1].Content);
    }

    [TestMethod]
    public void A_marker_followed_straight_by_another_marker_is_an_empty_file()
    {
        var files = GeneratedFiles.Split("@@@FILE c/x.css@@@\n@@@FILE c/x.html@@@\n<p></p>\n");

        Assert.AreEqual("", files[0].Content);
        Assert.AreEqual("<p></p>\n", files[1].Content);
    }

    [TestMethod]
    public void The_last_file_keeps_exactly_the_text_the_template_wrote()
    {
        // no newline at the end of the template's text: none is added
        Assert.AreEqual("last", GeneratedFiles.Split("@@@FILE a.txt@@@\nlast")[0].Content);
        // one newline at the end: exactly one comes out
        Assert.AreEqual("last\n", GeneratedFiles.Split("@@@FILE a.txt@@@\nlast\n")[0].Content);
    }

    [TestMethod]
    public void Windows_line_endings_around_markers_are_understood()
    {
        var files = GeneratedFiles.Split("@@@FILE a.txt@@@\r\nx\r\n@@@FILE b.txt@@@\r\ny\r\n");

        Assert.HasCount(2, files);
        Assert.AreEqual("a.txt", files[0].RelativePath);
        Assert.AreEqual("b.txt", files[1].RelativePath);
    }

    [TestMethod]
    [DataRow("../evil.txt")]
    [DataRow("a/../../evil.txt")]
    [DataRow("C:/Windows/evil.txt")]
    [DataRow("/rooted.txt")]
    [DataRow("\\\\server\\share\\x.txt")]
    public void An_unsafe_path_is_refused(string path)
    {
        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedFiles.Split($"@@@FILE {path}@@@\nx\n"));
    }

    [TestMethod]
    public void Text_before_the_first_marker_is_a_template_bug()
    {
        Assert.ThrowsExactly<InvalidDataException>(() => GeneratedFiles.Split("stray text\n@@@FILE a.txt@@@\nx\n"));
    }

    [TestMethod]
    public async Task WriteAsync_creates_the_sub_folders_and_every_file()
    {
        using var temp = new TempFolder();
        string text = "@@@FILE components/holiday/holiday.component.css@@@\n@@@FILE components/holiday/holiday.component.ts@@@\nexport class X {}\n";

        var written = await GeneratedFiles.WriteAsync(temp.Path, Template("TS_Component_v1"), "Holiday", text);

        Assert.HasCount(2, written);
        Assert.AreEqual("", File.ReadAllText(Path.Combine(temp.Path, "components", "holiday", "holiday.component.css")));
        Assert.AreEqual("export class X {}\n", File.ReadAllText(Path.Combine(temp.Path, "components", "holiday", "holiday.component.ts")));
    }

    [TestMethod]
    public async Task WriteAsync_without_markers_uses_the_ordinary_file_name()
    {
        using var temp = new TempFolder();

        var written = await GeneratedFiles.WriteAsync(temp.Path, Template("SP_Save"), "Holiday", "SELECT 1");

        Assert.HasCount(1, written);
        Assert.AreEqual(Path.Combine(temp.Path, "Holiday_Save.sql"), written[0]);
    }
}
