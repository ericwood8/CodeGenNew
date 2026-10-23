namespace CodeGenNew.Core;

/// <summary> The notation a text column that holds more than text must be written in: a regular expression the whole value matches (no anchors; the syntax every generated stack shares), what a value is
/// called in a message (no braces: a message goes through format strings) and a value that has it. Behind a PostgreSQL array, interval, range or shape, a MySQL set or spatial column
/// (see <see cref="ColumnModel.Notation"/>). </summary>
public sealed record TextNotation(string Pattern, string Noun, string Sample);

/// <summary> A MySQL spatial column as the generator sees it: a text column holding the value as well-known text (<c>POINT(1 2)</c>), longitude first as the WKB stores it, whatever the spatial reference
/// system says about the axis order. The raw SQL of the Rust and Python stacks and the stored routines read it with <c>ST_AsText</c> and write it with <c>ST_GeomFromText</c>; the C# context converts it to the
/// column's binary form. </summary>
public sealed record MySqlSpatialType(string Name)
{
    private const string Number = @"[-+]?\d+(?:\.\d+)?(?:[eE][-+]?\d+)?";
    private const string Coordinates = Number + @"\s+" + Number;
    private const string Point = @"\(\s*" + Coordinates + @"\s*\)";
    private const string Line = @"\(\s*" + Coordinates + @"(?:\s*,\s*" + Coordinates + @")+\s*\)";
    private const string Ring = @"\(\s*" + Coordinates + @"(?:\s*,\s*" + Coordinates + @"){3,}\s*\)";
    private const string Polygon = @"\(\s*" + Ring + @"(?:\s*,\s*" + Ring + @")*\s*\)";

    private static readonly string[] Names = ["geometry", "point", "linestring", "polygon", "multipoint", "multilinestring", "multipolygon", "geometrycollection"];

    /// <summary> The type of an information_schema DATA_TYPE (<c>point</c>, <c>geomcollection</c> ...), or null for any other type. </summary>
    public static MySqlSpatialType? FromDataType(string dataType)
    {
        string name = dataType.ToLowerInvariant();
        if (name == "geomcollection")
            name = "geometrycollection";
        return Array.IndexOf(Names, name) >= 0 ? new MySqlSpatialType(name) : null;
    }

    /// <summary> The type as <see cref="ColumnModel.DbSpatialType"/> keeps it. </summary>
    public static MySqlSpatialType? FromName(string? name) => name is null ? null : FromDataType(name);

    /// <summary> The characters the column holds at most. </summary>
    public int MaxLength => Name == "point" ? 64 : 4000;

    private static string Single(string name) => name switch
    {
        "point" => @"POINT\s*\(\s*" + Coordinates + @"\s*\)",
        "linestring" => @"LINESTRING\s*" + Line,
        "polygon" => @"POLYGON\s*" + Polygon,
        "multipoint" => @"MULTIPOINT\s*\(\s*(?:" + Point + "|" + Coordinates + @")(?:\s*,\s*(?:" + Point + "|" + Coordinates + @"))*\s*\)",
        "multilinestring" => @"MULTILINESTRING\s*\(\s*" + Line + @"(?:\s*,\s*" + Line + @")*\s*\)",
        _ => @"MULTIPOLYGON\s*\(\s*" + Polygon + @"(?:\s*,\s*" + Polygon + @")*\s*\)"
    };

    private static readonly string AnySingle = string.Join("|", new[] { "point", "linestring", "polygon", "multipoint", "multilinestring", "multipolygon" }.Select(Single));

    /// <summary> The regular expression a whole value must match (no anchors): the keyword in capitals as MySQL writes it, then the coordinates. </summary>
    public string Pattern => Name switch
    {
        "geometry" => "(?:" + AnySingle + @")|GEOMETRYCOLLECTION\s*\(\s*(?:" + AnySingle + @")(?:\s*,\s*(?:" + AnySingle + @"))*\s*\)|GEOMETRYCOLLECTION\s+EMPTY",
        "geometrycollection" => @"GEOMETRYCOLLECTION\s*\(\s*(?:" + AnySingle + @")(?:\s*,\s*(?:" + AnySingle + @"))*\s*\)|GEOMETRYCOLLECTION\s+EMPTY",
        _ => Single(Name)
    };

    public string Sample => Name switch
    {
        "linestring" => "LINESTRING(0 0,1 1)",
        "polygon" => "POLYGON((0 0,0 1,1 1,0 0))",
        "multipoint" => "MULTIPOINT((0 0),(1 1))",
        "multilinestring" => "MULTILINESTRING((0 0,1 1),(2 2,3 3))",
        "multipolygon" => "MULTIPOLYGON(((0 0,0 1,1 1,0 0)))",
        "geometrycollection" => "GEOMETRYCOLLECTION(POINT(1 2))",
        _ => "POINT(1 2)"
    };

    public string Description => Name switch
    {
        "point" => "a point such as POINT(1 2)",
        "linestring" => "a line such as LINESTRING(0 0,1 1)",
        "polygon" => "a polygon such as POLYGON((0 0,0 1,1 1,0 0))",
        "multipoint" => "a set of points such as MULTIPOINT((0 0),(1 1))",
        "multilinestring" => "a set of lines such as MULTILINESTRING((0 0,1 1),(2 2,3 3))",
        "multipolygon" => "a set of polygons such as MULTIPOLYGON(((0 0,0 1,1 1,0 0)))",
        "geometrycollection" => "a collection of shapes such as GEOMETRYCOLLECTION(POINT(1 2))",
        _ => "a shape in text form such as POINT(1 2)"
    };

    public TextNotation Notation => new(Pattern, Description, Sample);
}

/// <summary> A MySQL <c>set('a','b')</c> column: text holding some of the listed members, separated by commas and no spaces (<c>a,b</c>), which is how MySQL reads and writes it. </summary>
public static class MySqlSetNotation
{
    // only the characters a regular expression reads (an escaped space or letter is an error in some of the languages)
    private static string Escape(string text) => string.Concat(text.Select(c => @"\.^$|?*+()[]{}".Contains(c) ? "\\" + c : c.ToString()));

    /// <summary> The notation for the members: none, or any of them separated by commas. </summary>
    public static TextNotation Of(IReadOnlyList<string> members)
    {
        string one = "(?:" + string.Join("|", members.Select(Escape)) + ")";
        string list = members.Count > 3 ? string.Join(", ", members.Take(3)) + " ..." : string.Join(", ", members);
        return new TextNotation($@"(?:{one}(?:,{one})*)?", $"a list of {list} separated by commas, no spaces", members[0]);
    }
}
