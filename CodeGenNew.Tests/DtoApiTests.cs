using System.Data;
using CodeGenNew.Connections;
using CodeGenNew.Core;
using CodeGenNew.Generation;
using CodeGenNew.SchemaIntrospection;
using CodeGenNew.TemplateEngine;
using Microsoft.Data.Sqlite;

namespace CodeGenNew.Tests;

/// <summary> Dtos=true: a table's CRUD API takes an input type (CS_DtoInput) and answers the transfer class (CS_Dto), over CrudApi's two extra type arguments. One Core rule (DtoShape), the classes and
/// the base. Run live in a scratch project over SQLite (the answer is the transfer class, an update keeps the columns the input leaves out, the owner is policed, the entity validator runs, a text
/// key, children, the generated test project). </summary>
[TestClass]
public class DtoApiTests
{
    private static readonly (string, string) On = ("Dtos", "true");

    private static ProjectSettings Project(params (string Key, string Value)[] values) =>
        ProjectSettings.FromValues(values.Select(v => new KeyValuePair<string, string>(v.Key, v.Value)).Append(new("ProjectName", "Acme")));

    private static async Task<string> RenderProject(string template, params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static async Task<string> RenderTable(string template, TableModel table, params (string Key, string Value)[] values)
    {
        var result = await Repo.Cache.RunAsync(Repo.Template(template), table, Project(values));
        Assert.IsTrue(result.Success, string.Join(" | ", result.Errors));
        return result.GeneratedText!.ReplaceLineEndings("\n");
    }

    private static TableModel TimeSheet() => Sample.Table("TimeSheet",
    [
        Sample.Column("TimeSheetId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("EmployeeId", SqlDbType.Int, ordinal: 2),
        Sample.Column("Notes", SqlDbType.NVarChar, characters: 100, ordinal: 3),
        Sample.Column("CreateDate", SqlDbType.DateTime2, createDateColumn: true, ordinal: 4),
        Sample.Column("CreateUser", SqlDbType.NVarChar, characters: 50, createUserColumn: true, ordinal: 5),
        Sample.Column("ModifiedBy", SqlDbType.NVarChar, characters: 50, modifiedUserColumn: true, ordinal: 6),
        Sample.Column("DoubleHours", SqlDbType.Int, computed: true, ordinal: 7)
    ]);

    private static TableModel Currency() => Sample.Table("Currency",
    [
        Sample.Column("CurrencyCode", SqlDbType.NVarChar, characters: 3, primaryKey: true, ordinal: 1),
        Sample.Column("Symbol", SqlDbType.NVarChar, characters: 5, ordinal: 2)
    ]);

    private static TableModel Department() => Sample.Table("Department",
    [
        Sample.Column("DepartmentId", SqlDbType.Int, primaryKey: true, identity: true, ordinal: 1),
        Sample.Column("Name", SqlDbType.NVarChar, characters: 100, ordinal: 2),
        Sample.Column("IsActive", SqlDbType.Bit, ordinal: 3)
    ]);

    [TestMethod]
    public void The_setting_is_off_by_default_and_adds_the_three_templates()
    {
        Assert.IsFalse(Project().Dtos);
        Assert.IsTrue(Project(On).Dtos);
        Assert.Contains("Dtos", ProjectSettingsHints.BooleanKeys);
        Assert.AreEqual("API", ProjectSettingGroups.TabOf("Dtos"));
        Assert.DoesNotContain("CS_DtoInput", Project().ImpliedPlanTemplates.ToArray());
        CollectionAssert.IsSubsetOf(new[] { "CS_Dto", "CS_DtoInput", "CS_Mapper" }, Project(On).ImpliedPlanTemplates.ToArray());
        foreach (string template in new[] { "CS_Dto", "CS_DtoInput", "CS_Mapper" })
        {
            var config = TemplateConfig.Load(Repo.Template(template + "_v1.tt.config"));
            Assert.AreEqual(PlanTableSet.Dto, config.PlanTables, template);
            Assert.IsFalse(config.InPlan, template);
        }
    }

    [TestMethod]
    public void The_input_holds_what_a_caller_may_set()
    {
        CollectionAssert.AreEqual(new[] { "EmployeeId", "Notes" }, DtoShape.InputColumns(TimeSheet()).Select(c => c.Name).ToArray(), "no identity key, audit or computed column");
        CollectionAssert.AreEqual(new[] { "CurrencyCode", "Symbol" }, DtoShape.InputColumns(Currency()).Select(c => c.Name).ToArray(), "a key the person chooses stays");
    }

    [TestMethod]
    public void A_table_uses_dtos_only_with_the_setting_and_a_plain_CRUD_API()
    {
        Assert.IsTrue(DtoShape.Uses(TimeSheet(), Project(On)));
        Assert.IsTrue(DtoShape.Uses(Currency(), Project(On)));
        Assert.IsFalse(DtoShape.Uses(TimeSheet(), Project()));
        Assert.IsFalse(DtoShape.Uses(Department(), Project(On)), "a name and active table keeps its entity");
        Assert.IsFalse(DtoShape.Uses(TimeSheet(), Project(On, ("NoRepositoryTables", "TimeSheet"))));
    }

    [TestMethod]
    public async Task The_input_class_creates_a_row_and_changes_the_columns_it_holds()
    {
        string input = await RenderTable("CS_DtoInput_v1.tt", TimeSheet(), On);
        Expect.Contains(input, "public class TimeSheetInput");
        Expect.Contains(input, "public int EmployeeId { get; set; }");
        Expect.Contains(input, "public string Notes { get; set; } = \"\";");
        Expect.DoesNotContain(input, "TimeSheetId");
        Expect.DoesNotContain(input, "CreateDate");
        Expect.DoesNotContain(input, "DoubleHours");
        Expect.Contains(input, "public TimeSheet ToEntity() => new()");
        Expect.Contains(input, "        entity.Notes = Notes;");

        string text = await RenderTable("CS_DtoInput_v1.tt", Currency(), On);
        Expect.Contains(text, "        CurrencyCode = CurrencyCode,");
        Expect.DoesNotContain(text, "entity.CurrencyCode");
        Expect.Contains(text, "        entity.Symbol = Symbol;");
    }

    [TestMethod]
    public async Task The_API_class_names_the_input_and_output_and_converts_them()
    {
        string api = await RenderTable("API_Crud_v1.tt", TimeSheet(), On);
        Expect.Contains(api, "using Acme.App.Dtos;");
        Expect.Contains(api, "public class TimeSheetApi : CrudApi<TimeSheet, TimeSheetRepo, TimeSheetInput, TimeSheetDto>");
        Expect.Contains(api, "protected override TimeSheetDto ToOutput(TimeSheet row) => TimeSheetMapping.ToDto(row)!;");
        Expect.Contains(api, "protected override TimeSheet ToEntity(TimeSheetInput input) => input.ToEntity();");
        Expect.Contains(api, "protected override void Apply(TimeSheetInput input, TimeSheet row) => input.ApplyTo(row);");

        string text = await RenderTable("API_Crud_v1.tt", Currency(), On);
        Expect.Contains(text, "CrudApi<Currency, CurrencyRepo, string, CurrencyInput, CurrencyDto>");

        string plain = await RenderTable("API_Crud_v1.tt", TimeSheet());
        Expect.Contains(plain, "public class TimeSheetApi : CrudApi<TimeSheet, TimeSheetRepo>");
        Expect.DoesNotContain(plain, "Dto");

        string nameActive = await RenderTable("API_Crud_v1.tt", Department(), On);
        Expect.Contains(nameActive, "NameActiveCrudApi<Department, DepartmentRepo>");
        Expect.DoesNotContain(nameActive, "Input");
    }

    [TestMethod]
    public async Task CrudApi_gets_the_two_type_arguments_only_with_the_setting()
    {
        string with = await RenderProject("API_EssentialCrudApi_v1.tt", On);
        Expect.Contains(with, "public abstract class CrudApi<TEntity, TRepo, TKey, TInput, TOutput> : BaseApi<TEntity>");
        Expect.Contains(with, "public abstract class CrudApi<TEntity, TRepo, TInput, TOutput> : CrudApi<TEntity, TRepo, int, TInput, TOutput>");
        Expect.Contains(with, "public abstract class CrudApi<TEntity, TRepo, TKey> : CrudApi<TEntity, TRepo, TKey, TEntity, TEntity>");
        Expect.Contains(with, "[FromBody] TInput newRow");
        Expect.Contains(with, "[FromBody] TInput updatedRow");
        Expect.Contains(with, "protected virtual void Stamp(TEntity row, bool isNew)");
        Expect.Contains(with, "var current = await NewRepo(context).GetByIdAsync(id);");
        Expect.Contains(with, "CrudPage<TOutput>(page.Select(ToOutput).ToList()");
        Expect.Contains(with, ".Produces<TOutput>()");

        string without = await RenderProject("API_EssentialCrudApi_v1.tt");
        Expect.Contains(without, "public abstract class CrudApi<TEntity, TRepo, TKey> : BaseApi<TEntity>");
        Expect.DoesNotContain(without, "TInput");
        Expect.DoesNotContain(without, "TOutput");
        Expect.DoesNotContain(without, "Stamp(");
    }

    [TestMethod]
    public async Task The_entity_validator_runs_on_the_row_an_input_makes()
    {
        string with = await RenderProject("API_EssentialCrudApi_v1.tt", On, ("ApiValidation", "true"));
        Expect.Contains(with, "[FromServices] IValidator<TEntity>? validator");
        Expect.Contains(with, "var result = await validator.ValidateAsync(newRow);");
        Expect.Contains(with, "var result = await validator.ValidateAsync(updatedRow);");
        Expect.Contains(with, "using FluentValidation;");

        string plain = await RenderProject("API_EssentialCrudApi_v1.tt", On);
        Expect.DoesNotContain(plain, "IValidator<TEntity>");
        Expect.DoesNotContain(plain, "using FluentValidation;");
    }

    [TestMethod]
    public async Task Editable_children_are_read_into_the_input()
    {
        string with = await RenderProject("API_EssentialCrudApi_v1.tt", On, ("EditableChildren", "ProjectTask"));
        Expect.Contains(with, "ChildRowsSupport.ReadAsync<TInput>(request)");
        Expect.DoesNotContain(await RenderProject("API_EssentialCrudApi_v1.tt", ("EditableChildren", "ProjectTask")), "ReadAsync<TInput>");
    }

    [TestMethod]
    public async Task The_plan_writes_the_three_classes_for_each_table_with_an_API()
    {
        var files = await Generate("CS_Dto,CS_DtoInput,CS_Mapper,API_Crud", On);
        foreach (string table in new[] { "Employee", "Currency", "TimeSheet" })
        {
            Assert.IsTrue(files.ContainsKey($"Shop.Api/Dtos/{table}Dto.cs"), table + " dto");
            Assert.IsTrue(files.ContainsKey($"Shop.Api/Dtos/{table}Input.cs"), table + " input");
            Assert.IsTrue(files.ContainsKey($"Shop.Api/Dtos/{table}Mapping.cs"), table + " mapping");
        }
        Expect.Contains(files["Shop.Api/Apis/TimeSheetApi.cs"], "CrudApi<TimeSheet, TimeSheetRepo, TimeSheetInput, TimeSheetDto>");

        var plain = await Generate("CS_Dto,CS_DtoInput,CS_Mapper,API_Crud");
        Assert.IsFalse(plain.Keys.Any(k => k.Contains("/Dtos/")), "no setting, no classes");
    }

    private static string DatabaseFile()
    {
        string path = Path.Combine(Path.GetTempPath(), "codegen_dto_" + Guid.NewGuid().ToString("N") + ".db");
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
        connection.Open();
        foreach (string statement in new[]
        {
            "CREATE TABLE Employee (EmployeeId INTEGER PRIMARY KEY AUTOINCREMENT, Name VARCHAR(50) NOT NULL)",
            "CREATE TABLE Currency (CurrencyCode VARCHAR(3) PRIMARY KEY, Symbol VARCHAR(5) NOT NULL)",
            "CREATE TABLE TimeSheet (TimeSheetId INTEGER PRIMARY KEY AUTOINCREMENT, EmployeeId INTEGER NOT NULL REFERENCES Employee (EmployeeId), Notes VARCHAR(100) NOT NULL)"
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = statement;
            command.ExecuteNonQuery();
        }
        return path;
    }

    private static async Task<Dictionary<string, string>> Generate(string only, params (string Key, string Value)[] extra)
    {
        string database = DatabaseFile();
        string output = Path.Combine(Path.GetTempPath(), "codegen_dto_out_" + Guid.NewGuid().ToString("N"));
        try
        {
            var values = new Dictionary<string, string>
            {
                ["ProjectName"] = "Shop", ["DatabaseProvider"] = "Sqlite", ["DatabaseName"] = "shop.db", ["EnumTables"] = "none", ["Stacks"] = "Api", ["ContextName"] = "ShopContext",
                ["ContextNamespace"] = "Shop.Data", ["EntityNamespace"] = "Shop.Entities"
            };
            foreach (var (key, value) in extra)
                values[key] = value;
            var request = new ConnectionRequest { Provider = DatabaseProvider.Sqlite, ServerName = "", DatabaseName = database };
            var provider = SchemaProviderFactory.Create(request, Path.Combine(Repo.Root, "SpecialLogicColumns.config"));
            var report = await Repo.GenerateAsync(provider, new GenerateOptions
            {
                Project = ProjectSettings.FromValues(values), Stacks = ["Api"], OutputDirectory = output, DatabaseName = database, Schema = "main", OnlyTemplates = only.Split(',')
            });
            Assert.IsTrue(report.Success, string.Join(" | ", report.Errors));
            return Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(f => Path.GetRelativePath(output, f).Replace('\\', '/'), f => File.ReadAllText(f).Replace("\r\n", "\n"));
        }
        finally
        {
            File.Delete(database);
            if (Directory.Exists(output))
                Directory.Delete(output, true);
        }
    }
}
