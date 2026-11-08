namespace CodeGenNew.Core;

/// <summary> <c>tests/test_assets.py</c> of the Python API (PY_TestAssets), beside the <c>conftest.py</c> the approval tests share: an upload, a download and a delete, the refusals (no file, too large, a type that is
/// not allowed), and the storage key that an ordinary save cannot name. The files go to a temporary folder, not the API's own store. The words are <see cref="AssetWords"/>, as in the other stacks. </summary>
public static class PythonAssetTests
{
    /// <summary> Whether the tests are written: <c>PythonTests</c>, an asset table the plan accepts, over a database the shared conftest can open (not SQL Server). </summary>
    public static bool Applies(DatabaseModel database, ProjectSettings project) =>
        project.PythonTests && DialectInfo.For(database.Dialect).RunsPythonTests && PythonAssetWriter.Shapes(database, project).Count > 0;

    public static string Conftest(ProjectSettings project, SqlDialect dialect) => PythonApprovalTests.Conftest(project, dialect);

    public static string File(DatabaseModel database, ProjectSettings project)
    {
        var asset = PythonAssetWriter.Shapes(database, project)[0];
        var main = PythonTable.Of(asset.Table, project);
        string Json(ColumnModel c) => main.FieldOf(c).Json;
        var allowed = AssetPlan.AllowedTypes(project);
        string sampleType = allowed.Length == 0 ? "text/plain" : allowed[0].Replace("/*", "/x-sample");
        string auth = project.ApiAuth ? "from app.auth import CurrentUser, require_user\n" : "";
        string signIn = project.ApiAuth ? "    app.dependency_overrides[require_user] = lambda: CurrentUser(name=\"zz_uploader\", role=None, owner_id=None)\n" : "";
        string signOut = project.ApiAuth ? "    app.dependency_overrides.pop(require_user, None)\n" : "";
        string tooLarge = project.AssetMaxBytes > 2_000_000 ? "" : """"

def test_a_file_over_the_limit_is_refused(api):
    client, _ = api
    big = client.post(ROUTE + "/upload", files={"file": ("big.bin", b"x" * (§MAX + 1), "§TYPE")})
    assert big.status_code == 413, big.text
    assert big.json()["type"] == "§T_LARGE"
"""".Replace("§MAX", project.AssetMaxBytes.ToString()).Replace("§TYPE", sampleType).Replace("§T_LARGE", AssetWords.TooLargeType);
        string notAllowed = allowed.Length == 0 ? "" : """"

def test_a_type_that_is_not_allowed_is_refused(api):
    client, _ = api
    wrong = client.post(ROUTE + "/upload", files={"file": ("x.bin", b"abc", "application/x-never-allowed")})
    assert wrong.status_code == 415, wrong.text
    assert wrong.json()["type"] == "§T_TYPE"
"""".Replace("§T_TYPE", AssetWords.TypeNotAllowedType);
        string anonymous = project.ApiAuth ? """"

def test_a_download_needs_a_sign_in(api):
    client, _ = api
    uploaded = client.post(ROUTE + "/upload", files={"file": ("a.txt", b"abc", "§TYPE")}).json()
    try:
        client.app.dependency_overrides.pop(require_user, None)
        assert client.get(f"{ROUTE}/{uploaded['§ID']}/download").status_code == 401
    finally:
        client.app.dependency_overrides[require_user] = lambda: CurrentUser(name="zz_uploader", role=None, owner_id=None)
        client.delete(f"{ROUTE}/{uploaded['§ID']}")
"""".Replace("§TYPE", sampleType).Replace("§ID", Json(asset.Key)) : "";
        // the other columns the table cannot leave empty: the ordinary create asks for them even when the database has a default
        var fixedColumns = new[] { asset.Key, asset.StorageKey, asset.FileName, asset.MimeType, asset.Size }.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string others = string.Concat(asset.Table.Columns.Where(c => !fixedColumns.Contains(c.Name) && !c.IsNullable && !c.IsIdentity && !c.IsComputed && main.Fields.Any(f => f.Column == c))
            .Select(c => $", {PythonNames.Str(main.FieldOf(c).Json)}: {c.ValidExample(project, asset.Table.TableName)}"));
        return TemplateText.FillAll(Text,
            ("§OTHERS", others),
            ("§AUTHIMPORT\n", auth), ("§SIGNIN", signIn), ("§SIGNOUT", signOut), ("§ROUTE", PythonNames.Str(main.Route)), ("§TYPE", sampleType),
            ("§ID", Json(asset.Key)), ("§NAME", Json(asset.FileName)), ("§MIME", Json(asset.MimeType)), ("§SIZE", Json(asset.Size)), ("§KEY", Json(asset.StorageKey)),
            ("§UNATTACHED", AssetWords.UnattachedPrefix), ("§T_NOFILE", AssetWords.NoFileType), ("§T_MISSING", AssetWords.FileMissingType),
            ("§TOOLARGE", tooLarge), ("§NOTALLOWED", notAllowed), ("§ANONYMOUS", anonymous)) + "\n";
    }

    private const string Text = """"
"""The asset routes: an upload, a download and a delete, the refusals, and a storage key an ordinary save cannot name. Written by PY_TestAssets; regenerated with the tables."""
import pytest
from fastapi.testclient import TestClient

§AUTHIMPORT
from app.routes import asset_files

from conftest import run

ROUTE = §ROUTE


@pytest.fixture
def api(application, tmp_path):
    """A client over a scratch database, with the files kept in a temporary folder."""
    app, engine = application
    previous = asset_files.store
    asset_files.use_store(asset_files.LocalFolderAssetStore(str(tmp_path)))
§SIGNIN    client = TestClient(app)
    yield client, engine
§SIGNOUT    asset_files.use_store(previous)


def test_an_uploaded_file_can_be_downloaded_and_a_delete_removes_it(api):
    client, _ = api
    content = b"asset sample"
    uploaded = client.post(ROUTE + "/upload", files={"file": ("hello.txt", content, "§TYPE")})
    assert uploaded.status_code == 201, uploaded.text
    row = uploaded.json()
    assert row["§NAME"] == "hello.txt" and row["§SIZE"] == len(content) and row["§MIME"] == "§TYPE"
    key = row["§KEY"]
    assert not key.startswith("§UNATTACHED") and "/" not in key and "\\" not in key, "the server made the key"
    assert uploaded.headers["Location"].endswith(f"/{row['§ID']}")
    address = f"{ROUTE}/{row['§ID']}"
    download = client.get(address + "/download")
    assert download.status_code == 200 and download.content == content
    assert "hello.txt" in download.headers["content-disposition"] and download.headers["x-content-type-options"] == "nosniff"
    assert asset_files.store.open(key) is not None
    deleted = client.delete(address)
    assert deleted.status_code in (200, 204), deleted.text
    assert client.get(address + "/download").status_code == 404
    assert asset_files.store.open(key) is None, "the delete removed the file"


def test_an_upload_without_a_file_is_refused(api):
    client, _ = api
    none = client.post(ROUTE + "/upload", data={"note": "x"})
    assert none.status_code == 400, none.text
    assert none.json()["type"] == "§T_NOFILE"
    empty = client.post(ROUTE + "/upload", files={"file": ("empty.txt", b"", "§TYPE")})
    assert empty.status_code == 400, empty.text
§TOOLARGE§NOTALLOWED

def test_the_storage_key_cannot_be_named_by_a_save_and_a_row_with_no_upload_has_no_file(api):
    client, engine = api
    uploaded = client.post(ROUTE + "/upload", files={"file": ("k.txt", b"abc", "§TYPE")}).json()
    address = f"{ROUTE}/{uploaded['§ID']}"
    try:
        body = {**uploaded, "§KEY": "other", "§SIZE": 99, "§MIME": "x/y", "§NAME": "renamed.txt"}
        edited = client.put(address, json=body)
        assert edited.status_code == 200, edited.text
        stored = client.get(address).json()
        assert stored["§KEY"] == uploaded["§KEY"], "a save keeps the stored key"
        assert stored["§SIZE"] == 3 and stored["§MIME"] == "§TYPE"
        assert stored["§NAME"] == "renamed.txt", "the name may be changed"
    finally:
        client.delete(address)
    bare = client.post(ROUTE, json={"§NAME": "bare.txt", "§MIME": "text/plain", "§SIZE": 1, "§KEY": "../../escape"§OTHERS})
    assert bare.status_code == 201, bare.text
    made = bare.json()
    try:
        assert made["§KEY"].startswith("§UNATTACHED"), "a row made without an upload holds an unattached key"
        missing = client.get(f"{ROUTE}/{made['§ID']}/download")
        assert missing.status_code == 404 and missing.json()["type"] == "§T_MISSING"
    finally:
        client.delete(f"{ROUTE}/{made['§ID']}")
§ANONYMOUS
"""";
}
