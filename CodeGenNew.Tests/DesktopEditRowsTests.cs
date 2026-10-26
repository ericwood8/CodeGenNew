using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> EditableChildren on the desktop stacks: the child rows are boxes inside the parent's dialog (rows, lookups and panel written from Core), saved with the parent: in one transaction on WinUI3, in
/// the API's parent-with-children request on Uno. </summary>
[TestClass]
public class DesktopEditRowsTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")).Append(new("EditableChildren", "Project.ProjectTask")));

    private static ChildForeignKeyModel Task(bool binary = false) => new()
    {
        ConstraintName = "FK_ProjectTask_Project",
        ReferencingSchema = "dbo",
        ReferencingTable = "ProjectTask",
        ReferencingColumns = ["ProjectId"],
        ReferencedColumns = ["ProjectId"],
        ReferencingPrimaryKeyColumns = ["ProjectTaskId"],
        ReferencingTableColumns =
        [
            Sample.Column("ProjectTaskId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("ProjectId", SqlDbType.Int, ordinal: 2),
            Sample.Column("OwnerId", SqlDbType.Int, ordinal: 3),
            Sample.Column("Title", SqlDbType.NVarChar, characters: 100, ordinal: 4),
            Sample.Column("Hours", SqlDbType.Int, ordinal: 5),
            Sample.Column("Done", SqlDbType.Bit, ordinal: 6),
            binary ? Sample.Column("Attachment", SqlDbType.VarBinary, ordinal: 7) : Sample.Column("DueOn", SqlDbType.Date, nullable: true, ordinal: 7)
        ],
        ReferencingTableForeignKeys =
        [
            Sample.ForeignKey("ProjectId", "Project", "ProjectId", "Title"),
            Sample.ForeignKey("OwnerId", "Person", "PersonId", "Name")
        ]
    };

    private static TableModel Parent(ChildForeignKeyModel child, bool nameActive = false) => Sample.Table("Project",
    [
        Sample.Column("ProjectId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column(nameActive ? "Name" : "Title", SqlDbType.NVarChar, characters: 100, ordinal: 2),
        Sample.Column("Budget", SqlDbType.Int, ordinal: 3),
        .. nameActive ? [Sample.Column("IsActive", SqlDbType.Bit, ordinal: 4)] : Array.Empty<ColumnModel>()
    ], childForeignKeys: [child]);

    private static async Task<Dictionary<string, string>> Run(string template, TableModel table)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!.ReplaceLineEndings("\n")).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public void A_child_is_edited_in_place_when_the_web_rule_allows_it_and_the_boxes_can_hold_every_column()
    {
        var project = Project();

        Assert.IsTrue(DesktopChild.For(Parent(Task()), project, needsOwnKey: false).Single().Editable);
        Assert.IsFalse(DesktopChild.For(Parent(Task(binary: true)), project, needsOwnKey: false).Single().Editable, "a binary column has no box");
        Assert.IsFalse(DesktopChild.For(Parent(Task(), nameActive: true), project, needsOwnKey: false).Single().Editable, "a name / active parent shows its children read-only");
        Assert.IsFalse(DesktopChild.For(Parent(Task()), ProjectSettings.FromValues([new("ProjectName", "Acme")]), needsOwnKey: false).Single().Editable);
    }

    [TestMethod]
    public async Task WinUI3_saves_the_row_and_its_lines_in_one_transaction()
    {
        var files = await Run("WinUI3_DetailMasterScreen_v1.tt", Parent(Task()));
        string vm = files["ProjectDetailMasterViewModel.cs"];

        Expect.Contains(files["ProjectDetailMasterDialog.xaml"], "<StackPanel x:Name=\"projectTaskEditHost\" />");
        Assert.DoesNotContain("Save this Project first", files["ProjectDetailMasterDialog.xaml"], "every child is editable, so nothing waits for the first save");
        Expect.Contains(vm, "public ObservableCollection<ProjectProjectTaskEditRow> projectTaskEdit { get; } = [];");
        Expect.Contains(vm, "if (CheckEditRows() is { } rowError)");
        Expect.Contains(vm, "await using var transaction = await _context.Database.BeginTransactionAsync();");
        Expect.Contains(vm, "await SaveEditRowsAsync(entity);");
        Expect.Contains(vm, "_context.Remove(gone);");
        Expect.Contains(vm, "_context.Entry(added).Property(\"ProjectId\").CurrentValue = parentKey;");
        Expect.Contains(vm, "using Acme.App.Views;");
        Expect.Contains(files["ProjectDetailMasterDialog.xaml.cs"], "projectTaskEditHost.Children.Add(_projectTaskPanel);");
    }

    [TestMethod]
    public async Task Uno_sends_the_lines_with_the_row_in_the_one_request_the_api_reconciles()
    {
        var files = await Run("UNO_DetailMasterScreen_v1.tt", Parent(Task()));
        string vm = files["ProjectDetailMasterViewModel.cs"];

        Expect.Contains(vm, "[\"projectTasks\"] = projectTaskEdit.Select(l => { var model = new ProjectTask(); l.ApplyTo(model); model.ProjectTaskId = l.RowKey; return model; }).ToList(),");
        Expect.Contains(vm, "await _api.Project.CreateAsync(entity, children);");
        Expect.Contains(vm, "await _api.Project.UpdateAsync(entity.ProjectId, entity, children);");
        Expect.Contains(vm, "_api.Person.GetLookupAsync()");
    }

    [TestMethod]
    public async Task Both_stacks_write_the_same_boxes_and_the_same_checks()
    {
        var winui3 = (await Run("WinUI3_DetailMasterScreen_v1.tt", Parent(Task())))["ProjectDetailMasterEditRows.cs"];
        var uno = (await Run("UNO_DetailMasterScreen_v1.tt", Parent(Task())))["ProjectDetailMasterEditRows.cs"];

        Assert.AreEqual(winui3, uno.Replace("using Acme.App.Models;\n", ""), "only the using of the models differs");
        Expect.Contains(winui3, "if (OwnerId is null) return \"Owner is required.\";");
        Expect.Contains(winui3, "if (string.IsNullOrWhiteSpace(Title)) return \"Title is required.\";");
        Expect.Contains(winui3, "if (!double.IsNaN(Hours) && Hours != Math.Floor(Hours)) return \"Hours must be a whole number.\";");
        Expect.Contains(winui3, "e.DueOn = DueOn is null ? null : DueOn.Value.Date + _dueOnTime;");
        Expect.Contains(winui3, "var boxOwnerId = new ComboBox");
        Expect.Contains(winui3, "boxDone.Checked += (_, _) => row.Done = true;");
        Expect.Contains(winui3, "AutomationProperties.SetName(boxTitle, \"Title\");");
    }

    [TestMethod]
    public async Task A_name_active_parent_and_a_child_with_a_binary_column_stay_read_only_grids()
    {
        var nameActive = await Run("WinUI3_DetailMasterScreen_v1.tt", Parent(Task(), nameActive: true));
        var binary = await Run("UNO_DetailMasterScreen_v1.tt", Parent(Task(binary: true)));

        Assert.IsFalse(nameActive.ContainsKey("ProjectDetailMasterEditRows.cs"));
        Assert.IsFalse(binary.ContainsKey("ProjectDetailMasterEditRows.cs"));
        Expect.Contains(nameActive["ProjectDetailMasterDialog.xaml"], "Save this Project first");
    }
}
