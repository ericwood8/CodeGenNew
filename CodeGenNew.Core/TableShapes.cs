using System.Runtime.CompilerServices;
using CodeGenNew.Core.Api;
using CodeGenNew.Core.Content;
using CodeGenNew.Core.Party;
using CodeGenNew.Core.Payment;
using CodeGenNew.Core.Price;
using CodeGenNew.Core.Rate;
using CodeGenNew.Core.Stock;
using CodeGenNew.Core.Tax;
using CodeGenNew.Core.Tenant;
using CodeGenNew.Core.Usage;

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

    /// <summary> The table as an inbound event landing table; null when it is not. </summary>
    public InboundShape? Inbound => Shape<InboundShape>();
    public TokenShape? Token => Shape<TokenShape>();
    public UsageShape? Usage => Shape<UsageShape>();
    public SoftDeleteShape? SoftDelete => Shape<SoftDeleteShape>();
    public TreeShape? Tree => Shape<TreeShape>();
    public ContentShape? Content => Shape<ContentShape>();
    public AssetShape? Asset => Shape<AssetShape>();
    public EffectiveShape? Effective => Shape<EffectiveShape>();
    public DefaultRowShape? DefaultRow => Shape<DefaultRowShape>();
    public ExtensionShape? Extension => Shape<ExtensionShape>();

    /// <summary> The table as a document line that carries the rate of its tax category (<c>TaxLines</c>); null when it is not, or when the generator has not remembered the database of the run. </summary>
    public TaxLineShape? TaxLine => Shape<TaxLineShape>();
    /// <summary> The table as the price rows of a price list (<c>PriceListTables</c>); null when it is not, or when the generator has not remembered the database of the run. </summary>
    public PriceListShape? PriceList => Shape<PriceListShape>();

    /// <summary> The table as a bank account whose IBAN, or account and routing number, are checked (<c>BankAccountTables</c>); null when it is not, or when the generator has not remembered the database of the run. </summary>
    public BankAccountShape? BankAccount => Shape<BankAccountShape>();

    /// <summary> The table as the installments of a document (<c>InstallmentTables</c>); null when it is not, or when the generator has not remembered the database of the run. </summary>
    public InstallmentShape? Installment => Shape<InstallmentShape>();

    /// <summary> The table as payment means whose due date is worked out from payment terms (<c>PaymentTermsTables</c>); null when it is not, or when the generator has not remembered the database of the run. </summary>
    public DueDateShape? DueDate => Shape<DueDateShape>();
    public LedgerShape? Ledger => Shape<LedgerShape>();

    /// <summary> The table as the stock level table of <c>StockTables</c> (its saves and deletes may not change the quantities); null when it is not. </summary>
    public StockShape? StockLevel => Shape<StockShape>();

    /// <summary> The table as the ledger of a stock table (<c>StockTables</c>): the table that carries the stock routes; null when it is not. </summary>
    public StockLedgerOf? StockLedger => Shape<StockLedgerOf>();

    /// <summary> The table as the table of price lists that a price table (<c>PriceListTables</c>) points at; null when it is not. </summary>
    public PriceListHeadShape? PriceListHead => Shape<PriceListHeadShape>();

    public LogShape? Log => Shape<LogShape>();
    public NameActiveShape? NameActive => Shape<NameActiveShape>();

    /// <summary> The table as a table of exchange rates (<c>RateTables</c>); null when it is not. </summary>
    public RateShape? Rate => Shape<RateShape>();

    /// <summary> The table as one the project keeps a single row of (<c>SingleRowTables</c>), shown as a form. </summary>
    public SingleRowShape? SingleRow => Shape<SingleRowShape>();

    /// <summary> The table as one the database versions itself (<c>TemporalTables</c>). </summary>
    public TemporalShape? Temporal => Shape<TemporalShape>();

    /// <summary> The table as one whose key comes from the key-sequence table (<c>KeySequenceTables</c>). </summary>
    public KeySequenceShape? KeySequence => Shape<KeySequenceShape>();

    // the shapes below read other tables, so they need the database

    /// <summary> The table as a child that copies columns of its parents when a row is saved (<c>SnapshotColumns</c>). </summary>
    public SnapshotShape? Snapshot => Shape<SnapshotShape>();

    /// <summary> The table as a document that keeps the exchange rate it was written with (<c>RateDocuments</c>). </summary>
    public RateDocumentShape? RateDocument => Shape<RateDocumentShape>();

    /// <summary> The table as a party table, the one that has identifiers (<c>PartyTables</c>). </summary>
    public PartyRootShape? PartyRoot => Shape<PartyRootShape>();

    /// <summary> The table as the identifier table of a party table (<c>PartyTables</c>). </summary>
    public PartyIdentifierShape? PartyIdentifiers => Shape<PartyIdentifierShape>();

    /// <summary> The table as a polymorphic link table: a type and an id that name a row of any of several tables (<c>PolymorphicLinks</c>). </summary>
    public LinkShape? Link => Shape<LinkShape>();

    /// <summary> The table as one a polymorphic link table may name. </summary>
    public LinkedShape? Linked => Shape<LinkedShape>();

    /// <summary> The table as one a script keeps small by moving or deleting old rows (<c>ArchiveTables</c>). </summary>
    public ArchiveJob? Archive => Shape<ArchiveJob>();

    /// <summary> The table as one a script splits into a partition for each month (<c>PartitionTables</c>). </summary>
    public PartitionJob? Partition => Shape<PartitionJob>();

    /// <summary> The table as the category table of a tax line table (<c>TaxLines</c>). </summary>
    public TaxCategoryShape? TaxCategory => Shape<TaxCategoryShape>();

    /// <summary> The table as one of the four tables of roles and permissions (<c>PermissionTables</c>). </summary>
    public PermissionTableShape? Permission => Shape<PermissionTableShape>();

    /// <summary> The table as the flag table or the override table of feature flags (<c>FeatureTables</c>). </summary>
    public FeatureTableShape? Feature => Shape<FeatureTableShape>();

    /// <summary> The table as the subscription table of a tenant (<c>SubscriptionTable</c>). </summary>
    public SubscriptionModel? Subscription => Shape<SubscriptionModel>();

    /// <summary> The table as the plan table of the subscriptions (<c>PlanTable</c>). </summary>
    public SubscriptionPlanShape? SubscriptionPlan => Shape<SubscriptionPlanShape>();

    /// <summary> The table as the table of what each plan allows (<c>PlanLimitTable</c>). </summary>
    public PlanLimitModel? PlanLimit => Shape<PlanLimitModel>();

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

    private static readonly Type[] Known = [typeof(SoftDeleteShape), typeof(QueueShape), typeof(InboundShape), typeof(TokenShape), typeof(UsageShape), typeof(LogShape), typeof(TreeShape), typeof(ContentShape), typeof(AssetShape), typeof(EffectiveShape), typeof(DefaultRowShape), typeof(ExtensionShape), typeof(TaxLineShape), typeof(NameActiveShape), typeof(TenantOwnedShape), typeof(JunctionShape), typeof(UserTableShape), typeof(OwnerLinkShape), typeof(OwnerColumnShape), typeof(TenantTableShape),
        typeof(RateShape), typeof(PriceListShape), typeof(PriceListHeadShape), typeof(SingleRowShape), typeof(TemporalShape), typeof(KeySequenceShape)];
    private static readonly Type[] NeedDatabase = [typeof(TenantMembershipShape), typeof(ApprovalTableShape), typeof(ApprovalSide), typeof(SnapshotShape), typeof(RateDocumentShape), typeof(PartyRootShape), typeof(PartyIdentifierShape),
        typeof(LinkShape), typeof(LinkedShape), typeof(ArchiveJob), typeof(PartitionJob), typeof(TaxCategoryShape), typeof(PermissionTableShape), typeof(FeatureTableShape), typeof(SubscriptionModel), typeof(SubscriptionPlanShape),
        typeof(PlanLimitModel)];

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
        if (shape == typeof(InboundShape)) return InboundPlan.For(_table, _project);
        if (shape == typeof(TokenShape)) return TokenPlan.For(_table, _project);
        if (shape == typeof(UsageShape)) return UsagePlan.For(_table, _project);
        if (shape == typeof(LogShape)) return LogTablePlan.For(_table, _project);
        if (shape == typeof(TreeShape)) return TreePlan.For(_table, _project);
        if (shape == typeof(ContentShape)) return ContentPlan.For(_table, _project);
        if (shape == typeof(AssetShape)) return AssetPlan.For(_table, _project);
        if (shape == typeof(EffectiveShape)) return EffectiveDatePlan.For(_table, _project);
        if (shape == typeof(DefaultRowShape)) return DefaultRowPlan.For(_table, _project);
        if (shape == typeof(ExtensionShape)) return ExtensionPlan.For(_table, _project);
        if (shape == typeof(PriceListShape)) return _database is null ? PriceListPlan.For(_table, _project) : PriceListPlan.For(_table, _project, _database);
        if (shape == typeof(BankAccountShape)) return _database is null ? PaymentPlan.BankFor(_table, _project) : PaymentPlan.BankFor(_table, _project, _database);
        if (shape == typeof(InstallmentShape)) return _database is null ? PaymentPlan.InstallmentFor(_table, _project) : PaymentPlan.InstallmentFor(_table, _project, _database);
        if (shape == typeof(StockShape)) return _database is null ? StockPlan.ForLevel(_table, _project) : StockPlan.ForLevel(_table, _project, _database);
        if (shape == typeof(StockLedgerOf)) return (_database is null ? StockPlan.ForLedger(_table, _project) : StockPlan.ForLedger(_table, _project, _database)) is { } stock ? new StockLedgerOf(stock) : null;
        if (shape == typeof(LedgerShape)) return _database is null ? LedgerPlan.For(_table, _project) : LedgerPlan.For(_table, _project, _database);
        if (shape == typeof(DueDateShape)) return _database is null ? PaymentPlan.DueFor(_table, _project) : PaymentPlan.DueFor(_table, _project, _database);
        if (shape == typeof(PriceListHeadShape)) return (_database ?? RatePlan.Remembered(_project)) is { } known ? PriceListPlan.HeadOf(_table, _project, known) is { } priced ? new PriceListHeadShape(priced) : null : null;
        if (shape == typeof(TaxLineShape)) return _database is null ? TaxLinePlan.For(_table, _project) : TaxLinePlan.For(_table, _project, _database);
        if (shape == typeof(NameActiveShape)) return NameActiveShape.For(_table, _project);
        if (shape == typeof(TenantOwnedShape)) return TenantPlan.OwnedBy(_table, _project);
        if (shape == typeof(JunctionShape)) return _table.Junction;
        if (shape == typeof(UserTableShape)) return UserTable.PlanOf(_table, _project);
        if (shape == typeof(OwnerColumnShape)) return OwnedTable.OwnerColumn(_table, _project) is { } owner ? new OwnerColumnShape(owner) : null;
        if (shape == typeof(OwnerLinkShape)) return OwnedTable.Through(_table, _project);
        if (shape == typeof(TenantTableShape)) return TenantPlan.IsTenantTable(_table, _project) ? new TenantTableShape(_table) : null;
        if (shape == typeof(RateShape)) return RatePlan.For(_table, _project);
        if (shape == typeof(SingleRowShape)) return _project.IsSingleRow(_table.TableName) ? new SingleRowShape(_table) : null;
        if (shape == typeof(TemporalShape)) return _project.TemporalTables.Contains(_table.TableName, StringComparer.OrdinalIgnoreCase) ? new TemporalShape(_table) : null;
        if (shape == typeof(KeySequenceShape)) return _project.UsesKeySequence(_table.TableName) ? new KeySequenceShape(_table) : null;
        if (NeedDatabase.Contains(shape))
        {
            var database = _database ?? throw new InvalidOperationException($"{shape.Name} needs the database: make the shapes with TableShapes.For(table, project, database).");
            return WorkWithDatabase(shape, database);
        }
        throw new ArgumentException($"{shape.Name} is not a shape of a table.");
    }

    private bool IsThis(TableModel other) => IsThis(other.TableName);

    private bool IsThis(string name) => name.EqualsIgnoreCase(_table.TableName);

    // each shape asks its own planner, and only when the project's setting names this table, so a table the project does not list costs nothing
    private object? WorkWithDatabase(Type shape, DatabaseModel database)
    {
        if (shape == typeof(TenantMembershipShape)) return TenantPlan.MembershipOf(database, _project) is { } membership && IsThis(membership.Table) ? membership : null;
        if (shape == typeof(ApprovalTableShape)) return _project.RowApprovals.Length == 0 ? null : ApprovalPlan.Resolve(database, _project).Tables.FirstOrDefault(a => IsThis(a.Table));
        if (shape == typeof(ApprovalSide)) return _project.RowApprovals.Length > 0 && ApprovalPlan.Resolve(database, _project).Tables.FirstOrDefault(a => IsThis(a.Side)) is { } approval ? new ApprovalSide(approval) : null;
        if (shape == typeof(SnapshotShape)) return SnapshotPlan.For(_table, _project, database);
        if (shape == typeof(RateDocumentShape)) return RateDocumentPlan.For(_table, _project, database);
        if (shape == typeof(PartyRootShape)) return PartyPlan.PartyOf(_table, _project, database) is { } party ? new PartyRootShape(party) : null;
        if (shape == typeof(PartyIdentifierShape)) return PartyPlan.IdentifiersOf(_table, _project, database) is { } identifiers ? new PartyIdentifierShape(identifiers) : null;
        if (shape == typeof(LinkShape)) return PolymorphicPlan.LinkOf(_table, _project, database);
        if (shape == typeof(LinkedShape)) return PolymorphicPlan.TargetOf(_table, _project, database);
        if (shape == typeof(ArchiveJob)) return ArchivePlan.Entries(_project).Any(e => IsThis(e.Table)) ? ArchivePlan.Resolve(database, _project).Jobs.FirstOrDefault(j => IsThis(j.Table)) : null;
        if (shape == typeof(PartitionJob)) return PartitionPlan.Entries(_project).Any(e => IsThis(e.Table)) ? PartitionPlan.Resolve(database, _project).Jobs.FirstOrDefault(j => IsThis(j.Table)) : null;
        if (shape == typeof(TaxCategoryShape))
            return TaxLinePlan.Entries(_project).Where(e => e.CategoryTable is not null && IsThis(e.CategoryTable))
                .Select(e => database.Tables.FirstOrDefault(t => t.TableName.EqualsIgnoreCase(e.Table))).OfType<TableModel>()
                .Select(line => TaxLinePlan.For(line, _project, database)).OfType<TaxLineShape>().FirstOrDefault() is { } taxLine ? new TaxCategoryShape(taxLine) : null;
        if (shape == typeof(PermissionTableShape))
        {
            if (!PermissionTables.IsConfigured(_project) || PermissionTables.Resolve(database, _project).Plan is not { } plan)
                return null;
            string? role = IsThis(plan.Role) ? "Role" : IsThis(plan.Permission) ? "Permission" : IsThis(plan.RolePermission) ? "RolePermission" : IsThis(plan.UserRole) ? "UserRole" : null;
            return role is null ? null : new PermissionTableShape(plan, role);
        }
        if (shape == typeof(FeatureTableShape))
        {
            if (!FeatureFlagTables.IsConfigured(_project) || FeatureFlagTables.Resolve(database, _project).Plan is not { } plan)
                return null;
            string? role = IsThis(plan.Flag) ? "Flag" : IsThis(plan.Override) ? "Override" : plan.PlanLink is { } link && IsThis(link.Table) ? "PlanLink" : null;
            return role is null ? null : new FeatureTableShape(plan, role);
        }
        if (_project.SubscriptionTable is null)
            return null;
        if (shape == typeof(SubscriptionModel)) return SubscriptionFeature.SubscriptionOf(database, _project) is { } model && IsThis(model.Table) ? model : null;
        if (shape == typeof(SubscriptionPlanShape)) return SubscriptionFeature.SubscriptionOf(database, _project) is { } owner && IsThis(owner.PlanTable) ? new SubscriptionPlanShape(owner) : null;
        return SubscriptionFeature.LimitTableOf(database, _project) is { } limits && IsThis(limits.Table) ? limits : null;
    }
}

/// <summary> The table is one the project keeps a single row of (<c>SingleRowTables</c>). </summary>
public sealed record SingleRowShape(TableModel Table);

/// <summary> The table is one the database versions itself (<c>TemporalTables</c>). </summary>
public sealed record TemporalShape(TableModel Table);

/// <summary> The table takes its key from the key-sequence table (<c>KeySequenceTables</c>). </summary>
public sealed record KeySequenceShape(TableModel Table);

/// <summary> The table is the party table of <see cref="Party"/>, the one that has identifiers. </summary>
public sealed record PartyRootShape(PartyShape Party);

/// <summary> The table is the identifier table of <see cref="Party"/>. </summary>
public sealed record PartyIdentifierShape(PartyShape Party);

/// <summary> The table is the category table of the tax lines in <see cref="Line"/>. </summary>
public sealed record TaxCategoryShape(TaxLineShape Line);

/// <summary> The table of price lists that a price table points at. </summary>
public sealed record PriceListHeadShape(PriceListShape Prices);

/// <summary> The table is one of the four tables of roles and permissions; <see cref="Role"/> says which (Role, Permission, RolePermission or UserRole). </summary>
public sealed record PermissionTableShape(PermissionTablesPlan Plan, string Role);

/// <summary> The table is one of the tables of feature flags; <see cref="Role"/> says which (Flag, Override or PlanLink). </summary>
public sealed record FeatureTableShape(FeatureFlagPlan Plan, string Role);

/// <summary> The table is the plan table of <see cref="Subscription"/>. </summary>
public sealed record SubscriptionPlanShape(SubscriptionModel Subscription);

/// <summary> The table is the project's tenant table (<c>TenantTable</c>). </summary>
public sealed record TenantTableShape(TableModel Table);

/// <summary> The table is the side table that holds the request cycles of an approval table. </summary>
public sealed record ApprovalSide(ApprovalTableShape Approval);

/// <summary> The table is owned through a column of its own (<c>OwnedTables</c>). </summary>
public sealed record OwnerColumnShape(ColumnModel Column);
