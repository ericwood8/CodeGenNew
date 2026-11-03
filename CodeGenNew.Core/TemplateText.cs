namespace CodeGenNew.Core;

/// <summary> Text blocks with <c>§</c> tokens, filled in one place. </summary>
public static class TemplateText
{
    /// <summary> Replaces each token with its value, the longest token first so a token that starts another one is not cut. Text that is filled in stages may keep tokens for a later call. </summary>
    public static string Fill(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        return text;
    }

    /// <summary> <see cref="Fill"/> for the last call on a text: it throws when a token is still there, so a token that nobody fills is found when the text is made and not when the generated code is built. </summary>
    public static string FillAll(string text, params (string Token, string Value)[] values)
    {
        foreach (var (token, value) in values.OrderByDescending(v => v.Token.Length))
            text = text.Replace(token, value);
        int left = text.IndexOf('§');
        if (left >= 0)
            throw new InvalidOperationException("A token was not filled: " + new string(text.Skip(left).TakeWhile(c => !char.IsWhiteSpace(c)).Take(30).ToArray()));
        return text;
    }
}
