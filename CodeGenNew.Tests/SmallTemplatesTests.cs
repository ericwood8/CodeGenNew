using System.Data;
using CodeGenNew.Core;
using CodeGenNew.TemplateEngine;

namespace CodeGenNew.Tests;

/// <summary> The zod schema, the Angular validators, the proto file, the Mapperly mapper and the foreign-key index script, and the column rules the first two share with CS_Validator. </summary>
[TestClass]
public class SmallTemplatesTests
{
    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static ForeignKeyModel ForeignKey(string column, string table) => new()
    {
        ConstraintName = "FK_" + table, ReferencingColumns = [column], ReferencedSchema = "dbo", ReferencedTable = table, ReferencedColumns = [column]
    };

    private static TableModel Customer(List<IndexModel>? indexes = null) => Sample.Table("Customer",
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
        Sample.Column("Website", SqlDbType.VarChar, nullable: true, characters: 100, ordinal: 12),
        Sample.Column("Taxable", SqlDbType.Bit, ordinal: 13),
        Sample.Column("OpenedOn", SqlDbType.Date, nullable: true, ordinal: 14)
    ],
    [ForeignKey("CustomerStatusId", "CustomerStatus")], indexes: indexes);

    private static string Text(TemplateResult result, string template)
    {
        if (!result.Success)
            Assert.Fail(template + ": " + string.Join(" | ", result.Errors).ReplaceLineEndings(" "));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static async Task<string> Render(string template, TableModel table, ProjectSettings? project = null) =>
        Text(await Repo.Cache.RunAsync(Repo.Template(template), table, project ?? Project()), template);

    private static async Task<string> Render(string template, DatabaseModel database, ProjectSettings? project = null) =>
        Text(await Repo.Cache.RunAsync(Repo.Template(template), database, project ?? Project()), template);

    // ------------------------------------------------------------------ ColumnRules

    [TestMethod]
    public void A_column_rule_states_only_what_the_schema_says()
    {
        var project = Project();
        var name = Sample.Column("Name", SqlDbType.NVarChar, characters: 50).RuleOf(project)!;
        Assert.IsTrue(name.Required);
        Assert.AreEqual(50, name.MaxLength);

        var email = Sample.Column("BillingEmail", SqlDbType.VarChar, nullable: true, characters: 100).RuleOf(project)!;
        Assert.IsFalse(email.Required);
        Assert.AreEqual(TextShape.Email, email.Shape);

        var weight = Sample.Column("Weight", SqlDbType.Decimal, precision: 8, scale: 2, check: new CheckRange(0, true, null, false)).RuleOf(project)!;
        Assert.AreEqual(0, weight.Min);
        Assert.IsTrue(weight.MinStrict);
        Assert.IsNull(weight.Max);

        var degrees = Sample.Column("Longitude", SqlDbType.Decimal, nullable: true, precision: 9, scale: 6).RuleOf(project)!;
        Assert.AreEqual(-180, degrees.Min);
        Assert.AreEqual(180, degrees.Max);

        Assert.IsNull(Sample.Column("CustomerStatusId", SqlDbType.Int).RuleOf(project), "a plain foreign key number says nothing");
        Assert.IsNull(Sample.Column("Id", SqlDbType.Int, identity: true).RuleOf(project), "the database fills in an identity");
        Assert.IsNull(Sample.Column("Active", SqlDbType.Bit, nullable: true).RuleOf(project), "a flag says nothing");
    }

    // ------------------------------------------------------------------ TSX_Schema

    [TestMethod]
    public async Task The_zod_schema_has_a_field_per_editable_column_with_the_limits_the_schema_states()
    {
        string ts = await Render("TSX_Schema_v1.tt", Customer());

        Expect.Contains(ts, "@@@FILE schemas/customer.ts@@@\nimport { z } from \"zod\";\n\nexport const CustomerSchema = z.object({\n");
        Expect.Contains(ts, "    customerStatusId: z.number().int(),");
        Expect.Contains(ts, "    name: z.string().min(1, \"Name is required.\").max(50, \"Name is longer than 50 characters.\"),");
        Expect.Contains(ts, "    billingEmail: z.string().max(100, \"Billing Email is longer than 100 characters.\").email(\"Billing Email is not an email address.\").nullable(),");
        Expect.Contains(ts, "    creditLimit: z.number().min(0, \"Credit Limit must be at least 0.\").max(5000, \"Credit Limit must be at most 5000.\").nullable(),");
        Expect.Contains(ts, "    rating: z.number().int().min(1, \"Rating must be at least 1.\").max(5, \"Rating must be at most 5.\").nullable(),");
        Expect.Contains(ts, "    tier: z.enum([\"Gold\", \"Silver\", \"Bronze\"]),");
        Expect.Contains(ts, "    weight: z.number().gt(0, \"Weight must be more than 0.\"),");
        Expect.Contains(ts, "    latitude: z.number().min(-90, ");
        Expect.Contains(ts, @"    phone: z.string().max(20, ""Phone is longer than 20 characters."").regex(/^[0-9+()\-.\s]{3,25}$/, ""Phone is not a phone number."").nullable(),");
        Expect.Contains(ts, "    website: z.string().max(100, \"Website is longer than 100 characters.\").url(\"Website is not a URL.\").nullable(),");
        Expect.Contains(ts, "    taxable: z.boolean(),");
        Expect.Contains(ts, "    openedOn: z.string().nullable()");
        Expect.Contains(ts, "export type CustomerForm = z.infer<typeof CustomerSchema>;");
        Expect.DoesNotContain(ts, "customerId:");   // an identity
        Expect.DoesNotContain(ts, "added:");        // a create date the database sets
    }

    [TestMethod]
    public async Task The_zod_schema_is_refused_when_the_database_fills_in_every_column()
    {
        var table = Sample.Table("Counter", [Sample.Column("CounterId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1)]);

        Assert.IsFalse((await Repo.Cache.RunAsync(Repo.Template("TSX_Schema_v1.tt"), table, Project())).Success);
    }

    // ------------------------------------------------------------------ TS_Validators

    [TestMethod]
    public async Task The_Angular_validators_state_each_rule_and_add_a_function_only_for_a_strict_bound()
    {
        string ts = await Render("TS_Validators_v1.tt", Customer());

        Expect.Contains(ts, "@@@FILE validators/customer.validators.ts@@@\nimport { AbstractControl, ValidationErrors, ValidatorFn, Validators } from \"@angular/forms\";");
        Expect.Contains(ts, "function greaterThan(min: number): ValidatorFn {");
        Expect.DoesNotContain(ts, "function lessThan");
        Expect.Contains(ts, "export const customerValidators: Record<string, ValidatorFn[]> = {");
        Expect.Contains(ts, "    name: [Validators.required, Validators.maxLength(50)],");
        Expect.Contains(ts, "    billingEmail: [Validators.maxLength(100), Validators.email],");
        Expect.Contains(ts, "    creditLimit: [Validators.min(0), Validators.max(5000)],");
        Expect.Contains(ts, "    rating: [Validators.min(1), Validators.max(5)],");
        Expect.Contains(ts, "    tier: [Validators.required, Validators.pattern(/^(Gold|Silver|Bronze)$/)],");
        Expect.Contains(ts, "    weight: [greaterThan(0)],");
        Expect.Contains(ts, "    website: [Validators.maxLength(100), Validators.pattern(/^https?:\\/\\/\\S+$/i)]");
        Expect.DoesNotContain(ts, "customerStatusId");   // a plain foreign key number states no rule
        Expect.DoesNotContain(ts, "taxable:");
    }

    [TestMethod]
    public async Task The_Angular_validators_are_an_empty_record_when_the_schema_states_nothing()
    {
        var table = Sample.Table("Flag", [Sample.Column("FlagId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1), Sample.Column("On", SqlDbType.Bit, ordinal: 2)]);

        string ts = await Render("TS_Validators_v1.tt", table);

        Expect.Contains(ts, "import { ValidatorFn } from \"@angular/forms\";");
        Expect.Contains(ts, "export const flagValidators: Record<string, ValidatorFn[]> = {\n};");
    }

    // ------------------------------------------------------------------ PROTO_Message

    [TestMethod]
    public async Task The_proto_file_has_the_row_the_key_the_list_messages_and_a_service()
    {
        string proto = await Render("PROTO_Message_v1.tt", Customer());

        Expect.Contains(proto, "syntax = \"proto3\";\n\nimport \"google/protobuf/timestamp.proto\";\nimport \"google/protobuf/empty.proto\";\n\npackage acme;");
        Expect.Contains(proto, "option csharp_namespace = \"Acme.Grpc\";");
        Expect.Contains(proto, "message Customer {\n  int32 customer_id = 1;\n  int32 customer_status_id = 2;\n  string name = 3;\n  optional string billing_email = 4;\n  optional string credit_limit = 5;");
        Expect.Contains(proto, "  google.protobuf.Timestamp added = 9;");
        Expect.Contains(proto, "  bool taxable = 13;");
        Expect.Contains(proto, "  optional google.protobuf.Timestamp opened_on = 14;");
        Expect.Contains(proto, "message CustomerKey {\n  int32 customer_id = 1;\n}");
        Expect.Contains(proto, "message ListCustomersResponse {\n  repeated Customer items = 1;\n  int32 total_count = 2;\n}");
        Expect.Contains(proto, "service CustomerService {\n  rpc Get (CustomerKey) returns (Customer);");
        Expect.Contains(proto, "  rpc Delete (CustomerKey) returns (google.protobuf.Empty);");
    }

    [TestMethod]
    public async Task A_proto_file_imports_the_timestamp_only_when_a_date_is_used_and_a_composite_key_has_every_part()
    {
        var table = Sample.Table("OrderLine",
        [
            Sample.Column("OrderId", SqlDbType.Int, primaryKey: true, ordinal: 1),
            Sample.Column("LineNumber", SqlDbType.SmallInt, primaryKey: true, ordinal: 2),
            Sample.Column("Quantity", SqlDbType.BigInt, ordinal: 3)
        ]);

        string proto = await Render("PROTO_Message_v1.tt", table);

        Expect.DoesNotContain(proto, "timestamp.proto");
        Expect.Contains(proto, "message OrderLineKey {\n  int32 order_id = 1;\n  int32 line_number = 2;\n}");
        Expect.Contains(proto, "  int64 quantity = 3;");
    }

    [TestMethod]
    public void The_proto_template_names_its_file_after_the_table()
    {
        var template = TemplateCatalog.Discover(Repo.TemplatesDirectory).Single(t => t.Name == "PROTO_Message");

        Assert.AreEqual("Customer.proto", template.BuildFileName("Customer"));
    }

    // ------------------------------------------------------------------ CS_MapperlyMapper

    [TestMethod]
    public async Task The_Mapperly_mapper_declares_the_three_partial_methods()
    {
        string cs = await Render("CS_MapperlyMapper_v1.tt", Customer());

        Expect.Contains(cs, "using Riok.Mapperly.Abstractions;");
        Expect.Contains(cs, "[Mapper]\npublic static partial class CustomerMapper\n{");
        Expect.Contains(cs, "    [MapperRequiredMapping(RequiredMappingStrategy.Target)]\n    public static partial CustomerDto ToDto(Customer entity);");
        Expect.Contains(cs, "    [MapperRequiredMapping(RequiredMappingStrategy.Source)]\n    public static partial Customer ToEntity(CustomerDto dto);");
        Expect.Contains(cs, "    public static partial IQueryable<CustomerDto> ProjectToDto(this IQueryable<Customer> query);");
    }

    // ------------------------------------------------------------------ SP_ForeignKeyIndexes

    private static DatabaseModel Database(SqlDialect dialect, params TableModel[] tables) => new() { DatabaseName = "Acme", SchemaName = "dbo", Dialect = dialect, Tables = [.. tables] };

    private static TableModel Ticket(string column = "CustomerStatusId", List<IndexModel>? indexes = null) => Sample.Table("Ticket",
    [
        Sample.Column("TicketId", SqlDbType.Int, primaryKey: true, ordinal: 1),
        Sample.Column(column, SqlDbType.Int, ordinal: 2)
    ],
    [ForeignKey(column, "CustomerStatus")], indexes: indexes ?? [new IndexModel("PK_Ticket", true, ["TicketId"])]);

    [TestMethod]
    public async Task An_unindexed_foreign_key_gets_a_create_index_in_the_databases_own_syntax()
    {
        string sqlServer = await Render("SP_ForeignKeyIndexes_v1.tt", Database(SqlDialect.SqlServer, Ticket()));
        Expect.Contains(sqlServer, "@@@FILE ForeignKeyIndexes.sql@@@");
        Expect.Contains(sqlServer, "IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Ticket_CustomerStatusId' AND object_id = OBJECT_ID(N'[dbo].[Ticket]'))\n    CREATE INDEX [IX_Ticket_CustomerStatusId] ON [dbo].[Ticket] ([CustomerStatusId]);");

        string postgres = await Render("SP_ForeignKeyIndexes_v1.tt", Database(SqlDialect.PostgreSql, Ticket()));
        Expect.Contains(postgres, "CREATE INDEX IF NOT EXISTS \"IX_Ticket_CustomerStatusId\" ON \"dbo\".\"Ticket\" (\"CustomerStatusId\");");

        string mySql = await Render("SP_ForeignKeyIndexes_v1.tt", Database(SqlDialect.MySql, Ticket()));
        Expect.Contains(mySql, "CREATE INDEX `IX_Ticket_CustomerStatusId` ON `Ticket` (`CustomerStatusId`);");
    }

    [TestMethod]
    public async Task A_foreign_key_that_an_index_starts_with_is_left_alone()
    {
        var covered = Ticket(indexes: [new IndexModel("PK_Ticket", true, ["TicketId"]), new IndexModel("IX_Ticket_Status_Id", false, ["CustomerStatusId", "TicketId"])]);

        string sql = await Render("SP_ForeignKeyIndexes_v1.tt", Database(SqlDialect.SqlServer, covered));

        Expect.DoesNotContain(sql, "CREATE INDEX");
        Expect.Contains(sql, "Every foreign key of this database already has an index.");
    }

    [TestMethod]
    public async Task A_long_index_name_is_cut_to_the_shortest_limit_and_stays_unique()
    {
        string longName = new string('A', 40);
        string first = await Render("SP_ForeignKeyIndexes_v1.tt", Database(SqlDialect.PostgreSql, Ticket(longName + "OneId")));
        string second = await Render("SP_ForeignKeyIndexes_v1.tt", Database(SqlDialect.PostgreSql, Ticket(longName + "TwoId")));

        string Name(string sql) => System.Text.RegularExpressions.Regex.Match(sql, "CREATE INDEX IF NOT EXISTS \"([^\"]+)\"").Groups[1].Value;
        Assert.IsLessThanOrEqualTo(63, Name(first).Length);
        Assert.AreNotEqual(Name(first), Name(second));
        Assert.AreEqual(Name(first), Name(await Render("SP_ForeignKeyIndexes_v1.tt", Database(SqlDialect.PostgreSql, Ticket(longName + "OneId")))), "the same input gives the same name");
    }
}
