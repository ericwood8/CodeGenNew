using System.Data;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The suggestions that need the whole database or a second table, and the table shapes of content, files, inbound events, tokens and usage. </summary>
[TestClass]
public class DatabaseShapeDetectorTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef", ["ApiAuth"] = "true" }.Concat(values.Select(v => KeyValuePair.Create(v.Key, v.Value))).ToDictionary());

    private static ColumnModel Key(string name) => Sample.Column(name, SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1);

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Shop", SchemaName = "dbo", Tables = [.. tables] };

    private static ShapeSuggestion? Find(IReadOnlyList<DatabaseShapeDetection> found, string shape) => found.FirstOrDefault(d => d.Suggestion.Shape == shape)?.Suggestion;

    private static TableModel Post(bool uniqueSlug = true) => Sample.Table("Post",
        [Key("PostId"), Sample.Column("Title", SqlDbType.VarChar, characters: 120, ordinal: 2), Sample.Column("Slug", SqlDbType.VarChar, characters: 120, ordinal: 3), Sample.Column("Status", SqlDbType.VarChar, characters: 20, ordinal: 4)],
        indexes: uniqueSlug ? [new IndexModel("UX_Post_Slug", true, ["Slug"])] : []);

    [TestMethod]
    public void A_title_and_a_unique_slug_with_a_status_is_content()
    {
        var content = Find(DatabaseShapeDetector.Of(Database(Post()), Project()), "Content with a slug")!;
        Assert.IsTrue(content.Fits);
        Assert.AreEqual("Post", content.Item);
    }

    [TestMethod]
    public void A_slug_without_a_unique_index_is_short_of_content()
    {
        var content = Find(DatabaseShapeDetector.Of(Database(Post(uniqueSlug: false)), Project()), "Content with a slug")!;
        Assert.IsFalse(content.Fits);
        StringAssert.Contains(content.Note, "no unique index");
    }

    [TestMethod]
    public void Storage_key_file_name_type_and_size_are_stored_file_metadata()
    {
        var table = Sample.Table("Document", [Key("DocumentId"), Sample.Column("StorageKey", SqlDbType.VarChar, characters: 200, ordinal: 2), Sample.Column("FileName", SqlDbType.VarChar, characters: 200, ordinal: 3),
            Sample.Column("MimeType", SqlDbType.VarChar, characters: 100, ordinal: 4), Sample.Column("SizeBytes", SqlDbType.BigInt, ordinal: 5)]);
        var asset = Find(DatabaseShapeDetector.Of(Database(table), Project()), "Stored file metadata")!;
        Assert.IsTrue(asset.Fits);
        Assert.AreEqual("Document:StorageKey:FileName:MimeType:SizeBytes", asset.Item);
    }

    [TestMethod]
    public void Three_of_the_four_file_columns_only_look_like_stored_files()
    {
        var table = Sample.Table("Document", [Key("DocumentId"), Sample.Column("StorageKey", SqlDbType.VarChar, characters: 200, ordinal: 2), Sample.Column("MimeType", SqlDbType.VarChar, characters: 100, ordinal: 3),
            Sample.Column("FileName", SqlDbType.VarChar, characters: 100, ordinal: 4)]);
        var asset = Find(DatabaseShapeDetector.Of(Database(table), Project()), "Stored file metadata")!;
        Assert.IsFalse(asset.Fits);
        StringAssert.Contains(asset.Note, "no size");
    }

    [TestMethod]
    public void A_body_a_received_stamp_and_a_processed_stamp_is_an_inbound_table_unless_a_claim_stamp_makes_it_a_queue()
    {
        TableModel Hook(bool claim) => Sample.Table("WebhookEvent", [Key("WebhookEventId"), Sample.Column("Payload", SqlDbType.VarChar, characters: 4000, ordinal: 2),
            Sample.Column("ReceivedOn", SqlDbType.DateTime, ordinal: 3), Sample.Column("ProcessedOn", SqlDbType.DateTime, nullable: true, ordinal: 4),
            .. claim ? new[] { Sample.Column("ClaimedOn", SqlDbType.DateTime, nullable: true, ordinal: 5) } : []]);
        Assert.IsNotNull(Find(DatabaseShapeDetector.Of(Database(Hook(false)), Project()), "Inbound event table"));
        Assert.IsNull(Find(DatabaseShapeDetector.Of(Database(Hook(true)), Project()), "Inbound event table"));
    }

    [TestMethod]
    public void A_long_hash_and_a_required_expiry_is_a_token_table_and_needs_ApiAuth()
    {
        var table = Sample.Table("Session", [Key("SessionId"), Sample.Column("TokenHash", SqlDbType.VarChar, characters: 64, ordinal: 2), Sample.Column("ExpiresOn", SqlDbType.DateTime, ordinal: 3)]);
        var token = Find(DatabaseShapeDetector.Of(Database(table), Project()), "Session and token table")!;
        Assert.IsTrue(token.Fits);
        var withoutAuth = Find(DatabaseShapeDetector.Of(Database(table), ProjectSettings.FromValues(new Dictionary<string, string> { ["ProjectName"] = "Shop", ["AccessMode"] = "Ef" })), "Session and token table")!;
        Assert.IsFalse(withoutAuth.Fits);
        StringAssert.Contains(withoutAuth.Note, "ApiAuth");
    }

    [TestMethod]
    public void A_password_hash_is_not_a_token()
    {
        var table = Sample.Table("Employee", [Key("EmployeeId"), Sample.Column("PasswordHash", SqlDbType.VarChar, characters: 100, ordinal: 2), Sample.Column("ExpiresOn", SqlDbType.DateTime, ordinal: 3)]);
        Assert.IsNull(Find(DatabaseShapeDetector.Of(Database(table), Project()), "Session and token table"));
    }

    [TestMethod]
    public void A_required_kind_quantity_and_occurred_stamp_is_a_usage_ledger()
    {
        var table = Sample.Table("UsageEvent", [Key("UsageEventId"), Sample.Column("Kind", SqlDbType.VarChar, characters: 40, ordinal: 2), Sample.Column("Quantity", SqlDbType.Int, ordinal: 3), Sample.Column("OccurredOn", SqlDbType.DateTime, ordinal: 4)]);
        var usage = Find(DatabaseShapeDetector.Of(Database(table), Project()), "Usage ledger")!;
        Assert.AreEqual("UsageEvent:Kind:Quantity:OccurredOn", usage.Item);
    }

    [TestMethod]
    public void A_type_and_an_id_with_a_check_constraint_naming_tables_is_a_polymorphic_link()
    {
        var note = Sample.Table("Note", [Key("NoteId"), Sample.Column("OwnerType", SqlDbType.VarChar, characters: 20, ordinal: 2), Sample.Column("OwnerId", SqlDbType.Int, ordinal: 3), Sample.Column("Body", SqlDbType.VarChar, characters: 200, ordinal: 4)],
            checks: [new CheckConstraintModel("CK_Note_OwnerType", "CHECK ([OwnerType] IN ('Customer','Item'))")]);
        var found = Find(DatabaseShapeDetector.Of(Database(note, Sample.Table("Customer", [Key("CustomerId")]), Sample.Table("Item", [Key("ItemId")])), Project()), "Polymorphic link")!;
        Assert.IsTrue(found.Fits);
        Assert.AreEqual("Note:OwnerType:OwnerId:Customer|Item", found.Item);
    }

    [TestMethod]
    public void A_type_and_an_id_with_no_named_tables_only_looks_like_a_link()
    {
        var note = Sample.Table("Note", [Key("NoteId"), Sample.Column("EntityType", SqlDbType.VarChar, characters: 20, ordinal: 2), Sample.Column("EntityId", SqlDbType.Int, ordinal: 3)]);
        var found = Find(DatabaseShapeDetector.Of(Database(note), Project()), "Polymorphic link")!;
        Assert.IsFalse(found.Fits);
        StringAssert.Contains(found.Note, "no check constraint");
    }

    [TestMethod]
    public void An_id_with_a_foreign_key_is_not_a_polymorphic_link()
    {
        var note = Sample.Table("Note", [Key("NoteId"), Sample.Column("OwnerType", SqlDbType.VarChar, characters: 20, ordinal: 2), Sample.Column("OwnerId", SqlDbType.Int, ordinal: 3)], [Sample.ForeignKey("OwnerId", "Customer", "CustomerId")]);
        Assert.IsNull(Find(DatabaseShapeDetector.Of(Database(note), Project()), "Polymorphic link"));
    }

    [TestMethod]
    public void Copies_of_the_parents_columns_beside_a_quantity_look_like_a_snapshot_and_are_never_applied()
    {
        var product = Sample.Table("Product", [Key("ProductId"), Sample.Column("Name", SqlDbType.VarChar, characters: 60, ordinal: 2), Sample.Column("Price", SqlDbType.Decimal, ordinal: 3)]);
        var line = Sample.Table("OrderLine", [Key("OrderLineId"), Sample.Column("ProductId", SqlDbType.Int, ordinal: 2), Sample.Column("ProductName", SqlDbType.VarChar, characters: 60, ordinal: 3),
            Sample.Column("UnitPrice", SqlDbType.Decimal, ordinal: 4), Sample.Column("Quantity", SqlDbType.Int, ordinal: 5)], [Sample.ForeignKey("ProductId", "Product", "ProductId")]);
        var snapshot = Find(DatabaseShapeDetector.Of(Database(product, line), Project()), "Snapshot at checkout")!;
        Assert.IsFalse(snapshot.CanBeApplied);
        Assert.AreEqual("OrderLine:ProductName<-Product.Name+UnitPrice<-Product.Price", snapshot.Item);
    }

    [TestMethod]
    public void A_table_with_an_archive_beside_it_is_offered_the_archive_script_with_the_days_left_to_the_person()
    {
        var order = Sample.Table("Order", [Key("OrderId"), Sample.Column("PlacedOn", SqlDbType.DateTime, ordinal: 2)]);
        var archive = Sample.Table("OrderArchive", [Sample.Column("OrderId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("PlacedOn", SqlDbType.DateTime, ordinal: 2)]);
        var found = Find(DatabaseShapeDetector.Of(Database(order, archive), Project()), "Archive")!;
        Assert.IsFalse(found.Fits);
        StringAssert.Contains(found.Note, "choose the days");
    }

    [TestMethod]
    public void A_tenant_column_in_most_tables_names_the_tenant_table()
    {
        var tenant = Sample.Table("Tenant", [Key("TenantId")]);
        TableModel Owned(string name) => Sample.Table(name, [Key(name + "Id"), Sample.Column("TenantId", SqlDbType.Int, ordinal: 2)], [Sample.ForeignKey("TenantId", "Tenant", "TenantId")]);
        var found = Find(DatabaseShapeDetector.Of(Database(tenant, Owned("Customer"), Owned("Item")), Project()), "Tenant table")!;
        Assert.AreEqual("Tenant", found.Item);
        Assert.IsTrue(found.Fits);
        Assert.IsNull(Find(DatabaseShapeDetector.Of(Database(tenant, Owned("Customer"), Owned("Item")), Project(("TenantTable", "Tenant"))), "Tenant table"));
    }

    [TestMethod]
    public void A_login_and_a_password_hash_make_the_user_table()
    {
        var employee = Sample.Table("Employee", [Key("EmployeeId"), Sample.Column("UserName", SqlDbType.VarChar, characters: 40, ordinal: 2), Sample.Column("PasswordHash", SqlDbType.VarChar, characters: 100, ordinal: 3)]);
        Assert.AreEqual("Employee", Find(DatabaseShapeDetector.Of(Database(employee), Project()), "User table")!.Item);
    }

    [TestMethod]
    public void Role_permission_and_two_junctions_are_the_permission_set()
    {
        TableModel Junction(string name, string a, string b) => Sample.Table(name, [Sample.Column(a + "Id", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column(b + "Id", SqlDbType.Int, primaryKey: true, ordinal: 2)],
            [Sample.ForeignKey(a + "Id", a, a + "Id"), Sample.ForeignKey(b + "Id", b, b + "Id")]);
        var found = Find(DatabaseShapeDetector.Of(Database(Sample.Table("Role", [Key("RoleId")]), Sample.Table("Permission", [Key("PermissionId")]), Junction("RolePermission", "Role", "Permission"), Junction("UserRole", "Employee", "Role")),
            Project(("UserTable", "Employee"))), "Roles and permissions")!;
        Assert.AreEqual("Role,Permission,RolePermission,UserRole", found.Item);
        Assert.IsTrue(found.Fits);
    }

    [TestMethod]
    public void A_flag_table_and_an_override_table_are_feature_flags()
    {
        var feature = Sample.Table("Feature", [Key("FeatureId"), Sample.Column("Name", SqlDbType.VarChar, characters: 40, ordinal: 2), Sample.Column("DefaultEnabled", SqlDbType.Bit, ordinal: 3)]);
        var overrides = Sample.Table("FeatureOverride", [Key("FeatureOverrideId"), Sample.Column("FeatureId", SqlDbType.Int, ordinal: 2), Sample.Column("IsEnabled", SqlDbType.Bit, ordinal: 3)], [Sample.ForeignKey("FeatureId", "Feature", "FeatureId")]);
        Assert.AreEqual("Feature,FeatureOverride", Find(DatabaseShapeDetector.Of(Database(feature, overrides), Project()), "Feature flags")!.Item);
    }

    [TestMethod]
    public async Task The_menu_suggests_a_shape_from_a_read_database_and_stops_once_the_project_names_it()
    {
        string database = Path.Combine(Path.GetTempPath(), "codegen_menu_" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Folder (FolderId INTEGER PRIMARY KEY, Title VARCHAR(40) NOT NULL, Note VARCHAR(100) NULL, ParentId INTEGER NULL REFERENCES Folder(FolderId))";
                command.ExecuteNonQuery();
            }
            var request = new CodeGenNew.Connections.ConnectionRequest { Provider = CodeGenNew.Connections.DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = CodeGenNew.SchemaIntrospection.SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var model = await CodeGenNew.SchemaIntrospection.DatabaseModelBuilder.BuildAsync(provider, database, "main");

            string file = "ProjectName=Shop\nDatabaseProvider=Sqlite\nStacks=Api\nAccessMode=Ef\n";
            var tree = CodeGenNew.Generation.ShapeMenu.Suggestions(model, "Folder", ProjectSettings.Parse(file)).Single(s => s.Shape == "Tree");
            Assert.IsTrue(tree.Fits, tree.Note);

            string changed = CodeGenNew.Generation.ShapeMenu.Apply(file, tree);
            StringAssert.Contains(changed, "TreeTables=Folder");
            StringAssert.Contains(changed, "Stacks=Api");
            Assert.IsFalse(CodeGenNew.Generation.ShapeMenu.Suggestions(model, "Folder", ProjectSettings.Parse(changed)).Any(s => s.Shape == "Tree"));
            Assert.AreEqual(0, CodeGenNew.Generation.ShapeMenu.Suggestions(model, "NoSuchTable", ProjectSettings.Parse(file)).Count);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
        }
    }

    [TestMethod]
    public async Task A_suggestion_added_to_the_project_changes_what_a_regenerate_writes()
    {
        string database = Path.Combine(Path.GetTempPath(), "codegen_shape_" + Guid.NewGuid().ToString("N") + ".db");
        string output = Path.Combine(Path.GetTempPath(), "codegen_shape_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "CREATE TABLE Category (CategoryId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL, IsActive BIT NOT NULL DEFAULT 1, ParentId INTEGER NULL REFERENCES Category(CategoryId)); CREATE TABLE Other (OtherId INTEGER PRIMARY KEY, Name VARCHAR(40) NOT NULL)";
                command.ExecuteNonQuery();
            }
            var request = new CodeGenNew.Connections.ConnectionRequest { Provider = CodeGenNew.Connections.DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = CodeGenNew.SchemaIntrospection.SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var values = new Dictionary<string, string> { ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Api", ["AccessMode"] = "Ef" };
            async Task<string> Run(string text, string[]? tables = null)
            {
                if (Directory.Exists(output))
                    Directory.Delete(output, recursive: true);
                var project = ProjectSettings.FromValues(text.Length == 0 ? values : new Dictionary<string, string>(values) { ["TreeTables"] = text });
                var report = await Repo.GenerateAsync(provider, new CodeGenNew.Generation.GenerateOptions
                    { Project = project, Stacks = ["Api"], OutputDirectory = output, DatabaseName = database, Schema = "main", Tables = tables, WithDatabaseTemplates = tables is not null });
                Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
                return string.Join("\n", Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(f => "FILE:" + Path.GetRelativePath(output, f) + "\n" + File.ReadAllText(f)));
            }
            string without = await Run("");
            string with = await Run("Category");
            Assert.IsFalse(without.Contains("promote-children"));
            StringAssert.Contains(with, "promote-children");
            StringAssert.Contains(with, "Other");
            string quick = await Run("Category", ["Category"]);
            StringAssert.Contains(quick, "promote-children");
            Assert.IsTrue(with.Split('\n').Any(l => l.StartsWith("FILE:") && l.Contains("Other")), "the whole run writes files for the other table");
            Assert.IsFalse(quick.Split('\n').Any(l => l.StartsWith("FILE:") && l.Contains("Other")), "a run for one table does not write the other table's files");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, recursive: true);
        }
    }
}
