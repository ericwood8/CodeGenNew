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

    /// <summary> The RAISERROR severity of a mistake the caller can correct (16), and the state the routines use. </summary>
    public const int RuleSeverity = 16;
    public const int RuleState = 1;
}

/// <summary> The numbers each database raises for a refused write, which the generated API turns into a problem answer (ApiProblems): a duplicate value, a row still in use or a parent that is missing,
/// a value too long for its column, and a missing value. </summary>
public static class ConstraintErrors
{
    /// <summary> <c>Microsoft.Data.SqlClient.SqlException.Number</c>. </summary>
    public static class SqlServer
    {
        public const int UniqueConstraint = 2627;
        public const int UniqueIndex = 2601;
        public const int ForeignKey = SqlErrorCodes.SqlServerForeignKeyViolation;
        public const int StringTruncated = 8152;
        public const int StringTruncatedWithDetail = 2628;
        public const int NullNotAllowed = 515;
    }

    /// <summary> <c>Npgsql.PostgresException.SqlState</c>. </summary>
    public static class PostgreSql
    {
        public const string UniqueViolation = "23505";
        public const string ForeignKeyViolation = "23503";
        public const string StringDataRightTruncation = "22001";
        public const string NotNullViolation = "23502";
    }

    /// <summary> <c>MySqlException.Number</c>. </summary>
    public static class MySql
    {
        public const int DuplicateEntry = 1062;
        public const int RowIsReferenced = SqlErrorCodes.MySqlForeignKeyViolation;
        public const int NoReferencedRow = 1452;
        public const int DataTooLong = 1406;
        public const int ColumnCannotBeNull = 1048;
    }

    /// <summary> <c>SqliteException.SqliteExtendedErrorCode</c>. </summary>
    public static class Sqlite
    {
        public const int UniqueConstraint = 2067;
        public const int PrimaryKeyConstraint = 1555;
        public const int ForeignKeyConstraint = 787;
        public const int NotNullConstraint = 1299;
    }
}

/// <summary> What a generated delete routine returns. </summary>
public static class DeleteResult
{
    public const int Deleted = 0;
    public const int BlockedByForeignKey = -1;
    public const int Failed = -2;
}
