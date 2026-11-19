using System.Reflection;
using Mono.TextTemplating;

namespace CodeGenNew.TemplateEngine;

/// <summary> The namespaces under <c>CodeGenNew.Core</c> (Api, Python, Rust and the others) imported into every template, so a template that imports <c>CodeGenNew.Core</c> sees every type of the library as it did when they were one namespace. </summary>
internal static class CoreNamespaces
{
    private static readonly string[] Names = typeof(CodeGenNew.Core.TableModel).Assembly.GetTypes()
        .Select(t => t.Namespace).OfType<string>().Where(n => n.StartsWith("CodeGenNew.Core.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static void AddTo(TemplateGenerator generator)
    {
        foreach (string name in Names)
            if (!generator.Imports.Contains(name))
                generator.Imports.Add(name);
    }
}
