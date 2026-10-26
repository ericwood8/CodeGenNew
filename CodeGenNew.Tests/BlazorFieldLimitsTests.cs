using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The Blazor form limits its number, money and date boxes the way the React and Angular forms do (Core's <c>NumberBox</c>, <c>MoneyBox</c> and <c>NotFutureDateColumns</c>), without ApiAnnotations, and
/// leaves a stored value that is already outside the limits alone. </summary>
[TestClass]
public class BlazorFieldLimitsTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Pay() => Sample.Table("Pay",
    [
        Sample.Column("PayId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("MonthNumber", SqlDbType.Int, numericKind: NumericKind.Month, ordinal: 3),
        Sample.Column("Bonus", SqlDbType.Decimal, precision: 18, scale: 2, ordinal: 4, currency: true),
        Sample.Column("Hours", SqlDbType.Int, ordinal: 5),
        Sample.Column("BornOn", SqlDbType.Date, ordinal: 6)
    ]);

    private static async Task<string> Page(ProjectSettings project)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("BLZ_Page_v1.tt"), Pay(), project);
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    [TestMethod]
    public async Task A_number_box_carries_the_limits_Core_decides_only_while_it_holds_a_new_value()
    {
        string page = await Page(Project(("NonNegativeColumns", "Bonus"), ("NotFutureDateColumns", "BornOn")));
        string changed = "stored is null || row.MonthNumber != stored.MonthNumber";

        Expect.Contains(page, $"<InputNumber id=\"payMonthNumber\" @bind-Value=\"row.MonthNumber\" min=\"@({changed} ? \"1\" : null)\" max=\"@({changed} ? \"12\" : null)\" style=\"width: 7ch\" required />");
        Expect.Contains(page, "<CurrencyInput id=\"payBonus\" @bind-Value=\"row.Bonus\" Digits=\"2\" Symbol=\"$\" min=\"0\" required />");
        Expect.Contains(page, "max=\"@(stored is null || row.BornOn != stored.BornOn ? DateTime.Today.ToString(\"yyyy-MM-dd\") : null)\"");
        Expect.Contains(page, "private Pay? stored;");
    }

    [TestMethod]
    public async Task A_box_with_no_limit_is_written_as_before_and_needs_no_stored_row()
    {
        string page = await Page(Project());

        Expect.Contains(page, "<InputNumber id=\"payHours\" @bind-Value=\"row.Hours\" min=\"@(stored is null || row.Hours != stored.Hours ? \"-2147483648\" : null)\" max=\"@(stored is null || row.Hours != stored.Hours ? \"2147483647\" : null)\" required />");
        Expect.Contains(page, "<CurrencyInput id=\"payBonus\" @bind-Value=\"row.Bonus\" Digits=\"2\" Symbol=\"$\" required />");
        Expect.Contains(page, "<InputDate id=\"payBornOn\" @bind-Value=\"row.BornOn\" required />");
    }

    [TestMethod]
    public void Core_writes_the_attributes_of_both_boxes()
    {
        var bonus = Pay().Columns.Single(c => c.Name == "Bonus");
        var project = Project(("NonNegativeColumns", "Bonus"));

        Expect.Contains(NumberBox.For("Pay", bonus, project).Razor("x"), " step=\"0.01\" min=\"@(x ? \"0\" : null)\"");
        Assert.AreEqual(" min=\"0\"", MoneyBox.For("Pay", bonus, project).Razor);
        Assert.AreEqual("", MoneyBox.For("Pay", bonus, Project()).Razor);
    }

    [TestMethod]
    public void The_money_box_refuses_an_edited_amount_outside_its_limits()
    {
        string support = File.ReadAllText(Repo.Template("BLZ_EssentialSupport_v1.tt")).ReplaceLineEndings("\n");

        Expect.Contains(support, "if (Limit(\"min\") is { } smallest && amount < smallest)");
        Expect.Contains(support, "if (Limit(\"max\") is { } largest && amount > largest)");
    }
}
