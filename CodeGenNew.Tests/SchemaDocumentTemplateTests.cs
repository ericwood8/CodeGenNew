using System.Text.RegularExpressions;
using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;

namespace CodeGenNew.Tests;

/// <summary> The templates that write what the schema says for tools and people: an OpenAPI document, a data dictionary, an ER diagram and FluentValidation validators. </summary>
[TestClass]
public class SchemaDocumentTemplateTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static TableModel Status() => Sample.Table("CustomerStatus",
    [
        Sample.Column("CustomerStatusId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Description", SqlDbType.NVarChar, characters: 40, ordinal: 2)
    ]);

    private static TableModel Customer() => Sample.Table("Customer",
    [
        Sample.Column("CustomerId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("CustomerStatusId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 50, ordinal: 3),
        Sample.Column("BillingEmail", SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 4),
        Sample.Column("CreditLimit", SqlDbType.Decimal, nullable: true, precision: 10, scale: 2, ordinal: 5, check: new CheckRange(0, false, 5000, false)),
        Sample.Column("Rating", SqlDbType.Int, nullable: true, ordinal: 6, check: new CheckRange(1, false, 5, false)),
        Sample.Column("Tier", SqlDbType.VarChar, characters: 10, ordinal: 7, choices: ["Gold", "Silver", "Bronze"]),
        Sample.Column("Weight", SqlDbType.Decimal, precision: 8, scale: 2, ordinal: 8, check: new CheckRange(0, true, null, false)),
        Sample.Column("Added", SqlDbType.DateTime, ordinal: 9, createDateColumn: true),
        Sample.Column("Latitude", SqlDbType.Decimal, nullable: true, precision: 9, scale: 6, ordinal: 10),
        Sample.Column("Phone", SqlDbType.VarChar, nullable: true, characters: 20, ordinal: 11),
        Sample.Column("Website", SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 12)
    ],
    [ForeignKey("CustomerStatusId", "CustomerStatus", "CustomerStatusId", "Description")]);

    private static ForeignKeyModel ForeignKey(string column, string table, string referenced, string display) => new()
    {
        ConstraintName = $"FK_{table}", ReferencingColumns = [column], ReferencedSchema = "dbo", ReferencedTable = table, ReferencedColumns = [referenced],
        ReferencedDisplayColumns = [display]
    };

    private static DatabaseModel Database(params TableModel[] tables) => new() { DatabaseName = "Acme", SchemaName = "dbo", Tables = tables.Length > 0 ? tables.ToList() : [Status(), Customer()] };

    private static async Task<string> Render(string template, TableModel table, ProjectSettings? project = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, project ?? Project());
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    private static async Task<string> Render(string template, DatabaseModel database, ProjectSettings? project = null)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), database, project ?? Project());
        Assert.IsTrue(result.Success, $"{template}: {string.Join(" | ", result.Errors)}");
        return result.GeneratedText!.Replace("\r\n", "\n");
    }

    // ------------------------------------------------------------------ JsonNames

    [TestMethod]
    [DataRow("IsActive", "isActive")]
    [DataRow("CustomerId", "customerId")]
    [DataRow("SY_Role", "sY_Role")]
    [DataRow("URL", "url")]
    [DataRow("E_TimeSheetId", "e_TimeSheetId")]
    [DataRow("name", "name")]
    [DataRow("X", "x")]
    public void A_property_is_camel_cased_the_way_ASP_NET_Core_sends_it(string name, string expected)
    {
        Assert.AreEqual(expected, JsonNames.Camel(name));
    }

    // ------------------------------------------------------------------ API_OpenApi

    [TestMethod]
    public async Task The_openapi_document_is_valid_and_has_every_api_route()
    {
        string yaml = await Render("API_OpenApi_v1.tt", Database(), Project(("ApiPort", "5123")));
        Expect.Contains(yaml, "@@@FILE openapi.yaml@@@");

        var document = new OpenApiStringReader().Read(yaml.Replace("@@@FILE openapi.yaml@@@\n", ""), out var diagnostic);

        Assert.AreEqual(0, diagnostic.Errors.Count, string.Join(" | ", diagnostic.Errors.Select(e => e.Pointer + ": " + e.Message)));
        Assert.AreEqual("http://localhost:5123", document.Servers.Single().Url);
        CollectionAssert.IsSubsetOf(
            new[] { "/api/customers", "/api/customers/{id}", "/api/customers/search", "/api/customerstatus", "/api/customerstatus/{id}" }, document.Paths.Keys.ToArray());
        var customers = document.Paths["/api/customers"];
        Assert.AreEqual("GetCustomers", customers.Operations[OperationType.Get].OperationId);
        Assert.AreEqual("CreateCustomer", customers.Operations[OperationType.Post].OperationId);
        var byId = document.Paths["/api/customers/{id}"];
        CollectionAssert.AreEquivalent(new[] { OperationType.Get, OperationType.Put, OperationType.Delete }, byId.Operations.Keys.ToArray());
    }

    [TestMethod]
    public async Task A_schema_carries_the_columns_limits()
    {
        string yaml = await Render("API_OpenApi_v1.tt", Database());
        var document = new OpenApiStringReader().Read(yaml.Replace("@@@FILE openapi.yaml@@@\n", ""), out _);
        var schema = document.Components.Schemas["Customer"];

        CollectionAssert.IsSubsetOf(new[] { "customerId", "customerStatusId", "name", "tier", "weight", "added" }, schema.Required.ToArray());
        Assert.IsFalse(schema.Required.Contains("billingEmail"));
        Assert.AreEqual("integer", schema.Properties["customerId"].Type);
        Assert.AreEqual(50, schema.Properties["name"].MaxLength);
        Assert.IsTrue(schema.Properties["billingEmail"].Nullable);
        Assert.AreEqual(0m, schema.Properties["creditLimit"].Minimum);
        Assert.AreEqual(5000m, schema.Properties["creditLimit"].Maximum);
        Assert.AreEqual(1m, schema.Properties["rating"].Minimum);
        Assert.AreEqual(5m, schema.Properties["rating"].Maximum);
        CollectionAssert.AreEqual(new[] { "Gold", "Silver", "Bronze" }, schema.Properties["tier"].Enum.Select(e => ((Microsoft.OpenApi.Any.OpenApiString)e).Value).ToArray());
        Assert.AreEqual(true, schema.Properties["weight"].ExclusiveMinimum);   // CHECK (Weight > 0) stays strict
        Assert.AreEqual("date-time", schema.Properties["added"].Format);
        Assert.AreEqual("CustomerStatus", schema.Properties["customerStatus"].Reference.Id);   // the navigation property of the entity
    }

    [TestMethod]
    public async Task The_search_operation_has_a_parameter_per_searchable_column_and_a_paged_result()
    {
        string yaml = await Render("API_OpenApi_v1.tt", Database());
        var document = new OpenApiStringReader().Read(yaml.Replace("@@@FILE openapi.yaml@@@\n", ""), out _);

        var search = document.Paths["/api/customers/search"].Operations[OperationType.Get];
        var names = search.Parameters.Select(p => p.Name).ToList();
        CollectionAssert.IsSubsetOf(new[] { "name", "billingEmail", "pageNumber", "pageSize", "sortBy", "sortDir" }, names);
        Assert.AreEqual("CustomerSearchResult", search.Responses["200"].Content["application/json"].Schema.Reference.Id);
        CollectionAssert.AreEquivalent(new[] { "items", "page", "pageSize", "totalCount", "totalPages" }, document.Components.Schemas["CustomerSearchResult"].Required.ToArray());
    }

    [TestMethod]
    public async Task A_table_without_an_api_is_left_out_and_nothing_to_describe_is_refused()
    {
        var composite = Sample.CompositeKey();
        string yaml = await Render("API_OpenApi_v1.tt", Database(Status(), composite));

        Expect.DoesNotContain(yaml, composite.TableName);

        var none = await Repo.Cache.RunAsync(Repo.Template("API_OpenApi_v1.tt"), Database(composite), Project());
        Assert.IsFalse(none.Success);
    }

    // ------------------------------------------------------------------ MD_DataDictionary

    [TestMethod]
    public async Task The_data_dictionary_page_lists_every_column_with_its_limits_and_keys()
    {
        var customer = Customer();
        string md = await Render("MD_DataDictionary_v1.tt", customer);

        Expect.Contains(md, "# Customer\n");
        Expect.Contains(md, "Table `dbo.Customer`: 12 columns, primary key `CustomerId`.");
        Expect.Contains(md, "| # | Column | Type | Null | Default | Key | Limits | Notes |");
        Expect.Contains(md, "| 1 | `CustomerId` | int | no |  | PK |  | identity |");
        Expect.Contains(md, "| 2 | `CustomerStatusId` | int | no |  | FK -> [CustomerStatus](CustomerStatus.md) |  |  |");
        Expect.Contains(md, "| `Name` | nvarchar(50) | no |  |  | at most 50 characters |");
        Expect.Contains(md, "at least 0; at most 5000");
        Expect.Contains(md, "one of `Gold`, `Silver`, `Bronze`");
        Expect.Contains(md, "greater than 0");
        Expect.Contains(md, "## Refers to\n\n| Columns | Table | Referenced columns |\n|---|---|---|\n| `CustomerStatusId` | [CustomerStatus](CustomerStatus.md) | `CustomerStatusId` |");
    }

    [TestMethod]
    public async Task The_page_shows_the_tables_that_refer_to_it()
    {
        var parent = Status();
        var withChildren = new TableModel
        {
            SchemaName = parent.SchemaName, TableName = parent.TableName, QuotedName = parent.QuotedName, Columns = parent.Columns, PrimaryKeyColumns = parent.PrimaryKeyColumns,
            ForeignKeys = [], ChildForeignKeys =
            [
                new ChildForeignKeyModel { ConstraintName = "FK_x", ReferencingSchema = "dbo", ReferencingTable = "Customer", ReferencingColumns = ["CustomerStatusId"], ReferencedColumns = ["CustomerStatusId"] }
            ]
        };

        string md = await Render("MD_DataDictionary_v1.tt", withChildren);

        Expect.Contains(md, "## Referred to by\n\n| Table | Columns |\n|---|---|\n| [Customer](Customer.md) | `CustomerStatusId` |");
        Expect.DoesNotContain(md, "## Refers to");
    }

    [TestMethod]
    public async Task A_pipe_in_a_default_does_not_break_the_table()
    {
        var table = Sample.Table("Flag", [Sample.Column("FlagId", SqlDbType.Int, primaryKey: true, ordinal: 1), Sample.Column("Mask", SqlDbType.VarChar, characters: 10, defaultSql: "('a|b')", ordinal: 2)]);

        Expect.Contains(await Render("MD_DataDictionary_v1.tt", table), "`('a\\|b')`");
    }

    // ------------------------------------------------------------------ MD_Erd

    [TestMethod]
    public async Task The_diagram_draws_the_tables_and_a_line_per_foreign_key()
    {
        string md = await Render("MD_Erd_v1.tt", Database());

        Expect.Contains(md, "@@@FILE ErDiagram.md@@@");
        Expect.Contains(md, "```mermaid\nerDiagram\n");
        Expect.Contains(md, "    CustomerStatus ||--o{ Customer : \"CustomerStatusId\"");
        Expect.Contains(md, "    Customer {\n        int CustomerId PK\n        int CustomerStatusId FK\n        nvarchar Name\n        varchar BillingEmail \"null\"");
        Expect.Contains(md, "decimal CreditLimit \"null\"");
        Assert.IsTrue(md.TrimEnd().EndsWith("```"));
    }

    [TestMethod]
    public async Task Every_line_of_the_diagram_fits_the_erDiagram_grammar()
    {
        string md = await Render("MD_Erd_v1.tt", Database());
        var lines = md.Replace("\r\n", "\n").Split('\n').SkipWhile(l => l != "erDiagram").Skip(1).TakeWhile(l => l != "```").Where(l => l.Length > 0).ToList();
        Assert.IsGreaterThan(5, lines.Count, "no diagram lines were found");

        var entity = new Regex(@"^    (\w+|""[^""]+"") \{$");
        var attribute = new Regex(@"^        \w+ \w+( (PK|FK|UK)(,(PK|FK|UK))*)?( ""null"")?$");
        var relationship = new Regex(@"^    \w+ (\|\||\|o)--o\{ \w+ : ""[^""]+""$");
        foreach (string line in lines)
            Assert.IsTrue(line == "    }" || entity.IsMatch(line) || attribute.IsMatch(line) || relationship.IsMatch(line), "not erDiagram syntax: " + line);
    }

    [TestMethod]
    public async Task A_nullable_foreign_key_is_zero_or_one_and_the_project_can_pick_the_tables()
    {
        var child = Sample.Table("Ticket",
        [
            Sample.Column("TicketId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
            Sample.Column("CustomerStatusId", SqlDbType.Int, nullable: true, ordinal: 2)
        ],
        [ForeignKey("CustomerStatusId", "CustomerStatus", "CustomerStatusId", "Description")]);

        string all = await Render("MD_Erd_v1.tt", Database(Status(), child, Customer()));
        Expect.Contains(all, "    CustomerStatus |o--o{ Ticket : \"CustomerStatusId\"");

        string picked = await Render("MD_Erd_v1.tt", Database(Status(), child, Customer()), Project(("ErdTables", "CustomerStatus,Ticket")));
        Expect.DoesNotContain(picked, "Customer {");
        Expect.Contains(picked, "Ticket {");

        var unknown = await Repo.Cache.RunAsync(Repo.Template("MD_Erd_v1.tt"), Database(), Project(("ErdTables", "Nothing")));
        Assert.IsFalse(unknown.Success);
    }

    // ------------------------------------------------------------------ CS_Validator

    [TestMethod]
    public async Task The_validator_has_a_rule_for_what_the_schema_says_and_none_for_the_rest()
    {
        string cs = await Render("CS_Validator_v1.tt", Customer(), Project(("EntityNamespace", "Acme.Api.Entities"), ("ValidatorNamespace", "Acme.Api.Validators")));

        Expect.Contains(cs, "using FluentValidation;\nusing Acme.Api.Entities;");
        Expect.Contains(cs, "namespace Acme.Api.Validators;");
        Expect.Contains(cs, "public class CustomerValidator : AbstractValidator<Customer>");
        Expect.Contains(cs, "RuleFor(x => x.Name).NotEmpty().MaximumLength(50);");
        Expect.Contains(cs, "RuleFor(x => x.BillingEmail).MaximumLength(100).EmailAddress();");
        Expect.Contains(cs, "RuleFor(x => x.CreditLimit).GreaterThanOrEqualTo(0m).LessThanOrEqualTo(5000m);");
        Expect.Contains(cs, "RuleFor(x => x.Rating).InclusiveBetween(1, 5);");
        Expect.Contains(cs, "RuleFor(x => x.Weight).GreaterThan(0m);");   // strict stays strict
        Expect.Contains(cs, "private static readonly string[] TierValues = [\"Gold\", \"Silver\", \"Bronze\"];");
        Expect.Contains(cs, "RuleFor(x => x.Tier).NotEmpty().MaximumLength(10).Must(value => value is null || TierValues.Contains(value)).WithMessage(\"Tier must be one of: Gold, Silver, Bronze.\");");
        Expect.Contains(cs, "RuleFor(x => x.Latitude).InclusiveBetween(-90m, 90m);");
        Expect.Contains(cs, "RuleFor(x => x.Phone).MaximumLength(20).Matches(");
        Expect.Contains(cs, "RuleFor(x => x.Website).MaximumLength(100).Must(value => value is null || Uri.IsWellFormedUriString(value, UriKind.Absolute))");
        Expect.DoesNotContain(cs, "x.CustomerId");   // an identity key
        Expect.DoesNotContain(cs, "x.Added");        // a create-date column
        Expect.DoesNotContain(cs, "x.CustomerStatusId");
    }

    [TestMethod]
    public async Task A_table_without_a_key_has_no_entity_and_so_no_validator()
    {
        var keyless = Sample.Table("Log", [Sample.Column("Text", SqlDbType.NVarChar, characters: 50, ordinal: 1)]);

        var result = await Repo.Cache.RunAsync(Repo.Template("CS_Validator_v1.tt"), keyless, Project());

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Errors.Any(e => e.Contains("primary key")));
    }

    // ------------------------------------------------------------------ the family

    [TestMethod]
    public void The_new_templates_are_not_in_a_plan_unless_the_project_asks_and_write_their_own_file_names()
    {
        foreach (string name in new[] { "API_OpenApi_v1.tt.config", "MD_DataDictionary_v1.tt.config", "MD_Erd_v1.tt.config", "CS_Validator_v1.tt.config" })
            Assert.IsFalse(TemplateConfig.Load(Repo.Template(name)).InPlan, name);

        var offered = TemplateCatalog.Discover(Repo.TemplatesDirectory).ToDictionary(t => t.Name);
        Assert.AreEqual("Customer.md", offered["MD_DataDictionary"].BuildFileName("Customer"));
        Assert.AreEqual("CustomerValidator.cs", offered["CS_Validator"].BuildFileName("Customer"));
    }
}
