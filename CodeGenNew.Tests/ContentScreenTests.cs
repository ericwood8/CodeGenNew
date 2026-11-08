using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> The screens of a content table (ContentTables): Publish, Unpublish and Archive on a row, the status shown as text and the slug box that may stay blank, in every front end. </summary>
[TestClass]
public class ContentScreenTests
{
    private static TableModel Post() => Sample.Table("Post",
    [
        Sample.Column("PostId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Title", SqlDbType.NVarChar, characters: 150, ordinal: 2),
        Sample.Column("Slug", SqlDbType.NVarChar, characters: 150, ordinal: 3),
        Sample.Column("Status", SqlDbType.NVarChar, characters: 12, ordinal: 4),
        Sample.Column("PublishedOn", SqlDbType.DateTime, nullable: true, ordinal: 5)
    ], []);

    private static ProjectSettings Project(params (string Key, string Value)[] extra)
    {
        var values = new Dictionary<string, string> { ["ProjectName"] = "Shop", ["ContentTables"] = "Post", ["AccessMode"] = "Ef" };
        foreach (var (key, value) in extra)
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    [TestMethod]
    public void The_status_and_published_time_are_shown_as_text_and_the_slug_is_not_required()
    {
        var form = FormModel.For(Post(), Project());
        Assert.IsTrue(form.Field(form.Columns.Single(c => c.Name == "Status")).IsSystemStampField);
        Assert.IsTrue(form.Field(form.Columns.Single(c => c.Name == "PublishedOn")).IsSystemStampField);
        var slug = form.Field(form.Columns.Single(c => c.Name == "Slug"));
        Assert.IsFalse(slug.IsRequired);
        Assert.IsFalse(slug.IsMarked);
        Assert.AreEqual(ContentScreens.SlugHint, slug.Placeholder);
        Assert.IsTrue(form.Field(form.Columns.Single(c => c.Name == "Title")).IsRequired);
        Assert.DoesNotContain(form.Columns.Single(c => c.Name == "PublishedOn"), form.DateColumns);
    }

    [TestMethod]
    public void A_table_that_is_not_a_content_table_is_unchanged()
    {
        var form = FormModel.For(Post(), ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop" }));
        Assert.IsTrue(form.Field(form.Columns.Single(c => c.Name == "Slug")).IsRequired);
        Assert.IsFalse(form.Field(form.Columns.Single(c => c.Name == "Status")).IsSystemStampField);
        Assert.AreEqual("Enter slug", form.Field(form.Columns.Single(c => c.Name == "Slug")).Placeholder);
    }

    [TestMethod]
    public void Each_button_is_shown_only_when_it_changes_something()
    {
        var content = ContentPlan.For(Post(), Project())!;
        Assert.AreEqual("row.status !== 'published'", ContentScreens.ShownTs(content, ContentScreens.Publish, "row"));
        Assert.AreEqual("row.status === 'published'", ContentScreens.ShownTs(content, ContentScreens.Unpublish, "row"));
        Assert.AreEqual("row.status !== 'archived'", ContentScreens.ShownTs(content, ContentScreens.Archive, "row"));
        Assert.AreEqual("e.Status != \"published\"", ContentScreens.ShownCs(ContentScreens.Publish, "e", "Status"));
        Assert.AreEqual("PublishAsync", ContentScreens.CsMethod(ContentScreens.Publish));
        Assert.AreEqual("Problem while archiving!", ContentScreens.FailedText(ContentScreens.Archive));
    }

    [TestMethod]
    public void The_guarded_update_of_a_desktop_app_is_the_apis_change()
    {
        var content = ContentPlan.For(Post(), Project())!;
        Assert.AreEqual(".SetProperty(x => x.Status, \"published\").SetProperty(x => x.PublishedOn, x => x.PublishedOn ?? now)", ContentScreens.SetCalls(content, ContentScreens.Publish));
        Assert.AreEqual("var now = DateTime.Now;", ContentScreens.NowLine(content, ContentScreens.Publish));
        Assert.AreEqual(".SetProperty(x => x.Status, \"draft\")", ContentScreens.SetCalls(content, ContentScreens.Unpublish));
        Assert.AreEqual("", ContentScreens.NowLine(content, ContentScreens.Archive));
    }

    [TestMethod]
    public void The_sample_row_of_a_test_holds_a_slug_and_a_status_the_rules_leave_alone()
    {
        var table = Post();
        Assert.AreEqual("sample-slug", ContentPlan.SampleText("Post", table.Columns.Single(c => c.Name == "Slug"), Project()));
        Assert.AreEqual("draft", ContentPlan.SampleText("Post", table.Columns.Single(c => c.Name == "Status"), Project()));
        Assert.IsNull(ContentPlan.SampleText("Post", table.Columns.Single(c => c.Name == "Title"), Project()));
        Assert.IsNull(ContentPlan.SampleText("Other", table.Columns.Single(c => c.Name == "Slug"), Project()));
    }

    [TestMethod]
    public void The_desktop_save_lets_a_blank_slug_through_and_leaves_the_state_to_the_rules()
    {
        var table = Post();
        var project = Project();
        var form = FormModel.For(table, project);
        string slug = DesktopFormCode.SaveLines(form, project, table.Columns.Single(c => c.Name == "Slug"));
        Expect.Contains(slug, "entity.Slug = (Slug ?? \"\").Trim();");
        Expect.DoesNotContain(slug, "is required");
        Assert.AreEqual("", DesktopFormCode.SaveLines(form, project, table.Columns.Single(c => c.Name == "Status")));
        string check = DesktopFormCode.ContentCheckLines(project, table, "_context");
        Expect.Contains(check, "await PostContent.PrepareAsync(_context, entity, _editing is null) is { } contentRefusal");
        Assert.AreEqual("", DesktopFormCode.ContentCheckLines(ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop" }), table, "_context"));
    }

    private static string CreateDatabase(bool withChild = false)
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_content_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection($"Data Source={path}");
        connection.Open();
        string[] statements =
        [
            "CREATE TABLE Post (PostId INTEGER PRIMARY KEY AUTOINCREMENT, Title VARCHAR(150) NOT NULL, Slug VARCHAR(150) NOT NULL, Body TEXT NOT NULL, Status VARCHAR(12) NOT NULL DEFAULT 'draft', PublishedOn DATETIME NULL)",
            "CREATE UNIQUE INDEX UX_Post_Slug ON Post (Slug)",
            .. (withChild ? new[] { "CREATE TABLE PostComment (PostCommentId INTEGER PRIMARY KEY AUTOINCREMENT, PostId INTEGER NOT NULL REFERENCES Post (PostId), Author VARCHAR(50) NOT NULL, Remark VARCHAR(200) NOT NULL, Rating INTEGER NOT NULL DEFAULT 0)" } : Array.Empty<string>())
        ];
        foreach (string statement in statements)
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static Task<(GenerateReport Report, Dictionary<string, string> Files)> GenerateAsync(string stacks, params (string Key, string Value)[] extra) => GenerateAsync(stacks, false, extra);

    private static async Task<(GenerateReport Report, Dictionary<string, string> Files)> GenerateAsync(string stacks, bool withChild, params (string Key, string Value)[] extra)
    {
        string database = CreateDatabase(withChild);
        string output = Path.Combine(Path.GetTempPath(), "codegen_content_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = stacks, ["Dashboard"] = "false", ["AccessMode"] = "Ef",
                ["ContentTables"] = "Post", ["Screens"] = "Post", ["AngularVersion"] = "22"
            };
            foreach (var (key, value) in extra)
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = stacks.Split(","), OutputDirectory = output, DatabaseName = database, Schema = "main"
            });
            var files = Directory.Exists(output)
                ? Directory.GetFiles(output, "*", SearchOption.AllDirectories).ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"))
                : [];
            return (report, files);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }

    private static string FileEnding(Dictionary<string, string> files, string ending) => files.Single(f => f.Key.EndsWith(ending)).Value;

    [TestMethod]
    public async Task React_has_the_buttons_the_client_calls_and_a_slug_that_is_not_required()
    {
        var (report, files) = await GenerateAsync("Api,React");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string page = FileEnding(files, "PostPage.tsx");
        Expect.Contains(page, "const publish = async (id: number): Promise<void> =>");
        Expect.Contains(page, "await postApi.publish(id);");
        Expect.Contains(page, "setError('Problem while publishing!');");
        Expect.Contains(page, "{post.status !== 'published' && <button type=\"button\" onClick={() => void publish(post.postId!)}>Publish</button>}");
        Expect.Contains(page, "{post.status === 'published' && <button type=\"button\" onClick={() => void unpublish(post.postId!)}>Unpublish</button>}");
        Expect.Contains(page, "{post.status !== 'archived' && <button type=\"button\" onClick={() => void archive(post.postId!)}>Archive</button>}");
        Expect.Contains(page, "placeholder=\"Leave blank to make it from the title\"");
        Expect.Contains(page, "<span id=\"postStatus\">");
        Expect.DoesNotContain(page, "id=\"postSlug\"\n              required");
        string client = FileEnding(files, "postApi.ts");
        Expect.Contains(client, "publish: (id: number) => request<Post>(`${apiUrl}/${id}/publish`, { method: 'POST' }),");
        Expect.Contains(client, "getPublicBySlug: (slug: string) => request<Post>(`${apiUrl}/public/${encodeURIComponent(slug)}`),");
    }

    [TestMethod]
    public async Task Angular_has_the_buttons_the_service_calls_and_a_slug_that_is_not_required()
    {
        var (report, files) = await GenerateAsync("Api,Angular");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string html = FileEnding(files, "post.component.html");
        Expect.Contains(html, "@if (post.status !== 'published') {");
        Expect.Contains(html, "<button (click)=\"publish(post.postId!)\" class=\"btn-action\">Publish</button>");
        Expect.Contains(html, "@if (post.status === 'published') {");
        Expect.Contains(html, "<button (click)=\"unpublish(post.postId!)\" class=\"btn-action\">Unpublish</button>");
        Expect.Contains(html, "placeholder=\"Leave blank to make it from the title\"");
        string code = FileEnding(files, "post.component.ts");
        Expect.Contains(code, "publish(id: number): void {");
        Expect.Contains(code, "this.service.publish(id).subscribe({ next: (saved) => { if (this.selectedRow?.postId === id)");
        Expect.Contains(code, "this.load(); }, error: (error) => this.showError('publishing', error) });");
        string service = FileEnding(files, "post.service.ts");
        Expect.Contains(service, "publish(id: number): Observable<Post> {");
        Expect.Contains(service, "getPublic(pageNumber = 1, pageSize = 20): Observable<Post[]> {");
    }

    [TestMethod]
    public async Task Blazor_has_the_buttons_the_client_calls()
    {
        var (report, files) = await GenerateAsync("Api,Blazor");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string page = FileEnding(files, "PostPage.razor");
        Expect.Contains(page, "@if (item.Status != \"published\")");
        Expect.Contains(page, "<button type=\"button\" @onclick=\"() => PublishAsync(item)\">Publish</button>");
        Expect.Contains(page, "private async Task PublishAsync(Post item)");
        Expect.Contains(page, "await Client.PublishAsync(item.PostId);");
        string client = FileEnding(files, "PostClient.cs");
        Expect.Contains(client, "public async Task<Post> PublishAsync(int id)");
    }

    [TestMethod]
    public async Task WinUI3_changes_the_state_itself_and_makes_the_slug_before_a_save()
    {
        var (report, files) = await GenerateAsync("WinUI3");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string model = FileEnding(files, "PostListViewModel.cs");
        Expect.Contains(model, "public async Task PublishAsync(int id)");
        Expect.Contains(model, "var now = DateTime.Now;");
        Expect.Contains(model, ".ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, \"published\").SetProperty(x => x.PublishedOn, x => x.PublishedOn ?? now));");
        Expect.Contains(model, "PublishVisibility = e.Status != \"published\" ? Visibility.Visible : Visibility.Collapsed");
        Expect.Contains(FileEnding(files, "PostListPage.xaml"), "Content=\"Unpublish\"");
        Expect.Contains(FileEnding(files, "PostListPage.xaml.cs"), "private async void OnArchiveClick(object sender, RoutedEventArgs e) => await ViewModel.ArchiveAsync((int)((Button)sender).Tag);");
        string content = FileEnding(files, "Repositories/Content.cs");
        Expect.Contains(content, "public static class PostContent");
        Expect.Contains(content, "public static async Task<string?> PrepareAsync(ShopContext context, Post row, bool isNew)");
        string detail = FileEnding(files, "PostDetailViewModel.cs");
        Expect.Contains(detail, "PostContent.PrepareAsync(_context, entity, _editing is null)");
    }

    [TestMethod]
    public async Task Uno_calls_the_api_and_hides_the_buttons_that_would_change_nothing()
    {
        var (report, files) = await GenerateAsync("Api,Uno");
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string model = FileEnding(files, "PostListViewModel.cs");
        Expect.Contains(model, "public async Task PublishAsync(int id)");
        Expect.Contains(model, "await Client.PublishAsync(id);");
        Expect.Contains(model, "ArchiveVisibility = e.Status != \"archived\" ? Visibility.Visible : Visibility.Collapsed");
        Expect.Contains(FileEnding(files, "PostListPage.xaml"), "Click=\"OnPublishClick\"");
    }

    [TestMethod]
    [DataRow(false, "PostODataPage.tsx", "post-odata.component", "PostODataPage.razor")]
    [DataRow(true, "PostODataDetailMasterPage.tsx", "post-odata.component", "PostODataPage.razor")]
    public async Task The_OData_pages_publish_through_the_rest_client_and_ask_for_the_status(bool withChild, string reactPage, string angularStem, string blazorPage)
    {
        var (report, files) = await GenerateAsync("Api,React,Angular,Blazor", withChild, ("OData", "true"));
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string react = FileEnding(files, reactPage);
        Expect.Contains(react, "import { postApi } from '../api/postApi';");
        Expect.Contains(react, "await postApi.publish(id);");
        Expect.Contains(react, ".concat('status'),");
        Expect.Contains(react, "{post.status !== 'published' && <button type=\"button\" onClick={() => void publish(post.postId!)}>Publish</button>}");
        string code = FileEnding(files, angularStem + ".ts");
        Expect.Contains(code, "private readonly shapeService = inject(PostService);");
        Expect.Contains(code, "this.shapeService.publish(id).subscribe(");
        Expect.Contains(code, "protected override readonly alwaysSelected = ['status'];");
        Expect.Contains(FileEnding(files, angularStem + ".html"), "<button (click)=\"archive(post.postId!)\" class=\"btn-action\">Archive</button>");
        string page = FileEnding(files, blazorPage);
        Expect.Contains(page, "@inject PostClient ShapeClient");
        Expect.Contains(page, "await ShapeClient.PublishAsync(item.PostId);");
        Expect.Contains(page, ".Append(\"status\").Distinct().ToList()");
    }

    [TestMethod]
    [DataRow(false, "Detail")]
    [DataRow(true, "DetailMaster")]
    public async Task The_desktop_dialogs_of_a_saved_row_have_the_buttons_and_the_slug_hint(bool withChild, string kind)
    {
        var (report, files) = await GenerateAsync("Api,WinUI3,Uno", withChild);
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        foreach (string app in new[] { "WinUI3", "Uno" })
        {
            string xaml = files.Single(f => f.Key.EndsWith($"Views/Post{kind}Dialog.xaml") && (app == "Uno") == f.Key.Contains("Uno")).Value;
            Expect.Contains(xaml, "<Button Content=\"Publish\" Click=\"OnPublishClick\" Visibility=\"{x:Bind ViewModel.PublishVisibility, Mode=OneWay}\" />");
            Expect.Contains(xaml, "<Button Content=\"Archive\" Click=\"OnArchiveClick\"");
            Expect.Contains(xaml, "PlaceholderText=\"Leave blank to make it from the title\"");
        }
        string winCode = files.Single(f => f.Key.EndsWith($"Post{kind}Dialog.xaml.cs") && !f.Key.Contains("Uno")).Value;
        Expect.Contains(winCode, "private async void OnPublishClick(object sender, RoutedEventArgs e) => await ViewModel.PublishAsync();");
        string winModel = files.Single(f => f.Key.EndsWith($"Post{kind}ViewModel.cs") && !f.Key.Contains("Uno")).Value;
        Expect.Contains(winModel, "private void RefreshContentButtons()");
        Expect.Contains(winModel, "PublishVisibility = _editing is not null && this.Status != \"published\"");
        Expect.Contains(winModel, ".ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, \"published\")");
        Expect.Contains(winModel, "await _context.Entry(_editing).ReloadAsync();");
        string unoModel = files.Single(f => f.Key.EndsWith($"Post{kind}ViewModel.cs") && f.Key.Contains("Uno")).Value;
        Expect.Contains(unoModel, "saved = await _api.Post.PublishAsync(_editing.PostId);");
        Expect.Contains(unoModel, "_editing.Status = saved.Status; _editing.PublishedOn = saved.PublishedOn;");
    }

    [TestMethod]
    [DataRow(false, "PostPage.tsx", "post.component", "PostPage.razor")]
    [DataRow(true, "PostDetailMasterPage.tsx", "post-detail-master.component", "PostPage.razor")]
    public async Task The_web_dialogs_of_a_saved_row_have_the_buttons(bool withChild, string reactPage, string angularStem, string blazorPage)
    {
        var (report, files) = await GenerateAsync("Api,React,Angular,Blazor", withChild);
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        string react = FileEnding(files, reactPage);
        Expect.Contains(react, "{selectedRow.postId && selectedRow.status !== 'published' && <button type=\"button\" onClick={() => void publish(selectedRow.postId!)}>Publish</button>}");
        Expect.Contains(react, "setSelectedRow((current) => current && current.postId === id ? { ...current, status: saved.status, publishedOn: saved.publishedOn } : current);");
        string html = FileEnding(files, angularStem + ".html");
        Expect.Contains(html, "@if (selectedRow.postId && selectedRow.status !== 'published') {");
        Expect.Contains(html, "<button type=\"button\" (click)=\"publish(selectedRow.postId!)\" class=\"btn-action\">Publish</button>");
        Expect.Contains(FileEnding(files, angularStem + ".ts"), "this.selectedRow = { ...this.selectedRow, status: saved.status, publishedOn: saved.publishedOn };");
        string razor = FileEnding(files, blazorPage);
        Expect.Contains(razor, "@if (row!.Status != \"published\")");
        Expect.Contains(razor, "<button type=\"button\" @onclick=\"() => PublishAsync(row!)\">Publish</button>");
        Expect.Contains(razor, "row.Status = saved.Status;");
    }

    [TestMethod]
    public async Task Without_ContentTables_no_screen_has_the_buttons()
    {
        var (report, files) = await GenerateAsync("Api,React,Angular,Blazor", ("ContentTables", ""));
        Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
        foreach (var (name, text) in files.Where(f => f.Key.EndsWith("PostPage.tsx") || f.Key.EndsWith("post.component.html") || f.Key.EndsWith("PostPage.razor")))
        {
            Expect.DoesNotContain(text, "Unpublish");
            Expect.DoesNotContain(text, "Leave blank to make it from the title");
        }
    }
}
