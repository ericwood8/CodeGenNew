using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The F# family: an immutable record per table with validate / ofStrings, and the Rop module they share. </summary>
[TestClass]
public class FSharpTemplateTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Product() => Sample.Table("Product",
    [
        Sample.Column("ProductId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 2),
        Sample.Column("Alias", SqlDbType.NVarChar, nullable: true, characters: 100, ordinal: 3),
        Sample.Column("Price", SqlDbType.Decimal, precision: 10, scale: 2, ordinal: 4, check: new CheckRange(0, true, null, false)),
        Sample.Column("Rating", SqlDbType.Int, nullable: true, ordinal: 5, check: new CheckRange(1, false, 5, false)),
        Sample.Column("Added", SqlDbType.DateTime, ordinal: 6),
        Sample.Column("IsActive", SqlDbType.Bit, ordinal: 7),
        Sample.Column("type", SqlDbType.VarChar, nullable: true, characters: 10, ordinal: 8),
        Sample.Column("Photo", SqlDbType.VarBinary, nullable: true, ordinal: 9)
    ]);

    private static async Task<string> Render(string template, TableModel table, ProjectSettings? project = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project ?? Project());
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    [TestMethod]
    public async Task A_record_has_one_field_per_column_and_an_option_for_each_that_can_be_null()
    {
        string fs = await Render("FS_Entity_v1.tt", Product());

        Expect.Contains(fs, "namespace Acme.Domain\n");
        Expect.Contains(fs, "type Product =\n    { ProductId: int\n      Name: string\n      Alias: string option\n      Price: decimal\n      Rating: int option\n      Added: DateTime\n      IsActive: bool\n      ``type``: string option\n      Photo: byte[] option }");
        Expect.Contains(fs, "[<RequireQualifiedAccess>]\nmodule Product =");
    }

    [TestMethod]
    public async Task Validate_reports_text_lengths_and_the_limits_the_database_states()
    {
        string fs = await Render("FS_Entity_v1.tt", Product());

        Expect.Contains(fs, "let validate (row: Product) : Result<Product, string list> =");
        Expect.Contains(fs, "if String.IsNullOrWhiteSpace row.Name then \"Name is required.\"");
        Expect.Contains(fs, "if not (isNull row.Name) && row.Name.Length > 50 then \"Name must be at most 50 characters.\"");
        Expect.Contains(fs, "match row.Alias with Some v when not (isNull v) && v.Length > 100 -> \"Alias must be at most 100 characters.\" | _ -> ()");
        Expect.Contains(fs, "if row.Price <= 0M then \"Price must be greater than 0.\"");                                  // CHECK (Price > 0)
        Expect.Contains(fs, "match row.Rating with Some v when v < 1 || v > 5 -> \"Rating must be between 1 and 5.\" | _ -> ()");   // CHECK (Rating BETWEEN 1 AND 5)
        Expect.Contains(fs, "if List.isEmpty problems then Ok row else Error problems");
    }

    [TestMethod]
    public async Task OfStrings_reads_every_column_by_name_and_applies_each_field_by_its_kind()
    {
        string fs = await Render("FS_Entity_v1.tt", Product());

        Expect.Contains(fs, "let ofStrings (fields: Map<string, string>) : Result<Product, string list> =");
        Expect.Contains(fs, "Ok(fun f1 f2 f3 f4 f5 f6 f7 f8 f9 ->");
        Expect.Contains(fs, "{ ProductId = f1; Name = f2; Alias = f3; Price = f4; Rating = f5; Added = f6; IsActive = f7; ``type`` = f8; Photo = f9 })");
        Expect.Contains(fs, "<*> Parse.orDefault \"ProductId\" Parse.int32 fields");   // an identity column is filled in by the database
        Expect.Contains(fs, "<*> Parse.required \"Name\" Parse.text fields");
        Expect.Contains(fs, "<*> Parse.optional \"Alias\" Parse.text fields");
        Expect.Contains(fs, "<*> Parse.required \"Price\" Parse.decimal fields");
        Expect.Contains(fs, "<*> Parse.optional \"Rating\" Parse.int32 fields");
        Expect.Contains(fs, "<*> Parse.required \"Added\" Parse.dateTime fields");
        Expect.Contains(fs, "<*> Parse.required \"IsActive\" Parse.boolean fields");
        Expect.Contains(fs, "<*> Parse.optional \"Photo\" Parse.base64 fields");
        Expect.Contains(fs, "|> Result.bind validate");
    }

    [TestMethod]
    public async Task The_namespace_follows_the_project()
    {
        Expect.Contains(await Render("FS_Entity_v1.tt", Product(), Project(("FSharpNamespace", "Shop.Core"))), "namespace Shop.Core\n");
        Expect.Contains(await Render("FS_Entity_v1.tt", Product(), ProjectSettings.None), "namespace MyApp.Domain\n");
    }

    [TestMethod]
    public async Task A_table_with_no_checks_and_only_numbers_validates_nothing()
    {
        var table = Sample.Table("Tag", [Sample.Column("TagId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("Hits", SqlDbType.BigInt, ordinal: 2)]);

        string fs = await Render("FS_Entity_v1.tt", table);

        Expect.Contains(fs, "let validate (row: Tag) : Result<Tag, string list> =\n        Ok row");
        Expect.Contains(fs, "<*> Parse.required \"Hits\" Parse.int64 fields");
    }

    [TestMethod]
    public async Task The_rop_module_is_written_once_with_the_projects_namespace()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("FS_Rop_v1.tt"), Project(("FSharpNamespace", "Shop.Core")));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        string fs = result.GeneratedText!.Replace("\r\n", "\n");

        Expect.Contains(fs, "@@@FILE Rop.fs@@@");
        Expect.Contains(fs, "namespace Shop.Core");
        Expect.Contains(fs, "let (<*>) f x = apply f x");
        Expect.Contains(fs, "let required (name: string)");
        Expect.Contains(fs, "let optional (name: string)");
        Expect.Contains(fs, "let orDefault (name: string)");
        Expect.Contains(fs, "let base64 (name: string)");
    }

    [TestMethod]
    public void The_family_is_offered_under_its_own_group_and_writes_fs_files()
    {
        var offered = TemplateCatalog.Discover(Repo.TemplatesDirectory).Where(t => t.SubmenuGroup == "FS").ToList();

        CollectionAssert.AreEquivalent(new[] { "FS_Entity", "FS_Rop" }, offered.Select(t => t.Name).ToArray());
        Assert.AreEqual("Product.fs", offered.Single(t => t.Name == "FS_Entity").BuildFileName("Product"));
    }
}
