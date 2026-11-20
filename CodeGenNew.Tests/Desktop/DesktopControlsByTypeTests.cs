using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Core.Desktop;

namespace CodeGenNew.Tests.Desktop;

/// <summary> The control a WinUI3 and Uno form gives a column by its type: a toggle switch or radio buttons for the columns the project lists. </summary>
[TestClass]
public class DesktopControlsByTypeTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Shift() => Sample.Table("Shift",
    [
        Sample.Column("ShiftId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 40, ordinal: 2),
        Sample.Column("StartsAt", SqlDbType.Time, ordinal: 3),
        Sample.Column("BreakAt", SqlDbType.Time, nullable: true, ordinal: 4),
        Sample.Column("IsActive", SqlDbType.Bit, ordinal: 5),
        Sample.Column("IsOpen", SqlDbType.Bit, nullable: true, ordinal: 6),
        Sample.Column("Priority", SqlDbType.VarChar, characters: 10, choices: ["Low", "Normal", "High"], ordinal: 7),
        Sample.Column("Category", SqlDbType.VarChar, characters: 10, choices: ["A", "B", "C", "D", "E"], ordinal: 8)
    ]);

    private static string Xaml(ProjectSettings project, string column)
    {
        var table = Shift();
        var form = FormModel.For(table, project);
        return DesktopFormXaml.Field(form, project, table, table.Columns.First(c => c.Name == column), textKey: false);
    }

    private static string Save(ProjectSettings project, string column)
    {
        var table = Shift();
        return DesktopFormCode.SaveLines(FormModel.For(table, project), project, table.Columns.First(c => c.Name == column));
    }

    [TestMethod]
    public void A_time_of_day_column_is_on_no_form_yet_so_no_desktop_control_is_chosen_for_it()
    {
        var form = FormModel.For(Shift(), Project());
        CollectionAssert.DoesNotContain(form.Columns.Select(c => c.Name).ToList(), "StartsAt");
        CollectionAssert.DoesNotContain(form.Columns.Select(c => c.Name).ToList(), "BreakAt");
    }

    [TestMethod]
    public void A_yes_no_column_is_a_check_box_until_the_project_lists_it_for_a_toggle_switch()
    {
        Expect.Contains(Xaml(Project(), "IsActive"), "<CheckBox Content=\"Active?\"");
        var project = Project(("ToggleSwitchColumns", "IsActive,Shift.IsOpen"));
        Expect.Contains(Xaml(project, "IsActive"), "<ToggleSwitch Header=\"Active?\" IsOn=\"{x:Bind ViewModel.IsActive, Mode=TwoWay}\" />");
        Expect.Contains(Xaml(project, "IsOpen"), "<ToggleSwitch");
        Assert.DoesNotContain("<ToggleSwitch", Xaml(Project(("ToggleSwitchColumns", "Other.IsActive")), "IsActive"));
    }

    [TestMethod]
    public void A_toggle_switch_is_held_as_a_plain_bool_and_a_null_column_reads_as_off()
    {
        var project = Project(("ToggleSwitchColumns", "IsActive,IsOpen"));
        var table = Shift();
        var form = FormModel.For(table, project);
        Assert.AreEqual("bool", DesktopFormCode.FieldType(form, table.Columns.First(c => c.Name == "IsActive")));
        Assert.AreEqual("bool?", DesktopFormCode.FieldType(FormModel.For(table, Project()), table.Columns.First(c => c.Name == "IsActive")));
        Assert.AreEqual("entity.IsActive = IsActive;\n", Save(project, "IsActive").TrimStart());
        Assert.AreEqual("entity.IsOpen = IsOpen;\n", Save(project, "IsOpen").TrimStart());
        Assert.AreEqual("entity.IsActive = IsActive ?? false;\n", Save(Project(), "IsActive").TrimStart());
        Expect.Contains(DesktopFormCode.LoadLines(form, table.Columns.First(c => c.Name == "IsOpen")), "IsOpen = editing.IsOpen ?? false;");
    }

    [TestMethod]
    public void A_short_choice_list_is_radio_buttons_only_when_listed_and_a_longer_one_stays_a_drop_down()
    {
        Expect.Contains(Xaml(Project(), "Priority"), "<ComboBox Header=\"Priority\"");
        var project = Project(("RadioButtonColumns", "Priority,Category"));
        string radio = Xaml(project, "Priority");
        Expect.Contains(radio, "<RadioButtons Header=\"Priority\"");
        Expect.Contains(radio, "ItemsSource=\"{x:Bind ViewModel.PriorityChoices}\"");
        Expect.Contains(radio, "SelectedItem=\"{x:Bind ViewModel.Priority, Mode=TwoWay}\"");
        Expect.Contains(Xaml(project, "Category"), "<ComboBox Header=\"Category\"");
    }

    [TestMethod]
    public async Task The_dialog_templates_of_both_stacks_write_the_toggle_switch_and_the_radio_buttons()
    {
        var project = Project(("ToggleSwitchColumns", "IsActive"), ("RadioButtonColumns", "Priority"));
        foreach (string template in new[] { "WinUI3_DetailScreen_v1.tt", "UNO_DetailScreen_v1.tt" })
        {
            var result = await Repo.Cache.RunAsync(Repo.Template(template), Shift(), project);
            Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
            string text = result.GeneratedText!.ReplaceLineEndings("\n");
            Expect.Contains(text, "<ToggleSwitch Header=\"Active?\"");
            Expect.Contains(text, "<RadioButtons Header=\"Priority\"");
            Expect.Contains(text, "bool _isActive;");
        }
    }
}
