namespace CodeGenNew.TemplateEngine;

/// <summary> Parses a &lt;TemplateName&gt;.tt.config file (Docs/specs.md section 5.3). Both restrictions
/// default to true when the file is missing entirely (conservative default for a hand-added .tt file). </summary>
public class TemplateConfig
{
    public bool RequiresPrimaryKey { get; init; } = true;
    public bool TableOnly { get; init; } = true;

    /// <summary> Unlike the two restrictions above (which default to true), this defaults to false: only a
    /// template that generates from the table's actual data (e.g. SP_Load.tt) asks for its rows to be read. </summary>
    public bool NeedsRowData { get; init; }

    /// <summary> Defaults to false. A template that shows the display columns of foreign-keyed tables (SP_Lookup) asks
    /// for them to be looked up (ForeignKeyModel.ReferencedDisplayColumns). </summary>
    public bool NeedsReferencedDisplayColumns { get; init; }

    /// <summary> Optional. When set, the generated file is named by this pattern instead of "<Table>_<Suffix>.<ext>"; the token
    /// {Table} is replaced by the table name (e.g. "{Table}Api.cs" gives E_DonateLeaveApi.cs). </summary>
    public string? OutputName { get; init; }

    public static TemplateConfig Load(string ttConfigPath)
    {
        if (!File.Exists(ttConfigPath))
            return new TemplateConfig();

        bool requiresPrimaryKey = true;
        bool tableOnly = true;
        bool needsRowData = false;
        bool needsReferencedDisplayColumns = false;
        string? outputName = null;

        foreach (string rawLine in File.ReadAllLines(ttConfigPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            int equalsIndex = line.IndexOf('=');
            if (equalsIndex < 0)
                continue;

            string key = line[..equalsIndex].Trim();
            string value = line[(equalsIndex + 1)..].Trim();
            bool boolValue = value.Equals("true", StringComparison.OrdinalIgnoreCase);

            if (key.Equals("RequiresPrimaryKey", StringComparison.OrdinalIgnoreCase))
                requiresPrimaryKey = boolValue;
            else if (key.Equals("TableOnly", StringComparison.OrdinalIgnoreCase))
                tableOnly = boolValue;
            else if (key.Equals("NeedsRowData", StringComparison.OrdinalIgnoreCase))
                needsRowData = boolValue;
            else if (key.Equals("NeedsReferencedDisplayColumns", StringComparison.OrdinalIgnoreCase))
                needsReferencedDisplayColumns = boolValue;
            else if (key.Equals("OutputName", StringComparison.OrdinalIgnoreCase))
                outputName = value.Length > 0 ? value : null;
        }

        return new TemplateConfig { RequiresPrimaryKey = requiresPrimaryKey, TableOnly = tableOnly, NeedsRowData = needsRowData, NeedsReferencedDisplayColumns = needsReferencedDisplayColumns, OutputName = outputName };
    }
}
