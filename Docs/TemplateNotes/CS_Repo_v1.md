# CS_Repo_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>Repo.cs   (see OutputName in CS_Repo_v1.tt.config)

A repository class:

    public class HolidayRepo : GenericRepo<Holiday>
    {
        public HolidayRepo(AppContext context) : base(context) { }
        ... extra queries ...
    }

The shared plumbing (GenericRepo<T> / NameActiveRepo<T> and their interfaces) is written by hand once and is not
per-table, so this template only writes the thin per-table class on top of it:
  - Base class: a table with a NOT NULL text column called Name AND a NOT NULL bit column called IsActive uses
    NameActiveRepo<Entity> (which brings GetByName, GetAllActive and the duplicate-name check); any other table uses
    GenericRepo<Entity>. This is the same test CS_Entity uses to choose BaseNameActiveEntity / BaseEntity, and the
    entity has to derive from the matching base for the repo to compile.
  - Extra queries, only where the COLUMNS alone say what to write:
      * a name/active table that hangs off exactly one parent through a NOT NULL int foreign key (a "child" table:
        DepartmentTeam -> Department) gets  GetAllOf<Parent>(int id)  - its active rows for that parent, by name;
      * a table with a NOT NULL text Name that is NOT name/active-based gets  GetByName(string name)  - names starting
        with the text (Holiday);
      * every column that takes part in a UNIQUE index or constraint OTHER than the primary key
        (ColumnModel.IsInUniqueIndex - the same signal SP_Clone.tt already uses to decide which columns need a
        caller-supplied override on copy) gets  HasDuplicate<Column>(value, excludeId)  - does some OTHER row
        already have this value, the generalized shape of CityRepository.HasDuplicates in
        One column at a time:
        IsInUniqueIndex only says a column takes part in SOME unique index, not which other columns share a
        composite one, so a composite unique constraint gets one HasDuplicate per column rather than a single
        combined check;
      * every such column that is ALSO a string column gets  SuggestUnique<Column>(desired)  - given a
        starting value (e.g. "New Account"), returns it unchanged if free, else the same value with a numeric
        suffix bumped until free ("New Account2", "New Account3", ...), for pre-filling an Add dialog with
        something that will actually save. The generalized shape of a real EnsureNameIsUnique/IsUniqueName
        helper pair that needed a per-entity-type
        switch statement in the hand-written codebase it came from; generating one column at a time from
        IsInUniqueIndex needs no switch at all. Same single-column scope and composite-index caveat as
        HasDuplicate<Column> above - and the same caveat about NOT knowing whether a real project's
        constraint is actually scoped to a parent (e.g. unique per model, not globally) rather than table-wide;
      * a table with at least one searchable (string, non-audit) column gets SearchAsync(...) - the same
        filter/pagination SP_Search.tt/SP_SearchCount.tt generate, called directly through the DbContext so an in-process WinUI3 screen and the web API's
        API_Search.tt endpoint share one implementation instead of two.
  - No entity, no repo: a table listed in noRepositoryTables (the ones that are C# enums, the *Type lookup tables, and
    tables with no entity class) stops with an error instead of writing a class that cannot compile.

What it does NOT do, so a repository that needs any of these stays hand-maintained: queries that Include navigation
properties (GetAllIncludeTeams, GetAllIncludeDropdowns) - the entity's navigations are the developers' choice, and a
guess would not compile; lookups by a hand-picked column or order (GetAllOfTimesheet ordered by project).
```

- **Access mode (EF Core instead of routines):** with `AccessMode=Ef` (always for SQLite) this template writes LINQ over the context instead of calls to the routines. See CS_SearchQuery_v1.md and Docs/Reference.md.
