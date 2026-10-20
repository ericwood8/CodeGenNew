namespace CodeGenNew.Core;

/// <summary> The C# values the generated unit tests put in a column: two different ones per type, so a copy can be told from a changed one. </summary>
public static class TestSampleValues
{
    /// <summary> The C# expression for value number <paramref name="n"/> (1 or 2) of the column's type; <c>null</c> for a type that has no sample. </summary>
    public static string Value(ColumnModel column, int n) => column.CSharpBase() switch
    {
        "int" => n.ToString(),
        "long" => n + "L",
        "short" => $"(short){n}",
        "byte" => $"(byte){n}",
        "bool" => n == 1 ? "true" : "false",
        "decimal" => n + ".5m",
        "double" => n + ".5",
        "float" => n + ".5f",
        "DateTime" => $"new DateTime(2026, 1, {14 + n}, 8, 30, 0)",
        "DateTimeOffset" => $"new DateTimeOffset(2026, 1, {14 + n}, 8, 30, 0, TimeSpan.Zero)",
        "TimeSpan" => $"TimeSpan.FromMinutes({480 + n})",
        "Guid" => $"new Guid(\"00000000-0000-0000-0000-00000000000{n}\")",
        "string" => n == 1 ? "\"Sample\"" : "\"Other\"",
        "byte[]" => n == 1 ? "new byte[] { 1, 2, 3 }" : "new byte[] { 4, 5 }",
        _ => "null"
    };

    /// <summary> True when <see cref="Value"/> has a sample for the column's type. </summary>
    public static bool Has(ColumnModel column) => column.CSharpBase() != "object";
}
