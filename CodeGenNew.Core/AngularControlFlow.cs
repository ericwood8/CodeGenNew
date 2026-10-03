using System.Text.RegularExpressions;

namespace CodeGenNew.Core;

/// <summary> Rewrites the structural directives of a generated Angular template (<c>*ngIf</c>, <c>*ngFor</c>, <c>*ngIf="x; else tpl"</c> with its <c>&lt;ng-template&gt;</c>) into
/// the built-in control flow (<c>@if</c>, <c>@for</c>, <c>@else</c>) that Angular 18 and later prefer (the old directives are deprecated from 20). The generated html is plain and
/// regular (no self-closing wrapped elements, no <c>&gt;</c> outside a quoted attribute value), so a small tag scanner is enough. </summary>
public static class AngularControlFlow
{
    private static readonly Regex Directive = new("\\*ng(?<kind>If|For)=\"(?<expr>[^\"]*)\"", RegexOptions.Compiled);
    private static readonly Regex ForExpression = new("^\\s*let\\s+(?<item>\\w+)\\s+of\\s+(?<list>.+?)\\s*$", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex IfElse = new("^(?<cond>.*?)\\s*;\\s*else\\s+(?<template>\\w+)\\s*$", RegexOptions.Compiled | RegexOptions.Singleline);

    public static string Convert(string html)
    {
        // Each pass converts the first directive in the text; an element nested inside it still has its own directive, which a later pass finds.
        for (int guard = 0; guard < 10_000; guard++)
        {
            var match = Directive.Match(html);
            if (!match.Success)
                return html;
            html = ConvertOne(html, match);
        }
        throw new InvalidOperationException("The generated html has more structural directives than the control-flow converter allows.");
    }

    private static string ConvertOne(string html, Match directive)
    {
        int tagStart = html.LastIndexOf('<', directive.Index);
        int openEnd = EndOfTag(html, tagStart);
        string tagName = Regex.Match(html[(tagStart + 1)..], "^[\\w-]+").Value;
        int elementEnd = EndOfElement(html, tagName, openEnd + 1);

        // the element without its directive attribute
        string open = html[tagStart..(openEnd + 1)];
        int at = directive.Index - tagStart;
        bool spaceBefore = at > 0 && open[at - 1] == ' ';
        open = open.Remove(spaceBefore ? at - 1 : at, directive.Length + (spaceBefore ? 1 : 0));
        string element = open + html[(openEnd + 1)..elementEnd];

        // the line's own indentation, for the lines that wrap the element
        int lineStart = html.LastIndexOf('\n', tagStart) + 1;
        string beforeTag = html[lineStart..tagStart];
        string indent = new(beforeTag.TakeWhile(char.IsWhiteSpace).ToArray());
        string prefix = beforeTag.All(char.IsWhiteSpace) ? html[..lineStart] + indent : html[..tagStart] + "\n" + indent;

        string expr = directive.Groups["expr"].Value;
        string rest = html[elementEnd..];
        string core;
        if (directive.Groups["kind"].Value == "For")
        {
            var parts = ForExpression.Match(expr);
            if (!parts.Success)
                throw new InvalidOperationException($"Cannot convert *ngFor=\"{expr}\".");
            core = $"@for ({parts.Groups["item"].Value} of {parts.Groups["list"].Value}; track $index) {{\n{indent}    {element}\n{indent}}}";
        }
        else if (IfElse.Match(expr) is { Success: true } withElse)
        {
            string name = withElse.Groups["template"].Value;
            var template = Regex.Match(rest, "[ \\t]*<ng-template #" + name + ">(?<inner>.*?)</ng-template>[ \\t]*\\r?\\n?", RegexOptions.Singleline);
            if (!template.Success)
                throw new InvalidOperationException($"*ngIf refers to <ng-template #{name}>, which is not in the html after it.");
            core = $"@if ({withElse.Groups["cond"].Value}) {{\n{indent}    {element}\n{indent}}} @else {{\n{indent}    {template.Groups["inner"].Value.Trim()}\n{indent}}}";
            rest = rest.Remove(template.Index, template.Length);
        }
        else
        {
            core = $"@if ({expr}) {{\n{indent}    {element}\n{indent}}}";
        }
        return prefix + core + rest;
    }

    /// <summary> The index of the <c>&gt;</c> that closes the tag starting at <paramref name="tagStart"/>, skipping quoted attribute values. </summary>
    private static int EndOfTag(string html, int tagStart)
    {
        char quote = '\0';
        for (int i = tagStart; i < html.Length; i++)
        {
            char c = html[i];
            if (quote != '\0') { if (c == quote) quote = '\0'; }
            else if (c == '"' || c == '\'') quote = c;
            else if (c == '>') return i;
        }
        throw new InvalidOperationException("An html tag is not closed.");
    }

    /// <summary> The index just after the closing tag that matches the element whose opening tag ended just before <paramref name="from"/>. </summary>
    private static int EndOfElement(string html, string tagName, int from)
    {
        int depth = 1;
        var tag = new Regex("<(?<close>/?)" + Regex.Escape(tagName) + "(?=[\\s>/])");
        int position = from;
        while (depth > 0)
        {
            var match = tag.Match(html, position);
            if (!match.Success)
                throw new InvalidOperationException($"<{tagName}> is not closed.");
            int end = EndOfTag(html, match.Index);
            if (match.Groups["close"].Length > 0) depth--;
            else if (html[end - 1] != '/') depth++;
            position = end + 1;
        }
        return position;
    }
}
