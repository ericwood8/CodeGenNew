using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> An optional lookup drop-down must offer a blank entry so a chosen value can be cleared again; a required one must not. </summary>
[TestClass]
public class BlankLookupOptionTests
{
    private static TableModel Donation() => Sample.Table("E_DonateLeave",
    [
        Sample.Column("DonateLeaveId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("DonateFrom_EmployeeId", SqlDbType.Int, ordinal: 2),
        Sample.Column("DonateTo_EmployeeId", SqlDbType.Int, nullable: true, ordinal: 3)
    ],
    [
        Sample.ForeignKey("DonateFrom_EmployeeId", "Employee", "EmployeeId", "Name"),
        Sample.ForeignKey("DonateTo_EmployeeId", "Employee", "EmployeeId", "Name")
    ]);

    private static async Task<string> ViewModel(string template)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Donation(), ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).Single(f => f.RelativePath.EndsWith("DetailViewModel.cs")).Content;
    }

    [TestMethod]
    [DataRow("WinUI3_DetailScreen_v1.tt")]
    [DataRow("UNO_DetailScreen_v1.tt")]
    public async Task An_optional_lookup_starts_its_options_with_a_blank_entry(string template)
    {
        string vm = System.Text.RegularExpressions.Regex.Replace(await ViewModel(template), @"\s+", " ");
        Expect.Contains(vm, "public int? Id { get; set; }");
        Expect.Contains(vm, "EmployeeOptions.Clear(); EmployeeOptions.Add(new E_DonateLeaveDetailLookupOption { Text = \"\" });");
    }

    [TestMethod]
    public async Task A_required_lookup_has_no_blank_entry()
    {
        var required = Sample.Table("E_DonateLeave",
        [
            Sample.Column("DonateLeaveId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("DonateFrom_EmployeeId", SqlDbType.Int, ordinal: 2)
        ],
        [Sample.ForeignKey("DonateFrom_EmployeeId", "Employee", "EmployeeId", "Name")]);
        var result = await Repo.Cache.RunAsync(Repo.Template("WinUI3_DetailScreen_v1.tt"), required, ProjectSettings.FromValues([new("ProjectName", "Acme")]));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        Expect.DoesNotContain(result.GeneratedText!, "{ Text = \"\" }");
    }
}
