using System.Text.RegularExpressions;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> Every problem answer the generated API gives names its kind with a <see cref="ProblemTypes"/> value, and no two kinds share one. </summary>
[TestClass]
public class ProblemTypesTests
{
    private static List<string> All() =>
        typeof(ProblemTypes).GetFields().Where(f => f.IsLiteral && f.Name != nameof(ProblemTypes.Prefix)).Select(f => (string)f.GetRawConstantValue()!).ToList();

    [TestMethod]
    public void Each_kind_of_problem_has_its_own_lower_case_path()
    {
        var all = All();
        Assert.IsGreaterThan(10, all.Count);
        CollectionAssert.AreEqual(all.Distinct().ToList(), all);
        foreach (string type in all)
            Assert.IsTrue(Regex.IsMatch(type, "^" + Regex.Escape(ProblemTypes.Prefix) + "[a-z]+(-[a-z]+)*$"), type);
    }

    [TestMethod]
    public void Every_problem_a_template_answers_names_its_type()
    {
        var missing = new List<string>();
        foreach (string path in Directory.GetFiles(Repo.TemplatesDirectory, "API_*.tt"))
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Contains("Results.Problem(statusCode:") && !lines[i].Contains("type:"))
                    missing.Add($"{Path.GetFileName(path)}:{i + 1}");
        }
        Assert.IsEmpty(missing, "add type: <#= ProblemTypes.X #> to " + string.Join(", ", missing));
    }

    [TestMethod]
    public void Every_kind_of_problem_is_used_by_a_template()
    {
        // the API templates, and the writers in Core that write the text of an API template (the people routes)
        string templates = string.Concat(Directory.GetFiles(Repo.TemplatesDirectory, "API_*.tt").Select(File.ReadAllText))
            + string.Concat(Directory.GetFiles(Path.Combine(Repo.Root, "CodeGenNew.Core"), "*PeopleWriter.cs").Select(File.ReadAllText));
        foreach (var field in typeof(ProblemTypes).GetFields().Where(f => f.IsLiteral && f.Name != nameof(ProblemTypes.Prefix)))
            Expect.Contains(templates, "ProblemTypes." + field.Name);
    }
}
