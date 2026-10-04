namespace CodeGenNew.Core;

/// <summary> Error numbers the generated routines raise or react to. Use these names in templates and writers so a number is never bare. </summary>
public static class SqlErrorCodes
{
    /// <summary> SQL Server error for a FOREIGN KEY or CHECK constraint violation, read from @@ERROR after a DELETE. </summary>
    public const int SqlServerForeignKeyViolation = 547;

    /// <summary> MySQL error ER_ROW_IS_REFERENCED_2: the row a DELETE removes is still referenced by another table. </summary>
    public const int MySqlForeignKeyViolation = 1451;

    /// <summary> A lookup routine found no row; the application-defined range starts at 50000 on SQL Server. </summary>
    public const int NoRowToLookUp = 55508;

    /// <summary> A clone routine found no source row. </summary>
    public const int NoRowToClone = 55509;

    /// <summary> A rule the routine checks itself failed (a start date after its end date); the default number of a user-raised error. </summary>
    public const int RuleViolation = 50000;
}

/// <summary> What a generated delete routine returns. </summary>
public static class DeleteResult
{
    public const int Deleted = 0;
    public const int BlockedByForeignKey = -1;
    public const int Failed = -2;
}
