using CodeGenNew.Core;
using CodeGenNew.SchemaIntrospection;

namespace CodeGenNew.Tests;

/// <summary> Loads the actual shipped SpecialLogicColumns.config (not a fabricated one) and checks it
/// against real column-naming variety seen in a survey of ~35 databases on the author's own box
/// (2026-09-22): ALL-CAPS/snake_case audit columns that a case-sensitive or no-underscore pattern would
/// miss, and the new ModifiedUserColumn category. A future edit to the shipped file that silently drops
/// one of these back out is exactly what this guards against. </summary>
[TestClass]
public class SpecialLogicColumnsConfigTests
{
    private static readonly List<SpecialLogicRule> Rules = SpecialLogicColumnsConfig.Load(
        Path.Combine(Repo.Root, "SpecialLogicColumns.config"));

    /// <summary> Does this column name match one of the category's flag patterns? Works for both a per-column
    /// rule (CreateDateColumn, DisplayColumn, ...) and the flag side of a pair rule (IsActiveFlag, SoftDelete,
    /// StartEndDate) -- MatchesColumnRule itself only accepts the former, so a pair rule is checked directly
    /// against its FlagPatterns instead. </summary>
    private static bool MatchesColumn(string category, string columnName)
    {
        var rule = Rules.Single(r => r.Category.EqualsIgnoreCase(category));
        return rule.IsPairRule
            ? rule.FlagPatterns.Any(p => columnName.MatchesPattern(p, rule.IgnoreCase))
            : rule.MatchesColumnRule(columnName);
    }

    private static (string? Flag, string? Companion) MatchesPair(string category, params string[] columnNames)
    {
        var rule = Rules.Single(r => r.Category.EqualsIgnoreCase(category) && r.IsPairRule);
        var match = rule.EvaluatePairRule(columnNames);
        return match is null ? (null, null) : (match.Value.FlagColumn, match.Value.CompanionColumn);
    }

    [TestMethod]
    [DataRow("BEGIN_DATE", "END_DATE")] // ALL-CAPS snake_case, a real convention in several older sampled databases
    [DataRow("StartDate", "EndDate")]   // the original PascalCase shape, still matches after adding IgnoreCase
    public void StartEndDate_matches_regardless_of_case(string start, string end)
    {
        var (flag, companion) = MatchesPair("StartEndDate", start, end);
        Assert.AreEqual(start, flag);
        Assert.AreEqual(end, companion);
    }

    [TestMethod]
    [DataRow("is_active")]
    [DataRow("IsInActive")] // mixed/odd casing seen in the wild
    public void IsActiveFlag_matches_regardless_of_case_or_underscore(string columnName) =>
        Assert.IsTrue(MatchesColumn("IsActiveFlag", columnName));

    [TestMethod]
    public void SoftDelete_matches_the_underscore_spelling()
    {
        var (flag, companion) = MatchesPair("SoftDelete", "Is_Deleted", "Deleted_On");
        Assert.AreEqual("Is_Deleted", flag);
        Assert.AreEqual("Deleted_On", companion);
    }

    [TestMethod]
    [DataRow("Date_Created")]
    [DataRow("Created_On")]
    [DataRow("CreationDate")]
    [DataRow("CreateTime")]
    public void CreateDateColumn_matches_common_snake_case_and_synonym_spellings(string columnName) =>
        Assert.IsTrue(MatchesColumn("CreateDateColumn", columnName));

    [TestMethod]
    [DataRow("Date_Modified")]
    [DataRow("Modified_On")]
    [DataRow("Date_Updated")]
    [DataRow("UpdatedOn")]
    public void ModifiedDateColumn_matches_common_snake_case_and_synonym_spellings(string columnName) =>
        Assert.IsTrue(MatchesColumn("ModifiedDateColumn", columnName));

    [TestMethod]
    [DataRow("ModifiedBy")]
    [DataRow("UpdatedBy")]
    public void ModifiedUserColumn_matches_who_last_touched_the_row(string columnName) =>
        Assert.IsTrue(MatchesColumn("ModifiedUserColumn", columnName));

    [TestMethod]
    public void ModifiedUserColumn_and_ModifiedDateColumn_never_both_claim_the_same_column()
    {
        // Both categories are matched independently (there is no shared priority engine), so this is a
        // config-authoring invariant, not something the matching code enforces -- this test is the guard.
        foreach (var rule in Rules.Where(r => !r.IsPairRule))
        {
            if (!rule.Category.EqualsIgnoreCase("ModifiedDateColumn"))
                continue;

            var modifiedUserRule = Rules.Single(r => r.Category.EqualsIgnoreCase("ModifiedUserColumn"));
            foreach (string pattern in rule.FlagPatterns)
            {
                // A representative column name for this pattern (strip wildcards) must not also match ModifiedUserColumn.
                string sample = pattern.Trim('*');
                Assert.IsFalse(modifiedUserRule.MatchesColumnRule(sample),
                    $"ModifiedDateColumn pattern '{pattern}' collides with ModifiedUserColumn");
            }
        }
    }

    [TestMethod]
    public void DisplayColumn_recognizes_compound_description_and_label_names()
    {
        Assert.IsTrue(MatchesColumn("DisplayColumn", "AccountDescription")); // *Description, common suffix form
        Assert.IsTrue(MatchesColumn("DisplayColumn", "LegalDescription"));
        Assert.IsTrue(MatchesColumn("DisplayColumn", "Label"));
    }

    [TestMethod]
    [DataRow("PhotoFilePath")]
    [DataRow("LogoFile")]           // e.g. Company.APCheckLogoFile
    [DataRow("SignatureFile")]
    [DataRow("AttachmentFileName")]
    [DataRow("EmailAttachment")]
    public void FilePathColumn_matches_common_file_reference_spellings(string columnName) =>
        Assert.IsTrue(MatchesColumn("FilePathColumn", columnName));

    [TestMethod]
    [DataRow("FileSize")]   // an int, not a path -- must not falsely match
    [DataRow("ProfileId")]  // contains "file" only as a substring of "Profile", not as its own word
    public void FilePathColumn_does_not_match_unrelated_columns(string columnName) =>
        Assert.IsFalse(MatchesColumn("FilePathColumn", columnName));
}
