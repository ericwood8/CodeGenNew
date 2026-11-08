using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> AssetTables: a table that points at a stored file, with an upload route, a download route and a delete that removes the file. </summary>
[TestClass]
public class AssetTableTests
{
    private static TableModel Asset(int keyLength = 300, bool withUrl = true, bool extraRequired = false)
    {
        var columns = new List<ColumnModel>
        {
            Sample.Column("AssetId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("FileName", SqlDbType.NVarChar, characters: 200, ordinal: 2),
            Sample.Column("MimeType", SqlDbType.NVarChar, characters: 100, ordinal: 3),
            Sample.Column("SizeBytes", SqlDbType.BigInt, ordinal: 4),
            Sample.Column("StorageKey", SqlDbType.NVarChar, characters: keyLength, ordinal: 5),
        };
        if (withUrl)
            columns.Add(Sample.Column("PublicUrl", SqlDbType.NVarChar, characters: 500, nullable: true, ordinal: 6));
        if (extraRequired)
            columns.Add(Sample.Column("Owner", SqlDbType.NVarChar, characters: 50, ordinal: 7));
        return Sample.Table("Asset", columns, []);
    }

    private static ProjectSettings Project(params (string Key, string Value)[] extra)
    {
        var values = new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AssetTables"] = "Asset", ["AccessMode"] = "Ef" };
        foreach (var (key, value) in extra)
            values[key] = value;
        return ProjectSettings.FromValues(values);
    }

    private static string Written(TableModel table, ProjectSettings project) =>
        RowShapeWriter.File(new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [table] }, project, "ShopContext", []);

    [TestMethod]
    public void The_columns_are_found_by_their_names_and_can_be_named()
    {
        var asset = AssetPlan.For(Asset(), Project())!;
        Assert.AreEqual("StorageKey", asset.StorageKey.Name);
        Assert.AreEqual("FileName", asset.FileName.Name);
        Assert.AreEqual("MimeType", asset.MimeType.Name);
        Assert.AreEqual("SizeBytes", asset.Size.Name);
        Assert.IsNull(asset.Url);
        Assert.AreEqual("PublicUrl", AssetPlan.For(Asset(), Project(("AssetTables", "Asset:StorageKey:FileName:MimeType:SizeBytes:PublicUrl")))!.Url!.Name);
        Assert.IsNotNull(AssetPlan.Problem(Asset(), Project(("AssetTables", "Asset:Nothing"))));
    }

    [TestMethod]
    public void A_table_that_cannot_hold_a_stored_file_is_refused_with_the_reason()
    {
        Assert.Contains("room for", AssetPlan.Problem(Asset(keyLength: 20), Project())!);
        Assert.Contains("cannot fill Owner", AssetPlan.Problem(Asset(extraRequired: true), Project())!);
        Assert.Contains("SoftDeleteTables", AssetPlan.Problem(Asset(), Project(("SoftDeleteTables", "Asset")))!);
        Assert.IsNull(AssetPlan.Problem(Asset(), Project()));
        Assert.IsNull(AssetPlan.For(Asset(keyLength: 20), Project()));
    }

    [TestMethod]
    public void The_shape_is_known_to_the_table_shapes_and_the_row_shape_plan()
    {
        var table = Asset();
        var project = Project();
        Assert.IsNotNull(TableShapes.For(table, project).Asset);
        Assert.IsTrue(RowShapePlan.Has(table, project));
        Assert.IsTrue(RowShapePlan.Any(project));
        Assert.IsTrue(RowShapeWriter.HasSaveCheck(table, project));
        Assert.IsTrue(RowShapeWriter.HasDeleteCheck(table, project));
        Assert.HasCount(0, RowShapePlan.Problems(new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [table] }, project));
        Assert.Contains("is not a table", RowShapePlan.Problems(new DatabaseModel { DatabaseName = "Shop", SchemaName = "dbo", Tables = [table] }, Project(("AssetTables", "Nothing")))[0]);
    }

    [TestMethod]
    public void The_routes_the_store_and_the_limits_are_written()
    {
        string code = Written(Asset(), Project(("AssetMaxBytes", "5000"), ("AssetAllowedTypes", "image/*,application/pdf"), ("AssetStoreFolder", "files/store")));
        Assert.Contains("app.MapPost(\"/api/assets/upload\"", code);
        Assert.Contains("app.MapGet(\"/api/assets/{id}/download\"", code);
        Assert.Contains("public interface IAssetStore", code);
        Assert.Contains("new LocalFolderAssetStore(\"files/store\")", code);
        Assert.Contains("MaxBytes = 5000", code);
        Assert.Contains("[\"image/*\", \"application/pdf\"]", code);
        Assert.Contains(AssetWords.TooLargeType, code);
        Assert.Contains("await RememberFileAsync(context, id);", code);
        Assert.Contains("public static async Task AfterDeleteAsync(", code);
        Assert.DoesNotContain("§", code);
        Assert.DoesNotContain("PublicUrl", code);
    }

    [TestMethod]
    public void The_download_address_is_written_when_the_table_has_a_column_for_it()
    {
        string code = Written(Asset(), Project(("AssetTables", "Asset:StorageKey:FileName:MimeType:SizeBytes:PublicUrl")));
        Assert.Contains("SetProperty(x => x.PublicUrl, AssetKeys.Cut($\"/api/assets/{id}/download\", 500))", code);
        Assert.Contains("OUTPUT INSERTED.[AssetId] AS [Value]", code);
    }

    [TestMethod]
    public void A_path_that_could_leave_the_store_folder_is_not_a_key()
    {
        string code = Written(Asset(), Project());
        Assert.Contains("key.Length is > 0 and <= 200 && key[0] != '.' && !key.Contains(\"..\")", code);
        Assert.Contains("Not a storage key.", code);
    }

    private static DatabaseModel Shop(TableModel table, SqlDialect dialect = SqlDialect.Sqlite) => new() { DatabaseName = "Shop", SchemaName = "dbo", Dialect = dialect, Tables = [table] };

    [TestMethod]
    public void The_python_routes_the_store_and_the_events_that_keep_the_key_are_written()
    {
        var project = Project(("AssetMaxBytes", "5000"), ("AssetAllowedTypes", "image/*,application/pdf"), ("AssetStoreFolder", "files/store"));
        var shapes = PythonAssetWriter.Shapes(Shop(Asset()), project);
        string code = PythonAssetWriter.File(shapes, project);
        Assert.Contains("@router.post(f\"{spec.route}/upload\"", code);
        Assert.Contains("@router.get(f\"{spec.route}/{{id}}/download\")", code);
        Assert.Contains("os.environ.get(\"ASSET_STORE_FOLDER\", \"files/store\")", code);
        Assert.Contains("MAX_BYTES = 5000", code);
        Assert.Contains("ALLOWED_TYPES = [\"image/*\", \"application/pdf\"]", code);
        Assert.Contains("event.listens_for(spec.row, \"before_update\")", code);
        Assert.Contains(AssetWords.TooLargeType, code);
        Assert.DoesNotContain("§", code);
    }

    [TestMethod]
    public void The_python_tests_are_written_for_a_project_with_python_tests_only()
    {
        Assert.IsFalse(PythonAssetTests.Applies(Shop(Asset()), Project()));
        var project = Project(("PythonTests", "true"));
        Assert.IsTrue(PythonAssetTests.Applies(Shop(Asset()), project));
        string tests = PythonAssetTests.File(Shop(Asset()), project);
        Assert.Contains("def test_an_uploaded_file_can_be_downloaded_and_a_delete_removes_it", tests);
        Assert.Contains(AssetWords.NoFileType, tests);
        Assert.DoesNotContain("§", tests);
    }

    [TestMethod]
    public void The_rust_routes_the_store_and_the_hooks_of_the_table_are_written()
    {
        var project = Project(("AssetMaxBytes", "5000"), ("AssetAllowedTypes", "image/*"));
        var shapes = RustAssetWriter.Shapes(Shop(Asset()), project);
        string code = RustAssetWriter.File(shapes, project, SqlDialect.Sqlite);
        Assert.Contains("pub const MAX_BYTES: usize = 5000;", code);
        Assert.Contains("const ALLOWED_TYPES: &[&str] = &[\"image/*\"];", code);
        Assert.Contains(".route(\"/api/assets/upload\"", code);
        Assert.Contains("RETURNING \"AssetId\"", code);
        Assert.DoesNotContain("§", code);
        var main = RustTable.Of(Asset(), project);
        Assert.Contains("row.storage_key = crate::routes::asset_files::unattached();", RustAssetWriter.CreateLine(main, shapes[0]));
        Assert.Contains("row.size_bytes = stored.size_bytes.clone();", RustAssetWriter.UpdateLines(main, shapes[0]));
        Assert.Contains("file_key", RustAssetWriter.DeleteLines(main, shapes[0]));
    }

    [TestMethod]
    public void The_rust_insert_reads_the_new_key_without_returning_on_mysql()
    {
        string code = RustAssetWriter.File(RustAssetWriter.Shapes(Shop(Asset(), SqlDialect.MySql), Project()), Project(), SqlDialect.MySql);
        Assert.DoesNotContain("RETURNING", code);
        Assert.Contains("last_insert_id()", code);
    }

    [TestMethod]
    public void The_screens_know_the_columns_the_upload_owns_and_the_file_types_to_offer()
    {
        var table = Asset();
        var project = Project(("AssetAllowedTypes", "image/*,application/pdf"));
        Assert.IsTrue(AssetScreens.IsUploadColumn(table, project, table.Columns.First(c => c.Name == "StorageKey")));
        Assert.IsTrue(AssetScreens.IsUploadColumn(table, project, table.Columns.First(c => c.Name == "MimeType")));
        Assert.IsFalse(AssetScreens.IsUploadColumn(table, project, table.Columns.First(c => c.Name == "FileName")));
        Assert.IsFalse(AssetScreens.IsUploadColumn(table, Project(("AssetTables", "Nothing")), table.Columns.First(c => c.Name == "StorageKey")));
        Assert.AreEqual("image/*,application/pdf", AssetScreens.Accept(project));
        Assert.AreEqual("", AssetScreens.Accept(Project()));
    }

    [TestMethod]
    public void The_form_shows_the_columns_the_upload_owns_as_text_and_does_not_require_them()
    {
        var table = Asset();
        var form = FormModel.For(table, Project());
        var key = form.Field(table.Columns.First(c => c.Name == "StorageKey"));
        Assert.IsTrue(key.IsSystemStampField);
        Assert.IsFalse(key.IsRequired);
        Assert.IsFalse(form.Field(table.Columns.First(c => c.Name == "FileName")).IsSystemStampField);
    }

    [TestMethod]
    public void The_csharp_client_uploads_and_downloads_for_an_asset_table_only()
    {
        string withAsset = ApiClientCode.Client(Asset(), Project(), "Shop.Models", "Shop.Services");
        Assert.Contains("public async Task<Asset> UploadAsync(Stream content, string fileName, string? contentType)", withAsset);
        Assert.Contains("DownloadAsync(int id)", withAsset);
        string without = ApiClientCode.Client(Asset(), Project(("AssetTables", "Nothing")), "Shop.Models", "Shop.Services");
        Assert.DoesNotContain("UploadAsync", without);
    }

    [TestMethod]
    public void The_desktop_app_gets_the_store_and_the_rules_of_each_asset_table()
    {
        var project = Project(("AssetMaxBytes", "5000"), ("AssetAllowedTypes", "image/*"));
        string code = AssetDesktopCode.File(Shop(Asset()), project, "Shop.Repositories", "ShopContext", ["Shop.Entities"]);
        Assert.Contains("namespace Shop.Repositories;", code);
        Assert.Contains("public interface IAssetStore", code);
        Assert.Contains("public static class AssetAssets", code);
        Assert.Contains("public static async Task<string?> UploadAsync(ShopContext context, string path, string? contentType)", code);
        Assert.Contains("RETURNING", code);
        Assert.DoesNotContain("IResult", code, "no ASP.NET type reaches the desktop app");
        Assert.DoesNotContain("§", code);
        Assert.Contains("AssetAssets.PrepareAsync(_context, entity, _editing is null)", AssetDesktopCode.CheckLines(project, Asset(), "_context"));
        Assert.AreEqual("", AssetDesktopCode.CheckLines(Project(("AssetTables", "Nothing")), Asset(), "_context"));
    }

    [TestMethod]
    public void The_allowed_types_accept_a_whole_family_or_one_type()
    {
        string[] allowed = ["image/*", "application/pdf"];
        Assert.IsTrue(AssetPlan.Accepts(allowed, "image/png"));
        Assert.IsTrue(AssetPlan.Accepts(allowed, "application/pdf"));
        Assert.IsFalse(AssetPlan.Accepts(allowed, "text/html"));
        Assert.IsFalse(AssetPlan.Accepts(allowed, "application/pdf2"));
        Assert.IsTrue(AssetPlan.Accepts([], "anything/at-all"));
    }
}
