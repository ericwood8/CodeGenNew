using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> EditableChildren on the React master-detail page: the child rows are inputs inside the form, added and removed there and sent with the row. Run live in a scratch React app over SQLite and its API
/// (edit a row: change one task, remove one, add one, one Submit; a new row with a task; a blank required field refused by the browser) and its generated vitest file. </summary>
[TestClass]
public class EditableChildrenReactTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static readonly (string, string) Listed = ("EditableChildren", "Project.ProjectTask");

    private static ChildForeignKeyModel Task() => new()
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
            Sample.Column("Summary", SqlDbType.NVarChar, characters: 100, ordinal: 3),
            Sample.Column("DueOn", SqlDbType.Date, ordinal: 4),
            Sample.Column("OwnerId", SqlDbType.Int, nullable: true, ordinal: 5),
            Sample.Column("Hours", SqlDbType.Int, ordinal: 6),
            Sample.Column("Done", SqlDbType.Bit, ordinal: 7)
        ],
        ReferencingTableForeignKeys =
        [
            Sample.ForeignKey("ProjectId", "Project", "ProjectId", "Title"),
            Sample.ForeignKey("OwnerId", "Employee", "EmployeeId", "Name")
        ]
    };

    private static ChildForeignKeyModel Note() => new()
    {
        ConstraintName = "FK_ProjectNote_Project",
        ReferencingSchema = "dbo",
        ReferencingTable = "ProjectNote",
        ReferencingColumns = ["ProjectId"],
        ReferencedColumns = ["ProjectId"],
        ReferencingPrimaryKeyColumns = ["ProjectNoteId"],
        ReferencingTableColumns =
        [
            Sample.Column("ProjectNoteId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("ProjectId", SqlDbType.Int, ordinal: 2),
            Sample.Column("Body", SqlDbType.NVarChar, characters: 200, ordinal: 3)
        ],
        ReferencingTableForeignKeys = [Sample.ForeignKey("ProjectId", "Project", "ProjectId", "Title")]
    };

    private static TableModel Parent(params ChildForeignKeyModel[] children) => Sample.Table("Project",
    [
        Sample.Column("ProjectId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 100, ordinal: 2),
        Sample.Column("Budget", SqlDbType.Int, ordinal: 3)
    ], childForeignKeys: [.. children]);

    private static async Task<Dictionary<string, string>> Page(TableModel table, params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("TSX_DetailMasterPage_v1.tt"), table, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public async Task The_form_edits_the_child_rows_and_sends_them_with_the_row()
    {
        var files = await Page(Parent(Task()), Listed);
        string page = files["ProjectDetailMasterPage.tsx"];

        Expect.Contains(page, "const [projectTaskRows, setProjectTaskRows] = useState<any[]>([]);");
        Expect.Contains(page, "const [projectTaskOptions, setProjectTaskOptions] = useState<Record<string, { id: any; name: string }[]>>({});");
        Expect.Contains(page, "request<any[]>('/api/employees/lookup').then((items) => setProjectTaskOptions((options) => ({ ...options, ownerId: items.map((i) => ({ id: i['id'], name: String(i['name'] ?? '') })) }))).catch(() => {});");
        Expect.Contains(page, "const withChildren = (row: Project): Project => ({ ...row, projectTasks: projectTaskRows }) as Project;");
        Expect.Contains(page, "await projectApi.update(project.projectId, withChildren(project));");
        Expect.Contains(page, "await projectApi.create(withChildren(project));");
        Expect.Contains(page, "r['projectId'] === parentId).map((r) => ({ ...r, dueOn: r['dueOn']?.substring(0, 10) ?? null }))");
        Expect.Contains(page, "const addProjectTask = () => setProjectTaskRows((rows) => [...rows, { summary: '', dueOn: new Date().toISOString().substring(0, 10), hours: 0, done: false }]);");
        Expect.Contains(page, "const removeProjectTask = (row: any) => setProjectTaskRows((rows) => rows.filter((r) => r !== row));");
        Expect.Contains(page, "    loadProjectTask(project.projectId!);");

        Expect.Contains(page, "<legend>Project Task</legend>");
        Expect.Contains(page, "<tr><th>Summary</th><th>Due On</th><th>Owner</th><th>Hours</th><th>Done?</th><th></th></tr>");
        Expect.Contains(page, "{projectTaskRows.map((row, index) => (");
        Expect.Contains(page, "<input type=\"text\" aria-label=\"Summary\" value={row.summary ?? ''} onChange={(event) => changeProjectTask(row, { summary: event.target.value })} maxLength={100} required />");
        Expect.Contains(page, "<input type=\"date\" aria-label=\"Due On\" value={row.dueOn ?? ''} onChange={(event) => changeProjectTask(row, { dueOn: event.target.value || null })} required />");
        Expect.Contains(page, "<select aria-label=\"Owner\" value={row.ownerId ?? ''} onChange={(event) => changeProjectTask(row, { ownerId: event.target.value === '' ? null : Number(event.target.value) })}><option value=\"\"></option>"
                              + "{(projectTaskOptions['ownerId'] ?? []).map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select>");
        Expect.Contains(page, "<input type=\"number\" aria-label=\"Hours\" value={row.hours ?? ''} onChange={(event) => changeProjectTask(row, { hours: event.target.value === '' ? null : Number(event.target.value) })} min=\"-2147483648\" max=\"2147483647\" required />");
        Expect.Contains(page, "<input type=\"checkbox\" aria-label=\"Done?\" checked={!!row.done} onChange={(event) => changeProjectTask(row, { done: event.target.checked })} />");
        Expect.Contains(page, "onClick={() => removeProjectTask(row)}>Remove</button>");
        Expect.Contains(page, "<button type=\"button\" onClick={addProjectTask}>Add Project Task</button>");

        // the editor is inside the form, before Submit, and the read-only grid and its "save first" note are gone
        Assert.IsLessThan(page.IndexOf("<button type=\"submit\"", StringComparison.Ordinal), page.IndexOf("<fieldset>", StringComparison.Ordinal));
        Assert.IsLessThan(page.IndexOf("</form>", StringComparison.Ordinal), page.IndexOf("<fieldset>", StringComparison.Ordinal));
        foreach (string text in new[] { "Save this Project first", "projectTaskColumns", "projectTaskSort", "orderColumns", "formatChildCell", "sortRows", "editProjectTask", "deleteProjectTask" })
            Expect.DoesNotContain(page, text);

        string test = files["ProjectDetailMasterPage.test.tsx"];
        Expect.Contains(test, "import { fireEvent, render, screen } from '@testing-library/react';");
        Expect.Contains(test, "it('adds and removes project task rows in the form', async () => {");
        Expect.Contains(test, "fireEvent.click(await screen.findByText('Add Project Task'));");
        Expect.Contains(test, "expect(screen.getAllByText('Remove')).toHaveLength(2);");
    }

    [TestMethod]
    public async Task A_child_that_is_not_listed_keeps_its_read_only_grid_beside_the_editable_one()
    {
        var files = await Page(Parent(Task(), Note()), Listed);
        string page = files["ProjectDetailMasterPage.tsx"];

        Expect.Contains(page, "<fieldset>");
        Expect.Contains(page, "<h3>Project Note</h3>");
        Expect.Contains(page, "const [projectNoteColumns, setProjectNoteColumns]");
        Expect.Contains(page, "editProjectNote");
        Expect.Contains(page, "sortRows");
        Expect.Contains(page, "const withChildren = (row: Project): Project => ({ ...row, projectTasks: projectTaskRows }) as Project;");
        Expect.DoesNotContain(page, "projectTaskColumns");
        Expect.DoesNotContain(page, "projectNoteRows: projectNoteRows");
        Expect.DoesNotContain(page, "<h3>Project Task</h3>");
        Expect.Contains(files["ProjectDetailMasterPage.test.tsx"], "toHaveLength(1)");
    }

    [TestMethod]
    public async Task Without_the_setting_the_page_is_what_it_was()
    {
        var files = await Page(Parent(Task()));
        string page = files["ProjectDetailMasterPage.tsx"];

        Expect.Contains(page, "<h3>Project Task</h3>");
        Expect.Contains(page, "sortRows");
        Expect.Contains(page, "Save this Project first");
        Expect.DoesNotContain(page, "withChildren");
        Expect.DoesNotContain(page, "<fieldset>");
        Expect.DoesNotContain(page, "addProjectTask");
        Expect.DoesNotContain(files["ProjectDetailMasterPage.test.tsx"], "adds and removes");
    }
}
