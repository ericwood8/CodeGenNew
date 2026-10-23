namespace CodeGenNew.Core;

public enum PgTextFamily { Array, Interval, Range, Geometric }

/// <summary> A PostgreSQL array, interval, range or geometric column as the generator sees it: a text column whose text is the value in PostgreSQL's own notation
/// (<c>{1,2}</c>, <c>1 day 02:03:04</c>, <c>[1,5)</c>, <c>(1,2)</c>). Every front end edits it in a text box; the database is told the real type, by a cast in the
/// raw SQL of the Python and Rust stacks and by a value converter in the EF Core context of the C# API. <see cref="Name"/> is the type as a cast writes it
/// (<c>int4[]</c>, <c>interval</c>, <c>int4range</c>, <c>point</c>). </summary>
public sealed record PgTextType(string Name, PgTextFamily Family, string? Element)
{
    private const string Number = @"[-+]?\d+(\.\d+)?([eE][-+]?\d+)?";
    private const string Whole = @"[-+]?\d+";
    private const string Day = @"\d{4}-\d{2}-\d{2}";
    private const string Point = @"\(\s*" + Number + @"\s*,\s*" + Number + @"\s*\)";

    private static readonly Dictionary<string, string> ArrayElements = new(StringComparer.Ordinal)
    {
        ["_text"] = "text", ["_varchar"] = "varchar", ["_bpchar"] = "bpchar", ["_int2"] = "int2", ["_int4"] = "int4", ["_int8"] = "int8",
        ["_numeric"] = "numeric", ["_float4"] = "float4", ["_float8"] = "float8", ["_bool"] = "bool", ["_uuid"] = "uuid", ["_date"] = "date"
    };

    private static readonly string[] Ranges = ["int4range", "int8range", "numrange", "daterange"];
    private static readonly string[] Geometrics = ["point", "line", "lseg", "box", "path", "polygon", "circle"];

    /// <summary> The type of a catalog type name (<c>_int4</c>, <c>interval</c>, <c>int4range</c>, <c>point</c>), or null for any other type. </summary>
    public static PgTextType? FromUdt(string udtName)
    {
        string udt = udtName.ToLowerInvariant();
        if (ArrayElements.TryGetValue(udt, out var element))
            return new PgTextType(element + "[]", PgTextFamily.Array, element);
        if (udt == "interval")
            return new PgTextType("interval", PgTextFamily.Interval, null);
        if (Array.IndexOf(Ranges, udt) >= 0)
            return new PgTextType(udt, PgTextFamily.Range, udt[..^5]);
        return Array.IndexOf(Geometrics, udt) >= 0 ? new PgTextType(udt, PgTextFamily.Geometric, null) : null;
    }

    /// <summary> The type of a cast name as <see cref="Name"/> writes it, or null. </summary>
    public static PgTextType? FromName(string? name)
    {
        if (string.IsNullOrEmpty(name))
            return null;
        return FromUdt(name.EndsWith("[]", StringComparison.Ordinal) ? "_" + name[..^2] : name);
    }

    /// <summary> The characters the column holds at most: long enough for the usual value, short enough that the form keeps a one-line box. </summary>
    public int MaxLength => Family is PgTextFamily.Array || Name is "path" or "polygon" ? 255 : 64;

    /// <summary> The C# type of one element: <c>string</c>, <c>int</c>, <c>DateOnly</c> ... (an array's element, or the bound of a range). </summary>
    public string? ClrElement => Family switch
    {
        PgTextFamily.Array => Element switch
        {
            "text" or "varchar" or "bpchar" => "string",
            "int2" => "short", "int4" => "int", "int8" => "long", "numeric" => "decimal", "float4" => "float", "float8" => "double",
            "bool" => "bool", "uuid" => "Guid", _ => "DateOnly"
        },
        PgTextFamily.Range => Element switch { "int4" => "int", "int8" => "long", "num" => "decimal", _ => "DateOnly" },
        _ => null
    };

    /// <summary> What a person types, for a message: "an array of integers". </summary>
    public string Description => Family switch
    {
        PgTextFamily.Array => $"an array of {ElementWords} (items between braces, separated by commas)",
        PgTextFamily.Interval => "a time span such as 1 day 02:03:04",
        PgTextFamily.Range => $"a range of {ElementWords} such as [1,5)",
        _ => Name switch
        {
            "point" => "a point such as (1,2)",
            "line" => "a line (three numbers between braces)",
            "lseg" => "a line segment such as [(1,1),(2,2)]",
            "box" => "a box such as (2,2),(1,1)",
            "path" => "a path such as ((1,1),(2,2)) or [(1,1),(2,2)]",
            "polygon" => "a polygon such as ((1,1),(2,2),(3,1))",
            _ => "a circle such as <(1,1),2>"
        }
    };

    private string ElementWords => ClrElement switch
    {
        "string" => "text", "short" or "int" or "long" => "whole numbers", "decimal" or "float" or "double" => "numbers",
        "bool" => "flags written t or f", "Guid" => "ids", _ => "dates"
    };

    private string ElementPattern => ClrElement switch
    {
        "short" or "int" or "long" => Whole,
        "decimal" or "float" or "double" => Number,
        "bool" => "t|f|true|false",
        "Guid" => "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        "DateOnly" => Day,
        _ => @"""([^""\\]|\\.)*""|[^,{}""\\]+"
    };

    /// <summary> The regular expression a whole value must match (no anchors; in the syntax every language of the generated stacks shares), so a malformed value is refused as a
    /// validation error and never reaches the database or the converter. </summary>
    public string Pattern => Anchored[1..^1];

    private string Anchored
    {
        get
        {
            string e = ElementPattern;
            switch (Family)
            {
                case PgTextFamily.Array:
                    return @"^\{\s*(" + $"({e})(\\s*,\\s*({e}))*" + @")?\s*\}$";
                case PgTextFamily.Interval:
                    const string unit = @"[-+]?\d+(\.\d+)?\s*(days?|d|hours?|h|mins?|minutes?|m|secs?|seconds?|s)";
                    const string clock = @"[-+]?\d+:\d{2}(:\d{2}(\.\d+)?)?";
                    return @"^\s*(" + unit + "|" + clock + @")(\s+(" + unit + "|" + clock + @"))*\s*$";
                case PgTextFamily.Range:
                    string bound = ClrElement switch { "int" or "long" => Whole, "decimal" => Number, _ => Day };
                    return @"^(empty|[\[(]\s*(" + bound + @")?\s*,\s*(" + bound + @")?\s*[\])])$";
                default:
                    string list = Point + @"(\s*,\s*" + Point + ")*";
                    return Name switch
                    {
                        "point" => "^" + Point + "$",
                        "line" => @"^\{\s*" + Number + @"\s*,\s*" + Number + @"\s*,\s*" + Number + @"\s*\}$",
                        "lseg" => @"^\[\s*" + Point + @"\s*,\s*" + Point + @"\s*\]$",
                        "box" => "^" + Point + @"\s*,\s*" + Point + "$",
                        "path" => @"^(\(\s*" + list + @"\s*\)|\[\s*" + list + @"\s*\])$",
                        "polygon" => @"^\(\s*" + list + @"\s*\)$",
                        _ => @"^<\s*" + Point + @"\s*,\s*" + Number + @"\s*>$"
                    };
            }
        }
    }

    /// <summary> A value that passes <see cref="Pattern"/> and is written the way PostgreSQL and the converter write it back, so a test that posts it and reads it again sees the same text. </summary>
    public string Sample => Family switch
    {
        PgTextFamily.Array => ClrElement switch
        {
            "string" => "{a,b}", "bool" => "{t,f}", "Guid" => "{00000000-0000-0000-0000-000000000001}", "DateOnly" => "{2024-01-02,2024-03-04}",
            "decimal" or "float" or "double" => "{1.5,2.5}", _ => "{1,2}"
        },
        PgTextFamily.Interval => "1 day 02:03:04",
        PgTextFamily.Range => ClrElement switch
        {
            "decimal" => "[1.5,5.5)", "DateOnly" => "[2024-01-01,2024-02-01)", _ => "[1,5)"
        },
        _ => Name switch
        {
            "point" => "(1,2)", "line" => "{1,2,3}", "lseg" => "[(1,1),(2,2)]", "box" => "(2,2),(1,1)", "path" => "((1,1),(2,2))",
            "polygon" => "((1,1),(2,2),(3,1))", _ => "<(1,1),2>"
        }
    };

    /// <summary> The C# expression of the EF Core value converter (a member of the generated <c>PgText</c> class) between the text and the type Npgsql maps this column to. </summary>
    public string Converter(bool nullable) => Family switch
    {
        PgTextFamily.Array when ClrElement == "string" => $"PgText.TextArray({(nullable ? "true" : "false")})",
        PgTextFamily.Array => $"PgText.Array<{ClrElement}>({(nullable ? "true" : "false")})",
        PgTextFamily.Interval => "PgText.Interval()",
        PgTextFamily.Range => $"PgText.Range<{ClrElement}>()",
        _ => "PgText." + Name switch
        {
            "point" => "Point", "line" => "Line", "lseg" => "LineSegment", "box" => "Box", "path" => "Path", "polygon" => "Polygon", _ => "Circle"
        } + "()"
    };
}
