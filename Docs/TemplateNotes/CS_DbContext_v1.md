# CS_DbContext_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <ContextName>.cs   (see OutputName in CS_DbContext_v1.tt.config)

The EF Core context for the whole database, written for the database the schema was read from:
  - one DbSet<Entity> per table that has a primary key (the tables CS_Entity writes an entity for), named like the table, or in the plural with
    the project setting DbSetNames=Plural;
  - OnModelCreating, only when a table has a composite primary key (a junction table): HasKey for each of them, then a call to the
    partial method OnModelCreatingPartial that a hand-written part of the class can implement;
  - both constructors: the one dependency injection uses (a web API) and the parameterless one a desktop app uses, whose
    OnConfiguring reads the connection string from appsettings.json next to the exe;
  - the provider call - UseSqlServer, UseNpgsql or UseMySQL - in ONE place, UseProvider, which the API registration
    (API_Registration) calls too, so changing database is a regenerate and not a hand edit;
  - for PostgreSQL, "timestamp" as the column type of every DateTime: a DateTime maps to timestamptz by default and Npgsql then
    refuses any value that is not UTC, while the columns this generator reads are plain timestamps;
  - for PostgreSQL and MySQL the password is never in appsettings.json: Npgsql reads PGPASSWORD itself and, because the MySQL
    driver does not read MYSQL_PWD, the generated code adds it when the connection string has no password.
The class is partial (a context inside a WinUI 3 project is reachable from WinRT-projected types, which otherwise warns CsWinRT1028).
The NuGet package the provider needs is named in the class comment. Hand-written parts (extra DbSets for views, OnModelCreating)
go in another part of the partial class, so regenerating never loses them.
```
