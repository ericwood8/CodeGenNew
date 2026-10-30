namespace CodeGenNew.Core;

/// <summary> The C# statements of a junction table's List, Link and Unlink when the project reaches the database through EF Core (<see cref="AccessMode.Ef"/>): what SP_Junction's three routines do, written as LINQ over the
/// context so no routine has to exist. Used by API_Junction and WinUI3_JunctionEditor, which differ only in the context expression and the argument expressions they pass. Each method returns lines for a method body,
/// indented by <c>indent</c>. </summary>
public sealed class JunctionEf
{
    private readonly TableModel _model;
    private readonly ColumnModel _anchor;
    private readonly ColumnModel _target;
    private readonly ForeignKeyModel _anchorForeignKey;
    private readonly ForeignKeyModel _targetForeignKey;
    private readonly bool _tenanted;
    private readonly TenantOwned? _owned;

    /// <summary> With a <paramref name="project"/> that has a tenant API the calls check that the rows they name are visible to the caller (the context shows only the caller's tenant's rows) and stamp the
    /// caller's tenant on a link row of a tenant-owned junction table; without one the statements are the plain ones. </summary>
    public JunctionEf(TableModel model, ProjectSettings? project = null)
    {
        _model = model;
        _tenanted = project is not null && TenantPlan.HasApiTenancy(project);
        _owned = _tenanted ? TenantPlan.OwnedBy(model, project!) : null;
        var anchorFk = model.JunctionForeignKeys[0];
        _anchorForeignKey = anchorFk;
        _targetForeignKey = model.JunctionForeignKeys[1];
        _anchor = model.Columns.First(c => c.Name.EqualsIgnoreCase(anchorFk.ReferencingColumns[0]));
        _target = model.Columns.First(c => c.Name.EqualsIgnoreCase(_targetForeignKey.ReferencingColumns[0]));
    }

    /// <summary> The lines that answer 404 unless the anchor (and the target) row is one the caller can see. </summary>
    private IEnumerable<string> Visible(string contextExpression, string anchorExpression, string? targetExpression)
    {
        if (!_tenanted)
            yield break;
        yield return $"if (!await {contextExpression}.Set<{_anchorForeignKey.ReferencedTable}>().AnyAsync(r => r.{_anchorForeignKey.ReferencedColumns[0]} == {anchorExpression}))";
        yield return "    return NotFound();";
        if (targetExpression is null)
            yield break;
        yield return $"if (!await {contextExpression}.Set<{_targetForeignKey.ReferencedTable}>().AnyAsync(r => r.{_targetForeignKey.ReferencedColumns[0]} == {targetExpression}))";
        yield return "    return NotFound();";
    }

    private string Entity => _model.IsCSharpReservedWordName ? "@" + _model.TableName : _model.TableName;

    /// <summary> Fills <c>rows</c> with a <paramref name="itemType"/> per row of the target table: its display columns, <c>TargetId</c>, and whether it is linked to the anchor, in the order of the first display column. </summary>
    public List<string> List(string itemType, string contextExpression, string anchorExpression, string indent)
    {
        string targetEntity = _targetForeignKey.ReferencedTable;
        string targetKey = _targetForeignKey.ReferencedColumns[0];
        var display = _targetForeignKey.ReferencedDisplayColumns;
        string orderBy = display.Count > 0 ? display[0] : targetKey;

        var initializers = display.Select(d => $"{d} = t.{d}").Append($"TargetId = t.{targetKey}").Append("IsSelected = linked.Contains(t." + targetKey + ")").ToList();
        var lines = Visible(contextExpression, anchorExpression, null).ToList();
        lines.AddRange(new[]
        {
            $"var linked = {contextExpression}.Set<{Entity}>().Where(j => j.{_anchor.CSharpName()} == {anchorExpression}).Select(j => j.{_target.CSharpName()});",
            $"var rows = await {contextExpression}.Set<{targetEntity}>()",
            $"    .OrderBy(t => t.{orderBy})",
            $"    .Select(t => new {itemType} {{ {string.Join(", ", initializers)} }})",
            "    .ToListAsync();"
        });
        return lines.Select(l => indent + l).ToList();
    }

    /// <summary> Adds the link unless it exists (the routine is idempotent), recording the create date and an empty create user where the table has them. </summary>
    public List<string> Link(string contextExpression, string anchorExpression, string targetExpression, string indent)
    {
        string anchor = _anchor.CSharpName(), target = _target.CSharpName();
        var initializers = new List<string> { $"{anchor} = {anchorExpression}", $"{target} = {targetExpression}" };
        if (_model.Junction!.CreateDateColumn is { } createDate)
            initializers.Add($"{createDate.CSharpName()} = {createDate.CSharpBase()}.Now");
        if (_model.Junction!.CreateUserColumn is { } createUser)
            initializers.Add($"{createUser.CSharpName()} = \"\"");
        if (_owned is not null)
            initializers.Add($"{_owned.Column.CSharpName()} = {contextExpression}.CurrentTenantId!.Value");

        var lines = Visible(contextExpression, anchorExpression, targetExpression).ToList();
        lines.AddRange(new[]
        {
            $"if (!await {contextExpression}.Set<{Entity}>().AnyAsync(j => j.{anchor} == {anchorExpression} && j.{target} == {targetExpression}))",
            "{",
            $"    {contextExpression}.Set<{Entity}>().Add(new {Entity} {{ {string.Join(", ", initializers)} }});",
            $"    await {contextExpression}.SaveChangesAsync();",
            "}"
        });
        return lines.Select(l => indent + l).ToList();
    }

    /// <summary> Removes the link when it exists. </summary>
    public List<string> Unlink(string contextExpression, string anchorExpression, string targetExpression, string indent)
    {
        string anchor = _anchor.CSharpName(), target = _target.CSharpName();
        var lines = Visible(contextExpression, anchorExpression, targetExpression).ToList();
        lines.AddRange(new[]
        {
            $"var links = await {contextExpression}.Set<{Entity}>().Where(j => j.{anchor} == {anchorExpression} && j.{target} == {targetExpression}).ToListAsync();",
            $"{contextExpression}.Set<{Entity}>().RemoveRange(links);",
            $"await {contextExpression}.SaveChangesAsync();"
        });
        return lines.Select(l => indent + l).ToList();
    }
}
