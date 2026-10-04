namespace CodeGenNew.Core;

/// <summary> How the code a project generates reaches the database for search, sort, paging, clone and the many-to-many editors. </summary>
public enum AccessMode
{
    /// <summary> Through the routines the <c>SP_</c> templates write (stored procedures, PostgreSQL functions, MySQL procedures): the default wherever routines exist. </summary>
    Routines,

    /// <summary> Through LINQ over the EF Core context, with no routine in the database. The only way for SQLite, and an option for any database whose owners do not want routines deployed. </summary>
    Ef
}

public static class AccessModes
{
    /// <summary> The mode a project uses for a database: the project's <c>AccessMode</c> setting, else <see cref="AccessMode.Ef"/> where the database has no routines and <see cref="AccessMode.Routines"/> elsewhere.
    /// A setting of <c>Routines</c> over a database without them is not honoured. </summary>
    public static AccessMode For(ProjectSettings project, SqlDialect dialect)
    {
        if (!DialectInfo.For(dialect).SupportsRoutines)
            return AccessMode.Ef;
        return project.AccessModeSetting ?? AccessMode.Routines;
    }
}
