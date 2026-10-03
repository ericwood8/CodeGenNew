# API_Registration_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: ApiRegistration.cs   (see OutputName in API_Registration_v1.tt.config)

The part of a web API's Program.cs that used to be edited by hand for every table. Program.cs becomes

    builder.Services.AddGeneratedDbContext(builder.Configuration);
    ...
    app.RegisterGeneratedApis();

and never changes again:
  - AddGeneratedDbContext registers the context with the provider CS_DbContext chose (UseNpgsql, UseMySQL, UseSqlServer),
    reading ConnectionStrings:<connectionName> from the configuration;
  - RegisterGeneratedApis writes one `new <Table>Api<Table>().Register(app)` and one `<Table>SearchApi` line for every table
    API_Crud writes an API for (a single int key, no name/active shape, not an enum or other table the project says has no
    repository) and one `<Table>SearchApi` line for the same tables except bare lookup tables (a few rows, no search function).
    Enum tables get an entity and a DbSet but no API, so they are not listed.
A table that needs a hand-written API (a name/active table, a composite key) is registered in Program.cs as before.
```
