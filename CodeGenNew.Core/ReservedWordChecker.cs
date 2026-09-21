namespace CodeGenNew.Core;

/// <summary> Flags table/column names that collide with SQL Server or C# reserved words (Docs/specs.md section 9.4). </summary>
public static class ReservedWordChecker
{
    // ODBC/T-SQL reserved keywords (Microsoft Learn: "Reserved Keywords (Transact-SQL)" + ODBC reserved words).
    // Not exhaustive of every future-reserved word, but covers the practical set a table/column name could collide with.
    private static readonly HashSet<string> SqlServerReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "ADD","ALL","ALTER","AND","ANY","AS","ASC","AUTHORIZATION","BACKUP","BEGIN","BETWEEN","BREAK","BROWSE",
        "BULK","BY","CASCADE","CASE","CHECK","CHECKPOINT","CLOSE","CLUSTERED","COALESCE","COLLATE","COLUMN",
        "COMMIT","COMPUTE","CONSTRAINT","CONTAINS","CONTAINSTABLE","CONTINUE","CONVERT","CREATE","CROSS",
        "CURRENT","CURRENT_DATE","CURRENT_TIME","CURRENT_TIMESTAMP","CURRENT_USER","CURSOR","DATABASE",
        "DBCC","DEALLOCATE","DECLARE","DEFAULT","DELETE","DENY","DESC","DISK","DISTINCT","DISTRIBUTED","DOUBLE",
        "DROP","DUMP","ELSE","END","ERRLVL","ESCAPE","EXCEPT","EXEC","EXECUTE","EXISTS","EXIT","EXTERNAL",
        "FETCH","FILE","FILLFACTOR","FOR","FOREIGN","FREETEXT","FREETEXTTABLE","FROM","FULL","FUNCTION","GOTO",
        "GRANT","GROUP","HAVING","HOLDLOCK","IDENTITY","IDENTITY_INSERT","IDENTITYCOL","IF","IN","INDEX",
        "INNER","INSERT","INTERSECT","INTO","IS","JOIN","KEY","KILL","LEFT","LIKE","LINENO","LOAD","MERGE",
        "NATIONAL","NOCHECK","NONCLUSTERED","NOT","NULL","NULLIF","OF","OFF","OFFSETS","ON","OPEN","OPENDATASOURCE",
        "OPENQUERY","OPENROWSET","OPENXML","OPTION","OR","ORDER","OUTER","OVER","PERCENT","PIVOT","PLAN",
        "PRECISION","PRIMARY","PRINT","PROC","PROCEDURE","PUBLIC","RAISERROR","READ","READTEXT","RECONFIGURE",
        "REFERENCES","REPLICATION","RESTORE","RESTRICT","RETURN","REVERT","REVOKE","RIGHT","ROLLBACK","ROWCOUNT",
        "ROWGUIDCOL","RULE","SAVE","SCHEMA","SECURITYAUDIT","SELECT","SEMANTICKEYPHRASETABLE",
        "SEMANTICSIMILARITYDETAILSTABLE","SEMANTICSIMILARITYTABLE","SESSION_USER","SET","SETUSER","SHUTDOWN",
        "SOME","STATISTICS","SYSTEM_USER","TABLE","TABLESAMPLE","TEXTSIZE","THEN","TO","TOP","TRAN","TRANSACTION",
        "TRIGGER","TRUNCATE","TRY_CONVERT","TSEQUAL","UNION","UNIQUE","UNPIVOT","UPDATE","UPDATETEXT","USE",
        "USER","VALUES","VARYING","VIEW","WAITFOR","WHEN","WHERE","WHILE","WITH","WITHIN GROUP","WRITETEXT"
    };

    private static readonly HashSet<string> CSharpReservedWords = new(StringComparer.Ordinal)
    {
        "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const","continue",
        "decimal","default","delegate","do","double","else","enum","event","explicit","extern","false","finally",
        "fixed","float","for","foreach","goto","if","implicit","in","int","interface","internal","is","lock",
        "long","namespace","new","null","object","operator","out","override","params","private","protected",
        "public","readonly","ref","return","sbyte","sealed","short","sizeof","stackalloc","static","string",
        "struct","switch","this","throw","true","try","typeof","uint","ulong","unchecked","unsafe","ushort",
        "using","virtual","void","volatile","while"
    };

    public static bool IsSqlReservedWord(string name) => SqlServerReservedWords.Contains(name);

    /// <summary> Case-sensitive: C# keywords are only reserved in their lowercase form. </summary>
    public static bool IsCSharpReservedWord(string name) => CSharpReservedWords.Contains(name);
}
