using System.Globalization;

namespace CodeGenNew.Core;

/// <summary> The scenario the generated Rust test of a change gate (RS_TestGates) plays: the values that make the gate's condition hold or fail, found from the rule alone. Only a rule of a plain shape
/// has one: the gated column moves to equal values, the condition is terms joined by <c>and</c> that each compare for equality, and so is the exemption. Every other rule is left to a hand-written test. </summary>
public sealed record RustGateScenario(ChangeGate Gate, RustGateKind Kind, IReadOnlyList<GateTerm> When, IReadOnlyList<GateTerm> Unless)
{
    /// <summary> The value of a term that makes it hold, as a <c>json!</c> argument. </summary>
    public string Holds(GateTerm term) => GateCode.RustLiteral(KindOf(term), term.Values[0]);

    /// <summary> A value of the term's column that makes it fail, as a <c>json!</c> argument. </summary>
    public string Fails(GateTerm term) => GateCode.RustLiteral(KindOf(term), RustGateTests.Other(KindOf(term), term.Values[0]));

    /// <summary> The values the gated column may not move to, as <c>json!</c> arguments. </summary>
    public IEnumerable<string> Targets => Gate.To.Values.Select(v => GateCode.RustLiteral(Kind, v));

    /// <summary> The value a new row must start with, as a <c>json!</c> argument; null when the rule has no start. </summary>
    public string? Start => Gate.Start is null ? null : GateCode.RustLiteral(Kind, Gate.Start);

    /// <summary> A value a new row may not start with (the start value changed); null when the rule has no start. </summary>
    public string? NotStart => Gate.Start is null ? null : GateCode.RustLiteral(Kind, RustGateTests.Other(Kind, Gate.Start));

    private static RustGateKind KindOf(GateTerm term) => GateCode.RustKindOf(term.Column, term.Table.Dialect)!.Value;
}

/// <summary> Finds the scenario of a gate, and the values the Rust tests of gates and guards invent. </summary>
public static class RustGateTests
{
    /// <summary> A value different from <paramref name="value"/> in the same column: a number one higher, the other yes / no, a text with a letter added. </summary>
    public static string Other(RustGateKind kind, string value) => kind switch
    {
        RustGateKind.Bool => value == "true" ? "false" : "true",
        RustGateKind.Int => (long.Parse(value, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture),
        _ => value + "x"
    };

    /// <summary> The scenario of a gate, or null when the rule is not of the plain shape or a table it reads has no REST API to set a value through. </summary>
    public static RustGateScenario? Scenario(ChangeGate gate, DatabaseModel database, ProjectSettings project)
    {
        var withApi = database.CrudApiTables(project).Select(t => t.TableName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!withApi.Contains(gate.Table.TableName) || GateCode.RustKindOf(gate.Column, database.Dialect) is not { } kind)
            return null;
        if (gate.To.Operator is not (GateOperator.Equal or GateOperator.In) || gate.To.Values.Count == 0 || gate.When.AnyOf.Count != 1 || gate.Unless is { AnyOf.Count: not 1 })
            return null;
        var when = gate.When.AnyOf[0];
        var unless = gate.Unless?.AnyOf[0] ?? [];
        if (when.Count == 0 || (gate.Unless is not null && unless.Count == 0))
            return null;
        foreach (var term in when.Concat(unless))
        {
            bool plain = term.Operator == GateOperator.Equal && term.Values.Count == 1 && GateCode.RustKindOf(term.Column, database.Dialect) is not null;
            bool reachable = term.Via is null
                ? !term.Column.Name.EqualsIgnoreCase(gate.Column.Name)
                : term.Via.ReferencingColumns.Count == 1 && withApi.Contains(term.Table.TableName);
            if (!plain || !reachable)
                return null;
        }
        return new RustGateScenario(gate, kind, when, unless);
    }
}
