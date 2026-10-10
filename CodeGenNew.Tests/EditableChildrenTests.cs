using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> EditableChildren: the child tables a parent's form edits in place and saves in the same request. One Core rule (EditableChild), the API classes that reconcile the lists and the Angular form
/// that sends them. Run live in a scratch project over SQLite (a name/active parent and an ordinary one: change, add, drop, a row in use, another parent's row, rollback, the Angular form). </summary>
[TestClass]
public class EditableChildrenTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static readonly (string, string) Listed = ("EditableChildren", "Department.DepartmentTeam");

    private static ChildForeignKeyModel Team(bool nullableLink = false, bool identity = true, int parents = 2) => new()
    {
        ConstraintName = "FK_DepartmentTeam_Department",
        ReferencingSchema = "dbo",
        ReferencingTable = "DepartmentTeam",
        ReferencingColumns = ["DepartmentId"],
        ReferencedColumns = ["DepartmentId"],
        ReferencingPrimaryKeyColumns = ["DepartmentTeamId"],
        ReferencingTableColumns =
        [
            Sample.Column("DepartmentTeamId", SqlDbType.Int, primaryKey: true, identity: identity, ordinal: 1),
            Sample.Column("DepartmentId", SqlDbType.Int, nullable: nullableLink, ordinal: 2),
            Sample.Column("RegionId", SqlDbType.Int, ordinal: 3),
            Sample.Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 4),
            Sample.Column("Headcount", SqlDbType.Int, ordinal: 5),
            Sample.Column("IsActive", SqlDbType.Bit, ordinal: 6)
        ],
        ReferencingTableForeignKeys = parents == 2
            ? [Sample.ForeignKey("DepartmentId", "Department", "DepartmentId", "Name"), Sample.ForeignKey("RegionId", "Region", "RegionId", "Title")]
            : [Sample.ForeignKey("DepartmentId", "Department", "DepartmentId", "Name")]
    };

    private static TableModel Department(params ChildForeignKeyModel[] children) => Sample.Table("Department",
    [
        Sample.Column("DepartmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 2),
        Sample.Column("IsActive", SqlDbType.Bit, defaultSql: "((1))", ordinal: 3)
    ], childForeignKeys: [.. children]);

    private static TableModel Project_(params ChildForeignKeyModel[] children) => Sample.Table("Project",
    [
        Sample.Column("ProjectId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 100, ordinal: 2),
        Sample.Column("Budget", SqlDbType.Int, ordinal: 3)
    ], childForeignKeys: [.. children]);

    [TestMethod]
    public void The_setting_is_a_list_and_empty_by_default()
    {
        Assert.IsEmpty(Project().EditableChildren);
        CollectionAssert.AreEqual(new[] { "Department.DepartmentTeam", "ProjectTask" }, Project(("EditableChildren", "Department.DepartmentTeam, ProjectTask")).EditableChildren);
        Assert.AreEqual("Tables", ProjectSettingGroups.TabOf("EditableChildren"));
    }

    [TestMethod]
    public void A_child_is_editable_when_it_is_listed_and_fits()
    {
        var team = Team();
        var department = Department(team);
        Assert.IsTrue(EditableChild.IsEditable(department, team, Project(Listed)));
        Assert.AreEqual("departmentTeams", EditableChild.JsonName(team));
        CollectionAssert.AreEqual(new[] { "RegionId", "Name", "Headcount", "IsActive" }, EditableChild.Columns(team).Select(c => c.Name).ToArray());

        Assert.IsFalse(EditableChild.IsEditable(department, team, Project()), "not listed");
        Assert.IsEmpty(EditableChild.Of(department, Project()));
        Assert.HasCount(1, EditableChild.Of(department, Project(Listed)));
        Assert.IsFalse(EditableChild.IsEditable(department, team, Project(("EditableChildren", "Region.DepartmentTeam"))), "named under another parent");
        Assert.IsFalse(EditableChild.IsEditable(department, team, Project(("EditableChildren", "Department.Other"))));
    }

    [TestMethod]
    public void A_plain_child_name_counts_only_for_a_child_that_belongs_to_one_table()
    {
        var plain = Project(("EditableChildren", "DepartmentTeam"));
        var twoParents = Team();
        Assert.IsFalse(EditableChild.IsEditable(Department(twoParents), twoParents, plain), "a team also points at its region");

        var oneParent = Team(parents: 1);
        Assert.IsTrue(EditableChild.IsEditable(Department(oneParent), oneParent, plain));
    }

    [TestMethod]
    public void A_child_that_cannot_be_reconciled_stays_read_only()
    {
        var project = Project(Listed);
        var nullable = Team(nullableLink: true);
        Assert.IsFalse(EditableChild.IsEditable(Department(nullable), nullable, project), "the link to the parent must be NOT NULL");
        var assigned = Team(identity: false);
        Assert.IsFalse(EditableChild.IsEditable(Department(assigned), assigned, project), "the key must be an identity the database numbers");

        // two links to the same parent: no single list can say which
        var first = Team();
        var second = Team();
        Assert.IsFalse(EditableChild.IsEditable(Department(first, second), first, project));

        // the parent's key must be the column the child points at
        var elsewhere = Team();
        elsewhere.ReferencedColumns[0] = "Name";
        Assert.IsFalse(EditableChild.IsEditable(Department(elsewhere), elsewhere, project));
    }

    [TestMethod]
    public async Task The_API_class_of_the_parent_names_the_lists_it_saves_with_itself()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Crud_v1.tt"), Department(Team()), Project(Listed));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string api = result.GeneratedText!.ReplaceLineEndings("\n");
        Expect.Contains(api, "public class DepartmentApi : NameActiveCrudApi<Department, DepartmentRepo>");
        Expect.Contains(api, "protected override IReadOnlyList<ChildRows<int>> EditableChildren =>");
        Expect.Contains(api, "new ChildRows<int, DepartmentTeam>(\"departmentTeams\", c => c.DepartmentTeamId, c => c.DepartmentId),");

        var ordinary = await Repo.Cache.RunAsync(Repo.Template("API_Crud_v1.tt"), Project_(), Project(Listed));
        Expect.DoesNotContain(ordinary.GeneratedText!, "EditableChildren");
        var unlisted = await Repo.Cache.RunAsync(Repo.Template("API_Crud_v1.tt"), Department(Team()), Project());
        Expect.DoesNotContain(unlisted.GeneratedText!, "EditableChildren");
    }

    [TestMethod]
    public async Task CrudApi_reads_the_child_lists_only_when_the_project_has_editable_children()
    {
        string with = await CrudApiTests.Essential(("EditableChildren", "Department.DepartmentTeam"));
        Expect.Contains(with, "public abstract class ChildRows<TKey> where TKey : notnull");
        Expect.Contains(with, "public sealed class ChildRows<TKey, TChild>(string name, Expression<Func<TChild, int>> key, Expression<Func<TChild, TKey>> parent) : ChildRows<TKey>");
        Expect.Contains(with, "public static class ChildRowsSupport");
        Expect.Contains(with, "context.RemoveRange(existing.Where(row => !kept.Contains(keyOf(row))));");
        Expect.Contains(with, "entry.State = EntityState.Added;");
        Expect.Contains(with, "foreach (var navigation in entry.Navigations)");
        Expect.Contains(with, "if (body.Json.TryGetProperty(list.Name, out var rows) && rows.ValueKind == JsonValueKind.Array)");

        // both bases read the body as JSON, save the lists with the row in one transaction and answer 400 for a refused list
        Assert.AreEqual(2, CountOf(with, "protected virtual IReadOnlyList<ChildRows<"));
        Expect.Contains(with, "protected virtual IReadOnlyList<ChildRows<TKey>> EditableChildren => [];");
        Expect.Contains(with, "protected virtual IReadOnlyList<ChildRows<int>> EditableChildren => [];");
        Assert.AreEqual(2, CountOf(with, "CreateWithChildrenAsync(AcmeContext context, HttpRequest request)"));
        Assert.AreEqual(2, CountOf(with, "UpdateWithChildrenAsync(AcmeContext context,"));
        Assert.AreEqual(4, CountOf(with, "await using var transaction = children is null ? null : await context.Database.BeginTransactionAsync();"));
        Assert.AreEqual(4, CountOf(with, "if (await ChildRowsSupport.SaveAsync(context, EditableChildren,"));
        Assert.AreEqual(4, CountOf(with, "await transaction!.CommitAsync();"));
        Expect.Contains(with, "(EditableChildren.Count > 0\n            ? app.MapPost(apiSubDir, ([FromServices] AcmeContext context, HttpRequest request) => CreateWithChildrenAsync(context, request))");
        Expect.Contains(with, "Results.Problem(statusCode: 400, title: \"Rows refused\"");
        Expect.DoesNotContain(with, "FluentValidation");

        string withValidation = await CrudApiTests.Essential(("EditableChildren", "Department.DepartmentTeam"), ("ApiValidation", "true"));
        Expect.Contains(withValidation, "using FluentValidation;");
        Expect.Contains(withValidation, "return (null, null, Results.ValidationProblem(result.ToDictionary()));");

        string without = await CrudApiTests.Essential();
        foreach (string text in new[] { "ChildRows", "ChildBody", "ChildRowsSupport", "CreateWithChildrenAsync", "BeginTransactionAsync", "System.Text.Json" })
            Expect.DoesNotContain(without, text);
        Expect.Contains(without, "app.MapPost(apiSubDir, ([FromServices] AcmeContext context, [FromBody] TEntity newRow) => CreateAsync(context, newRow))");
    }

    private static int CountOf(string text, string part)
    {
        int count = 0, at = 0;
        while ((at = text.IndexOf(part, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            at += part.Length;
        }
        return count;
    }

    private static async Task<Dictionary<string, string>> Component(TableModel table, params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("TS_DetailMasterComponent_v1.tt"), table, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return GeneratedFiles.Split(result.GeneratedText!).ToDictionary(f => Path.GetFileName(f.RelativePath), f => f.Content.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public async Task The_Angular_form_edits_the_child_rows_and_sends_them_with_the_row()
    {
        var files = await Component(Department(Team()), Listed, ("AngularVersion", "22"));
        string html = files["department-detail-master.component.html"];
        string ts = files["department-detail-master.component.ts"];

        Expect.Contains(html, "<fieldset class=\"child-edit\">");
        Expect.Contains(html, "<legend>Department Team</legend>");
        Expect.Contains(html, "<tr><th>Region</th><th>Name</th><th>Headcount</th><th>Active?</th><th></th></tr>");
        Expect.Contains(html, "@for (row of departmentTeamRows; track $index) {");
        Expect.Contains(html, "[(ngModel)]=\"row.regionId\" required>");
        Expect.Contains(html, "@for (p of departmentTeamOptions['regionId']; track $index) {");
        Expect.Contains(html, "<input type=\"text\" [name]=\"'departmentTeamName' + departmentTeamRows.indexOf(row)\" [(ngModel)]=\"row.name\" maxlength=\"100\" required>");
        Expect.Contains(html, "<input type=\"checkbox\" [name]=\"'departmentTeamIsActive' + departmentTeamRows.indexOf(row)\" [(ngModel)]=\"row.isActive\">");
        Expect.Contains(html, "(click)=\"removeDepartmentTeam(row)\"");
        Expect.Contains(html, "(click)=\"addDepartmentTeam()\"");
        // the editor is inside the form, before Submit, and the read-only grid and its "save first" note are gone
        Assert.IsLessThan(html.IndexOf("<button type=\"submit\"", StringComparison.Ordinal), html.IndexOf("<fieldset class=\"child-edit\">", StringComparison.Ordinal));
        Assert.IsLessThan(html.IndexOf("</form>", StringComparison.Ordinal), html.IndexOf("<fieldset class=\"child-edit\">", StringComparison.Ordinal));
        Expect.DoesNotContain(html, "child-grid");
        Expect.DoesNotContain(html, "Save this Department first");
        Expect.Contains(files["department-detail-master.component.css"], ".child-edit table { width: 100%; }");

        Expect.Contains(ts, "departmentTeamRows: any[] = [];");
        Expect.Contains(ts, "departmentTeamOptions: Record<string, { id: any; name: string }[]> = {};");
        Expect.Contains(ts, "this.http.get<any[]>('api/regions/lookup').subscribe((items) => { this.departmentTeamOptions = { ...this.departmentTeamOptions, regionId:");
        Expect.Contains(ts, "this.departmentTeamRows = rows.filter((r: any) => r['departmentId'] === parentId).map((r: any) => ({ ...r }));");
        Expect.Contains(ts, "addDepartmentTeam(): void {");
        Expect.Contains(ts, "this.departmentTeamRows = [...this.departmentTeamRows, { regionId: null, name: '', headcount: 0, isActive: false }];");
        Expect.Contains(ts, "removeDepartmentTeam(row: any): void {");
        Expect.Contains(ts, "protected override prepare(row: Department, isNew: boolean): void {");
        Expect.Contains(ts, "(row as unknown as Record<string, unknown>)['departmentTeams'] = this.departmentTeamRows;");
        Expect.Contains(ts, "this.loadDepartmentTeam(department.departmentId!);");
        // nothing of the read-only grid is left for a form that has no other child
        foreach (string text in new[] { "childSorts", "GridSort", "HostListener", "orderColumns", "apiMessage", "departmentTeamColumns", "editDepartmentTeam" })
            Expect.DoesNotContain(ts, text);

        Expect.Contains(files["department-detail-master.component.spec.ts"], "keeps the department team rows in the form and sends them with the row");
        Expect.Contains(files["department-detail-master.component.spec.ts"], "expect(row['departmentTeams'].length).toBe(1);");
    }

    [TestMethod]
    public async Task A_child_that_is_not_listed_keeps_its_read_only_grid_beside_the_editable_one()
    {
        var other = new ChildForeignKeyModel
        {
            ConstraintName = "FK_DepartmentNote_Department",
            ReferencingSchema = "dbo",
            ReferencingTable = "DepartmentNote",
            ReferencingColumns = ["DepartmentId"],
            ReferencedColumns = ["DepartmentId"],
            ReferencingPrimaryKeyColumns = ["DepartmentNoteId"],
            ReferencingTableColumns =
            [
                Sample.Column("DepartmentNoteId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
                Sample.Column("DepartmentId", SqlDbType.Int, ordinal: 2),
                Sample.Column("Body", SqlDbType.NVarChar, characters: 200, ordinal: 3)
            ],
            ReferencingTableForeignKeys = [Sample.ForeignKey("DepartmentId", "Department", "DepartmentId", "Name")]
        };
        var files = await Component(Department(Team(), other), Listed);
        string html = files["department-detail-master.component.html"];
        string ts = files["department-detail-master.component.ts"];
        Expect.Contains(html, "<fieldset class=\"child-edit\">");
        Expect.Contains(html, "<div class=\"child-grid\"");
        Expect.Contains(html, "<h3>Department Note</h3>");
        Expect.DoesNotContain(html, "<h3>Department Team</h3>");
        Expect.Contains(ts, "departmentNoteColumns: string[] = [];");
        Expect.Contains(ts, "editDepartmentNote(row: any): void {");
        Expect.DoesNotContain(ts, "departmentTeamColumns");
        Expect.DoesNotContain(ts, "editDepartmentTeam");
    }

    [TestMethod]
    public async Task Without_the_setting_the_form_is_what_it_was()
    {
        var files = await Component(Department(Team()), ("AngularVersion", "22"));
        string html = files["department-detail-master.component.html"];
        string ts = files["department-detail-master.component.ts"];
        Expect.Contains(html, "<div class=\"child-grid\"");
        Expect.DoesNotContain(html, "child-edit");
        Expect.DoesNotContain(ts, "prepare(");
        Expect.DoesNotContain(files["department-detail-master.component.css"], ".child-edit");
        Expect.DoesNotContain(files["department-detail-master.component.spec.ts"], "sends them with the row");
    }
}
