using System.Runtime.CompilerServices;

namespace CodeGenNew.Core;

/// <summary> What one table is, for the project's settings: one shape object for each shape the table has (soft delete, queue, log, tree, effective dated, Name and IsActive, owned by a tenant, junction), asked of the
/// planner that holds the rule. A table may have several at once. Each shape is worked out the first time it is asked for and kept; nothing is worked out for a shape nobody asks about. </summary>
public sealed class TableShapes
{
    private readonly TableModel _table;
    private readonly ProjectSettings _project;
    private readonly DatabaseModel? _database;
    private readonly Dictionary<Type, object?> _kept = [];

    private TableShapes(TableModel table, ProjectSettings project, DatabaseModel? database)
    {
        _table = table;
        _project = project;
        _database = database;
    }

    /// <summary> The shapes of <paramref name="table"/> for the project's settings. The shapes that need another table (the membership of a tenant, an approval and its side table) need the <paramref name="database"/>; asking for one without it is an error. </summary>
    public static TableShapes For(TableModel table, ProjectSettings project, DatabaseModel? database = null)
    {
        var byTable = Made.GetValue(project, _ => new ConditionalWeakTable<TableModel, TableShapes>());
        if (byTable.TryGetValue(table, out var made) && (database is null || made._database is not null))
            return made;
        var shapes = new TableShapes(table, project, database);
        byTable.Remove(table);
        byTable.Add(table, shapes);
        return shapes;
    }

    // one set of shapes per table and project object, so every writer that asks gets the answer the first one worked out
    private static readonly ConditionalWeakTable<ProjectSettings, ConditionalWeakTable<TableModel, TableShapes>> Made = new();

    /// <summary> The tenant column of the table and the tenant table it points at, or null when the project has no tenants or the table is not owned by one. </summary>
    public TenantOwnedShape? Tenant => Shape<TenantOwnedShape>();

    /// <summary> The table as a work queue, a soft deleted table, a tree, an effective dated table, a log or a table with a Name and an IsActive column; null when it is not. </summary>
    public QueueShape? Queue => Shape<QueueShape>();
    public TokenShape? Token => Shape<TokenShape>();
    public UsageShape? Usage => Shape<UsageShape>();
    public SoftDeleteShape? SoftDelete => Shape<SoftDeleteShape>();
    public TreeShape? Tree => Shape<TreeShape>();
    public EffectiveShape? Effective => Shape<EffectiveShape>();
    public ExtensionShape? Extension => Shape<ExtensionShape>();
    public LogShape? Log => Shape<LogShape>();
    public NameActiveShape? NameActive => Shape<NameActiveShape>();

    /// <summary> The plan of the table when it is the user table and the settings fit it. </summary>
    public UserTableShape? User => Shape<UserTableShape>();

    /// <summary> The column that says which person owns a row of the table, or null. </summary>
    public ColumnModel? Owner => Shape<OwnerColumnShape>()?.Column;

    /// <summary> The parent that says which person owns a row of the table (an owner through a link), or null. </summary>
    public OwnerLinkShape? OwnerThrough => Shape<OwnerLinkShape>();

    /// <summary> The table's shape of type <typeparamref name="T"/>, or null when the table does not have it. </summary>
    public T? Shape<T>() where T : class
    {
        return (T?)ShapeOf(typeof(T));
    }

    private static readonly Type[] Known = [typeof(SoftDeleteShape), typeof(QueueShape), typeof(TokenShape), typeof(UsageShape), typeof(LogShape), typeof(TreeShape), typeof(EffectiveShape), typeof(ExtensionShape), typeof(NameActiveShape), typeof(TenantOwnedShape), typeof(JunctionShape), typeof(UserTableShape), typeof(OwnerLinkShape), typeof(OwnerColumnShape), typeof(TenantTableShape)];
    private static readonly Type[] NeedDatabase = [typeof(TenantMembershipShape), typeof(ApprovalTableShape), typeof(ApprovalSide)];

    /// <summary> Every shape the table has, in a fixed order; a table with none gives an empty list. </summary>
    public IReadOnlyList<object> All => (_database is null ? Known : [.. Known, .. NeedDatabase]).Select(t => ShapeOf(t)).OfType<object>().ToList();

    private object? ShapeOf(Type type)
    {
        if (!_kept.TryGetValue(type, out object? shape))
        {
            shape = Work(type);
            _kept[type] = shape;
        }
        return shape;
    }

    /// <summary> True when the table has a shape of type <typeparamref name="T"/>. </summary>
    public bool Has<T>() where T : class => Shape<T>() is not null;

    private object? Work(Type shape)
    {
        if (shape == typeof(SoftDeleteShape)) return SoftDeletePlan.For(_table, _project);
        if (shape == typeof(QueueShape)) return QueuePlan.For(_table, _project);
        if (shape == typeof(TokenShape)) return TokenPlan.For(_table, _project);
        if (shape == typeof(UsageShape)) return UsagePlan.For(_table, _project);
        if (shape == typeof(LogShape)) return LogTablePlan.For(_table, _project);
        if (shape == typeof(TreeShape)) return TreePlan.For(_table, _project);
        if (shape == typeof(EffectiveShape)) return EffectiveDatePlan.For(_table, _project);
        if (shape == typeof(ExtensionShape)) return ExtensionPlan.For(_table, _project);
        if (shape == typeof(NameActiveShape)) return NameActiveShape.For(_table, _project);
        if (shape == typeof(TenantOwnedShape)) return TenantPlan.OwnedBy(_table, _project);
        if (shape == typeof(JunctionShape)) return _table.Junction;
        if (shape == typeof(UserTableShape)) return UserTable.PlanOf(_table, _project);
        if (shape == typeof(OwnerColumnShape)) return OwnedTable.OwnerColumn(_table, _project) is { } owner ? new OwnerColumnShape(owner) : null;
        if (shape == typeof(OwnerLinkShape)) return OwnedTable.Through(_table, _project);
        if (shape == typeof(TenantTableShape)) return TenantPlan.IsTenantTable(_table, _project) ? new TenantTableShape(_table) : null;
        if (NeedDatabase.Contains(shape))
        {
            var database = _database ?? throw new InvalidOperationException($"{shape.Name} needs the database: make the shapes with TableShapes.For(table, project, database).");
            if (shape == typeof(TenantMembershipShape)) return TenantPlan.MembershipOf(database, _project) is { } membership && membership.Table.TableName.EqualsIgnoreCase(_table.TableName) ? membership : null;
            var approvals = ApprovalPlan.Resolve(database, _project).Tables;
            if (shape == typeof(ApprovalTableShape)) return approvals.FirstOrDefault(a => a.Table.TableName.EqualsIgnoreCase(_table.TableName));
            return approvals.FirstOrDefault(a => a.Side.TableName.EqualsIgnoreCase(_table.TableName)) is { } approval ? new ApprovalSide(approval) : null;
        }
        throw new ArgumentException($"{shape.Name} is not a shape of a table.");
    }
}

/// <summary> The table is the project's tenant table (<c>TenantTable</c>). </summary>
public sealed record TenantTableShape(TableModel Table);

/// <summary> The table is the side table that holds the request cycles of an approval table. </summary>
public sealed record ApprovalSide(ApprovalTableShape Approval);

/// <summary> The table is owned through a column of its own (<c>OwnedTables</c>). </summary>
public sealed record OwnerColumnShape(ColumnModel Column);
