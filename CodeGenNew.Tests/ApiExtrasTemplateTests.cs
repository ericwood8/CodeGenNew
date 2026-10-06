using System.Data;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.TemplateEngine;
using Microsoft.OpenApi.Readers;

namespace CodeGenNew.Tests;

/// <summary> The request files, the fake-data generators, the settings that add them to a plan, the validation endpoint filter, the Swagger page, and the descriptions the database holds. </summary>
[TestClass]
public class ApiExtrasTemplateTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Customer() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerStatusId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 3, description: "The name on the account."),
        Sample.Column("BillingEmail", SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 4),
        Sample.Column("CreditLimit", SqlDbType.Decimal, nullable: true, precision: 10, scale: 2, ordinal: 5, check: new CheckRange(0, false, 5000, false)),
        Sample.Column("Rating", SqlDbType.Int, nullable: true, ordinal: 6, check: new CheckRange(1, false, 5, false)),
        Sample.Column("Tier", SqlDbType.VarChar, characters: 10, ordinal: 7, choices: ["Gold", "Silver", "Bronze"]),
        Sample.Column("Weight", SqlDbType.Decimal, precision: 8, scale: 2, ordinal: 8, check: new CheckRange(0, true, null, false)),
        Sample.Column("Added", SqlDbType.DateTime, ordinal: 9, createDateColumn: true),
        Sample.Column("Phone", SqlDbType.VarChar, nullable: true, characters: 20, ordinal: 10),
        Sample.Column("Year", SqlDbType.Int, nullable: true, ordinal: 11, numericKind: NumericKind.Year)
    ],
    [new ForeignKeyModel { ConstraintName = "FK_s", ReferencingColumns = ["CustomerStatusId"], ReferencedSchema = "dbo", ReferencedTable = "CustomerStatus", ReferencedColumns = ["CustomerStatusId"] }]);

    private static async Task<string> Render(string template, TableModel table, ProjectSettings? project = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project ?? Project());
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    // ------------------------------------------------------------------ API_Http

    [TestMethod]
    public async Task The_request_file_has_every_request_with_bodies_shaped_by_the_columns()
    {
        string http = await Render("API_Http_v1.tt", Customer(), Project(("ApiPort", "5123")));

        Expect.Contains(http, "@host = http://localhost:5123\n@id = 1\n");
        Expect.Contains(http, "### All Customers\nGET {{host}}/api/customers\nAccept: application/json");
        Expect.Contains(http, "GET {{host}}/api/customers/{{id}}");
        Expect.Contains(http, "GET {{host}}/api/customers/search?name=&billingEmail=&pageNumber=1&pageSize=20&sortBy=name&sortDir=asc");
        Expect.Contains(http, "POST {{host}}/api/customers\nContent-Type: application/json\n\n{\n  \"customerId\": 0,\n  \"customerStatusId\": 1,\n  \"name\": \"Sample name\",\n  \"tier\": \"Gold\",\n  \"weight\": 1,");
        Expect.Contains(http, "PUT {{host}}/api/customers/{{id}}\nContent-Type: application/json\n\n{\n  \"customerId\": {{id}},");
        Expect.Contains(http, "DELETE {{host}}/api/customers/{{id}}");
        Expect.DoesNotContain(http, "creditLimit");   // a nullable column is left out
        Expect.DoesNotContain(http, "billingEmail\":");
    }

    [TestMethod]
    public async Task A_check_range_and_a_strict_bound_shape_the_example_numbers()
    {
        var table = Sample.Table("Product",
        [
            Sample.Column("ProductId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("Price", SqlDbType.Decimal, precision: 10, scale: 2, ordinal: 2, check: new CheckRange(10, true, 100, false)),
            Sample.Column("Stars", SqlDbType.Int, ordinal: 3, check: new CheckRange(3, false, 5, false)),
            Sample.Column("Contact", SqlDbType.VarChar, characters: 40, ordinal: 4, nullable: false),
            Sample.Column("ContactEmail", SqlDbType.VarChar, characters: 40, ordinal: 5)
        ]);

        string http = await Render("API_Http_v1.tt", table);

        Expect.Contains(http, "\"price\": 11,");     // > 10: the next whole step
        Expect.Contains(http, "\"stars\": 3,");      // the CHECK minimum
        Expect.Contains(http, "\"contactEmail\": \"someone@example.com\"");
    }

    [TestMethod]
    public async Task The_request_file_is_refused_for_a_table_with_no_api()
    {
        var result = await Repo.Cache.RunAsync(Repo.Template("API_Http_v1.tt"), Sample.CompositeKey(), Project());

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("API_Crud")));
    }

    // ------------------------------------------------------------------ CS_Faker

    [TestMethod]
    public async Task The_faker_has_a_rule_per_column_the_schema_describes_and_none_for_the_database_s_own()
    {
        string cs = await Render("CS_Faker_v1.tt", Customer(), Project(("EntityNamespace", "Acme.Api.Entities"), ("FakerNamespace", "Acme.Api.Fakers")));

        Expect.Contains(cs, "using Bogus;\nusing Bogus.Extensions;\nusing Acme.Api.Entities;");
        Expect.Contains(cs, "namespace Acme.Api.Fakers;");
        Expect.Contains(cs, "public static class CustomerFaker");
        Expect.Contains(cs, "public static Faker<Customer> Create(int? seed = null, int maxForeignKey = 10)");
        Expect.Contains(cs, "faker.UseSeed(seed.Value);");
        Expect.Contains(cs, ".RuleFor(x => x.CustomerStatusId, f => f.Random.Int(1, maxForeignKey))");
        Expect.Contains(cs, ".RuleFor(x => x.Name, f => f.Company.CompanyName().ClampLength(max: 50))");
        Expect.Contains(cs, ".RuleFor(x => x.BillingEmail, f => f.Random.Bool(0.8f) ? f.Internet.Email().ClampLength(max: 100) : null)");
        Expect.Contains(cs, ".RuleFor(x => x.CreditLimit, f => f.Random.Bool(0.8f) ? Math.Round(f.Random.Decimal(0m, 5000m), 2) : null)");
        Expect.Contains(cs, ".RuleFor(x => x.Rating, f => f.Random.Bool(0.8f) ? f.Random.Int(1, 5) : null)");
        Expect.Contains(cs, ".RuleFor(x => x.Weight, f => Math.Round(f.Random.Decimal(0.01m, 10000m), 2))");   // a strict bound: just above 0
        Expect.Contains(cs, "private static readonly string[] TierValues = [\"Gold\", \"Silver\", \"Bronze\"];");
        Expect.Contains(cs, ".RuleFor(x => x.Tier, f => f.PickRandom(TierValues))");
        Expect.Contains(cs, "f.Phone.PhoneNumber().ClampLength(max: 20)");
        Expect.Contains(cs, "f.Random.Int(2000, 2100)");   // a year column: the project's year range
        Expect.DoesNotContain(cs, "x.CustomerId,");        // an identity key
        Expect.DoesNotContain(cs, "x.Added");              // a create-date column
        Expect.Contains(cs, "public static List<Customer> Generate(int count, int? seed = null, int maxForeignKey = 10)");
    }

    [TestMethod]
    public async Task A_key_that_is_not_an_identity_is_numbered_from_one()
    {
        var table = Sample.Table("Code", [Sample.Column("CodeId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("Label", SqlDbType.VarChar, characters: 20, ordinal: 2)]);

        Expect.Contains(await Render("CS_Faker_v1.tt", table), ".RuleFor(x => x.CodeId, f => f.IndexFaker + 1)");
    }

    [TestMethod]
    public async Task The_faker_needs_a_key_because_the_entity_does()
    {
        var keyless = Sample.Table("Log", [Sample.Column("Text", SqlDbType.NVarChar, characters: 50, ordinal: 1)]);

        var result = await Repo.Cache.RunAsync(Repo.Template("CS_Faker_v1.tt"), keyless, Project());

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("primary key")));
    }

    // ------------------------------------------------------------------ descriptions

    [TestMethod]
    public async Task A_description_from_the_database_reaches_the_data_dictionary_and_the_openapi_document()
    {
        var customer = Customer();
        var described = new TableModel
        {
            SchemaName = customer.SchemaName, TableName = customer.TableName, QuotedName = customer.QuotedName, Columns = customer.Columns, PrimaryKeyColumns = customer.PrimaryKeyColumns,
            ForeignKeys = [], ChildForeignKeys = [], Description = "Somebody who buys."
        };

        string md = await Render("MD_DataDictionary_v1.tt", described);
        Expect.Contains(md, "Table `dbo.Customer`: 11 columns, primary key `CustomerId`.\n\nSomebody who buys.\n\n## Columns");
        Expect.Contains(md, "| Notes | Description |\n");
        Expect.Contains(md, "| 3 | `Name` | nvarchar(50) | no |  |  | at most 50 characters |  | The name on the account. |");

        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [described] };
        var result = await Repo.Cache.RunAsync(Repo.Template("API_OpenApi_v1.tt"), database, Project());
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        var document = new OpenApiStringReader().Read(result.GeneratedText!.Replace("\r\n", "\n").Replace("@@@FILE openapi.yaml@@@\n", ""), out var diagnostic);
        Assert.AreEqual(0, diagnostic.Errors.Count);
        Assert.AreEqual("Somebody who buys.", document.Components.Schemas["Customer"].Description);
        Assert.AreEqual("The name on the account.", document.Components.Schemas["Customer"].Properties["name"].Description);
    }

    [TestMethod]
    public async Task A_table_with_no_description_keeps_the_page_as_it_was()
    {
        var plain = Sample.Table("Flag", [Sample.Column("FlagId", SqlDbType.Int, primaryKey: true, ordinal: 1)]);

        string md = await Render("MD_DataDictionary_v1.tt", plain);

        Expect.DoesNotContain(md, "Description");
        Expect.Contains(md, "| # | Column | Type | Null | Default | Key | Limits | Notes |\n|---|---|---|---|---|---|---|---|");
    }

    // ------------------------------------------------------------------ the settings that add templates to a plan

    [TestMethod]
    public void The_flags_name_the_templates_they_add()
    {
        CollectionAssert.AreEqual(Array.Empty<string>(), Project().ImpliedPlanTemplates.ToArray());
        CollectionAssert.AreEquivalent(new[] { "API_OpenApi", "API_Http", "CS_Faker", "MD_DataDictionary", "MD_Erd", "CS_Validator", "RS_Validate", "PY_Validate" },
            Project(("ApiDocs", "true"), ("ApiHttp", "TRUE"), ("ApiFakers", "1"), ("ProjectDocs", "yes"), ("ApiValidation", "true"), ("ApiProduction", "true")).ImpliedPlanTemplates.ToArray());
        Assert.IsFalse(Project(("ApiDocs", "false")).ApiDocs);
        Assert.IsFalse(Project(("ApiDocs", "maybe")).ApiDocs);
    }

    [TestMethod]
    public void A_plan_includes_the_templates_a_flag_adds_and_nothing_else()
    {
        var customer = Sample.Table("Customer", [Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("Name", SqlDbType.NVarChar, characters: 40, ordinal: 2)]);
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [customer] };
        var templates = TemplateCatalog.Discover(Repo.TemplatesDirectory);

        var none = ProjectPlan.Build(templates, database, Project(), ["Api"]).Select(s => s.Template.Name).ToList();
        var all = ProjectPlan.Build(templates, database, Project(("ApiDocs", "true"), ("ApiHttp", "true"), ("ApiFakers", "true"), ("ProjectDocs", "true"), ("ApiValidation", "true")), ["Api"]).ToDictionary(s => s.Template.Name);

        foreach (string name in new[] { "API_OpenApi", "API_Http", "CS_Faker", "MD_DataDictionary", "MD_Erd", "CS_Validator" })
        {
            CollectionAssert.DoesNotContain(none, name);
            Assert.IsTrue(all.ContainsKey(name), name);
        }
        CollectionAssert.AreEqual(new[] { "Customer" }, all["API_Http"].TableNames.ToArray());
        Assert.AreEqual(0, all["API_OpenApi"].TableNames.Count, "a whole-database template runs once");
    }

    [TestMethod]
    public void The_API_rules_live_on_the_table_and_the_database_uses_them()
    {
        var lookup = Sample.Roles();
        var plain = Sample.DonateLeave();
        var database = new DatabaseModel { DatabaseName = "Acme", SchemaName = "dbo", Tables = [lookup, plain, Sample.CompositeKey()] };
        var project = Project(("EnumTables", "none"));

        Assert.IsTrue(plain.HasCrudApi(project));
        Assert.IsFalse(Sample.CompositeKey().HasCrudApi(project));
        CollectionAssert.AreEqual(database.ApiTables(project).Select(t => t.TableName).ToArray(), database.EntityTables.Where(t => t.HasCrudApi(project)).Select(t => t.TableName).ToArray());
        CollectionAssert.AreEqual(database.SearchApiTables(project).Select(t => t.TableName).ToArray(), database.ApiTables(project).Where(t => t.HasSearchApi(project)).Select(t => t.TableName).ToArray());
    }

    // ------------------------------------------------------------------ validation filter and the Swagger page

    [TestMethod]
    public async Task The_create_and_update_endpoints_run_the_validators_only_when_asked()
    {
        var plain = await Render("API_Crud_v1.tt", Sample.DonateLeave(), Project());
        var validated = await Render("API_Crud_v1.tt", Sample.DonateLeave(), Project(("ApiValidation", "true")));

        Expect.DoesNotContain(plain, "ValidationFilter");
        Assert.AreEqual(2, validated.Split("AddEndpointFilter<ValidationFilter<E_DonateLeave>>()").Length - 1, "the create and the update, not the reads or the delete");
        Expect.Contains(validated, "app.MapPost(_apiSubDir, CreateRow)\n        .WithName($\"Create{singular}\")\n        .WithOpenApi()\n        .AddEndpointFilter<ValidationFilter<E_DonateLeave>>()");
    }

    [TestMethod]
    public async Task The_production_profile_adds_files_and_no_package_only_when_asked()
    {
        var plain = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project());
        var production = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(("ApiProduction", "true")));
        Assert.IsTrue(plain.Success && production.Success, string.Join(" | ", plain.Errors.Concat(production.Errors)));
        string without = plain.GeneratedText!.Replace("\r\n", "\n"), with = production.GeneratedText!.Replace("\r\n", "\n");

        foreach (string part in new[] { "ProductionProfile", "AddProductionProfile", "Dockerfile", "UseRateLimiter", "MapProductionHealth", "RateLimit", "Cors:Origins" })
            Expect.DoesNotContain(without, part);

        Expect.Contains(with, "@@@FILE ProductionProfile.cs@@@");
        Expect.Contains(with, "@@@FILE Dockerfile@@@");
        Expect.Contains(with, "@@@FILE .dockerignore@@@");
        Expect.Contains(with, "builder.AddProductionProfile();");
        Expect.Contains(with, "var app = builder.Build();\napp.UseProductionProfile();\napp.UseCors();\napp.UseRateLimiter();");
        Expect.Contains(with, "app.MapProductionHealth();");
        Expect.Contains(with, "\"RateLimit\": {\n    \"PermitsPerMinute\": 100\n  },");
        Expect.Contains(with, "public class DatabaseHealthCheck(AcmeContext context) : IHealthCheck");
        Expect.Contains(with, "ENV Urls=http://+:8080 \\\n    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true");
        Assert.AreEqual(without.Split("PackageReference").Length, with.Split("PackageReference").Length, "no package is added");
    }

    [TestMethod]
    public async Task The_program_files_add_the_package_the_filter_the_registration_and_the_swagger_page_when_asked()
    {
        var plain = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project());
        var all = await Repo.Cache.RunAsync(Repo.Template("API_EssentialProgram_v1.tt"), Project(("ApiDocs", "true"), ("ApiValidation", "true"), ("ApiFakers", "true")));
        Assert.IsTrue(plain.Success && all.Success, string.Join(" | ", plain.Errors.Concat(all.Errors)));
        string without = plain.GeneratedText!.Replace("\r\n", "\n"), with = all.GeneratedText!.Replace("\r\n", "\n");

        foreach (string part in new[] { "FluentValidation", "Bogus", "MapOpenApiDocs", "OpenApiDocs.cs", "ValidationFilter", "AddValidatorsFromAssemblyContaining" })
            Expect.DoesNotContain(without, part);

        Expect.Contains(with, "<PackageReference Include=\"FluentValidation.DependencyInjectionExtensions\" Version=\"12.0.0\" />");
        Expect.Contains(with, "<PackageReference Include=\"Bogus\" Version=\"35.6.3\" />");
        Expect.Contains(with, "<Content Include=\"openapi.yaml\" CopyToOutputDirectory=\"PreserveNewest\" CopyToPublishDirectory=\"PreserveNewest\" />");
        Expect.Contains(with, "builder.Services.AddValidatorsFromAssemblyContaining<Program>();");
        Expect.Contains(with, "app.RegisterGeneratedApis();\n\n// openapi.yaml (API_OpenApi) and a Swagger UI page over it at /docs.\napp.MapOpenApiDocs();");
        Expect.Contains(with, "@@@FILE OpenApiDocs.cs@@@");
        Expect.Contains(with, "app.MapGet(\"/openapi.yaml\"");
        Expect.Contains(with, "SwaggerUIBundle({ url: '/openapi.yaml', dom_id: '#swagger' });");
        Expect.Contains(with, "@@@FILE ValidationFilter.cs@@@");
        Expect.Contains(with, "public class ValidationFilter<T> : IEndpointFilter where T : class");
        Expect.Contains(with, "Results.ValidationProblem(result.ToDictionary())");
    }
}
