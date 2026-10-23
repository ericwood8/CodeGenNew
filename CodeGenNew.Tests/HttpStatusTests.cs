using System.Text.RegularExpressions;
using CodeGenNew.Core;

namespace CodeGenNew.Tests;

/// <summary> The status numbers the generator writes come from <see cref="HttpStatus"/>, never from a bare number in a template. </summary>
[TestClass]
public class HttpStatusTests
{
    [TestMethod]
    public void Every_status_has_the_ASP_NET_name_that_carries_its_number()
    {
        foreach (var field in typeof(HttpStatus).GetFields().Where(f => f.IsLiteral))
        {
            int status = (int)field.GetRawConstantValue()!;
            string name = HttpStatus.AspNetName(status);
            Expect.Contains(name, "StatusCodes.Status" + status);
        }
        Assert.Throws<ArgumentOutOfRangeException>(() => HttpStatus.AspNetName(418));
    }

    [TestMethod]
    public void The_exit_codes_are_the_usual_pair()
    {
        int[] codes = [ExitCodes.Success, ExitCodes.Failure];
        Assert.AreEqual(0, codes[0]);
        Assert.AreEqual(2, codes.Distinct().Count());
    }

    // where a template writes a status into code: a call, a comparison or a keyword argument of the generated C#, TypeScript, Python or Rust (a number in a comment or in prose is left alone)
    private static readonly Regex[] BareStatus =
    [
        new(@"ProducesProblem\(\d{3}\)"),
        new(@"statusCode: \d{3}"),
        new(@"\bstatus ?===? ?\d{3}"),
        new(@"\.Status ?== ?\d{3}"),
        new(@"\bApiError\(\d{3},"),
        new(@"\bstatus_code=\d{3}"),
        new(@"\bsubmit\(\d{3}\)"),
        new(@"\b_problem\(\d{3},"),
        new(@"\bstatus: \d{3}"),
        new(@"""status"": \d{3}"),
        new(@"=\d{3};"),
        new(@"^\s+\d{3} => ")
    ];

    [TestMethod]
    public void No_template_writes_a_bare_status_number_into_code()
    {
        var found = new List<string>();
        foreach (string path in Directory.GetFiles(Repo.TemplatesDirectory, "*.tt"))
        {
            var lines = File.ReadAllLines(path);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].TrimStart();
                if (line.StartsWith("//", StringComparison.Ordinal) || line.StartsWith("///", StringComparison.Ordinal))
                    continue;
                if (BareStatus.Any(rx => rx.IsMatch(lines[i])))
                    found.Add($"{Path.GetFileName(path)}:{i + 1}: {line.Trim()}");
            }
        }
        Assert.IsEmpty(found, "use HttpStatus in the template:\n" + string.Join("\n", found.Take(20)));
    }
}
