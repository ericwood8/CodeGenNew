using System.Text;

namespace CodeGenNew.TemplateEngine;

/// <summary> A small line diff (longest common subsequence) written the way a unified diff reads: <c>-</c> for a line only in the old text, <c>+</c> for one only in the new, a few unchanged
/// lines around each change. Enough to see what regenerating would change in a file; it is not a patch tool. </summary>
public static class TextDiff
{
    /// <summary> Null when the two texts are the same (line endings ignored). </summary>
    public static string? Unified(string oldText, string newText, string? name = null, int context = 3)
    {
        var a = Lines(oldText);
        var b = Lines(newText);
        if (a.SequenceEqual(b))
            return null;

        // LCS table over the lines between the common head and tail (the usual case is a few changed lines in a long file)
        int head = 0;
        while (head < a.Length && head < b.Length && a[head] == b[head]) head++;
        int tail = 0;
        while (tail < a.Length - head && tail < b.Length - head && a[^(tail + 1)] == b[^(tail + 1)]) tail++;
        var midA = a[head..(a.Length - tail)];
        var midB = b[head..(b.Length - tail)];

        var ops = new List<(char Kind, string Line)>();
        for (int i = 0; i < head; i++) ops.Add((' ', a[i]));
        ops.AddRange(Edit(midA, midB));
        for (int i = a.Length - tail; i < a.Length; i++) ops.Add((' ', a[i]));

        var sb = new StringBuilder();
        if (name is not null)
            sb.Append("--- ").Append(name).Append('\n').Append("+++ ").Append(name).Append(" (regenerated)\n");
        // keep only the changes with `context` lines around them
        var keep = new bool[ops.Count];
        for (int i = 0; i < ops.Count; i++)
            if (ops[i].Kind != ' ')
                for (int j = Math.Max(0, i - context); j <= Math.Min(ops.Count - 1, i + context); j++) keep[j] = true;
        bool gap = false;
        for (int i = 0; i < ops.Count; i++)
        {
            if (!keep[i]) { gap = true; continue; }
            if (gap && sb.Length > 0) sb.Append("...\n");
            gap = false;
            sb.Append(ops[i].Kind).Append(' ').Append(ops[i].Line).Append('\n');
        }
        return sb.ToString();
    }

    private static string[] Lines(string text) => text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    private static List<(char, string)> Edit(string[] a, string[] b)
    {
        int n = a.Length, m = b.Length;
        // a very large middle part is not worth a quadratic table: show it as one removal and one addition
        if ((long)n * m > 4_000_000)
            return [.. a.Select(l => ('-', l)), .. b.Select(l => ('+', l))];

        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var result = new List<(char, string)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { result.Add((' ', a[x])); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) result.Add(('-', a[x++]));
            else result.Add(('+', b[y++]));
        }
        while (x < n) result.Add(('-', a[x++]));
        while (y < m) result.Add(('+', b[y++]));
        return result;
    }
}
