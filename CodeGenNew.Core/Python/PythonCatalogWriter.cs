using CodeGenNew.Core.Catalog;

namespace CodeGenNew.Core.Python;

/// <summary> The Python (FastAPI) counterpart of <see cref="CatalogWriter"/> (<c>FacetTables</c>): the product table's router gets <c>GET facets</c> and <c>GET narrow</c>. Several values of one attribute are alternatives, different attributes all have to match,
/// a range keeps the products with a number of that attribute between its ends, and an attribute is counted without its own filter, so the other values of the attribute that was chosen stay on the list.
/// The keyword is read in the product's text columns and in the attribute values; it lowers both sides, so the answer does not depend on the collation (with <c>FacetFullText</c> on SQL Server it is a CONTAINS condition). </summary>
public static class PythonCatalogWriter
{
    /// <summary> The Python names the facet routes of a product table need. </summary>
    public sealed record Names(PythonTable Product, PythonTable Attributes, string Key, string AProduct, string AName, string AValue, string? ANumber, bool FullText, IReadOnlyList<string> Search, string? Title, string? Code, string? Detail)
    {
        /// <summary> The tables whose model the routes read. </summary>
        public IEnumerable<PythonTable> Related => new[] { Attributes }.Where(t => t.Class != Product.Class);
    }

    /// <summary> The sqlalchemy words the routes need. </summary>
    public static string[] SqlAlchemyWords => ["func", "or_"];

    public static Names? NamesFor(CatalogShape? shape, PythonTable product, ProjectSettings project)
    {
        if (shape is null)
            return null;
        var attributes = PythonTable.Of(shape.Attributes, project);
        string? Of(ColumnModel? c) => c is null ? null : product.FieldOf(c).Name;
        return new Names(product, attributes, product.FieldOf(shape.Key).Name, attributes.FieldOf(shape.AttributeProduct).Name, attributes.FieldOf(shape.AttributeName).Name, attributes.FieldOf(shape.AttributeValue).Name,
            shape.AttributeNumber is null ? null : attributes.FieldOf(shape.AttributeNumber).Name, shape.FullText, shape.SearchColumns.Select(c => product.FieldOf(c).Name).ToList(), Of(shape.Title), Of(shape.Code), Of(shape.Detail));
    }

    public static string Helper(Names n) => Fill(n.FullText ? FullTextText + HelperText : HelperText, n);

    public static string Routes(Names n, string route, string readDepends, string userParameter) =>
        Fill(RouteText, n, ("§ROUTE", route), ("§READ", readDepends), ("§USER", userParameter), ("§FACETSROUTE", CatalogPlan.FacetsRoute), ("§NARROWROUTE", CatalogPlan.NarrowRoute));

    private static string Fill(string text, Names n, params (string Token, string Value)[] more)
    {
        string p = n.Product.Class, a = n.Attributes.Class;
        string pick(string? field, string json) => field is null ? $"\"{json}\": None" : $"\"{json}\": row.{field}";
        var columns = new List<string> { $"{p}.{n.Key}" };
        foreach (string? f in new[] { n.Title, n.Code, n.Detail })
            if (f is not null)
                columns.Add($"{p}.{f}");
        var hits = n.Search.Select(f => n.FullText ? $"func.contains({p}.{f}, fts)" : $"func.lower({p}.{f}).contains(term, autoescape=True)").ToList();
        hits.Add(n.FullText
            ? $"{p}.{n.Key}.in_(select({a}.{n.AProduct}).where(func.contains({a}.{n.AValue}, fts)))"
            : $"{p}.{n.Key}.in_(select({a}.{n.AProduct}).where(func.lower({a}.{n.AValue}).contains(term, autoescape=True)))");
        string number = n.ANumber is null ? "" : $"""

    for name, low, high in bounds:
        if except_name is not None and name.lower() == except_name.lower():
            continue
        numbers = select({a}.{n.AProduct}).where({a}.{n.AName} == name, {a}.{n.ANumber}.is_not(None))
        if low is not None:
            numbers = numbers.where({a}.{n.ANumber} >= low)
        if high is not None:
            numbers = numbers.where({a}.{n.ANumber} <= high)
        query = query.where({p}.{n.Key}.in_(numbers))
""";
        string bounds = n.ANumber is null ? "low, high = None, None" : $"low, high = session.execute(select(func.min({a}.{n.ANumber}), func.max({a}.{n.ANumber})).where({a}.{n.AName} == name, {a}.{n.ANumber}.is_not(None), {a}.{n.AProduct}.in_(catalog_matching(q, chosen, bounds, name)))).one()";
        var values = new List<(string, string)>
        {
            ("§PRODUCT", p), ("§ATTR", a), ("§KEY", n.Key), ("§APRODUCT", n.AProduct), ("§ANAME", n.AName), ("§AVALUE", n.AValue),
            ("§SEARCHTERM", n.FullText ? "fts = catalog_full_text(text)" : "term = (text or \"\").strip().lower()"), ("§SEARCHIF", n.FullText ? "if fts:" : "if term:"),
            ("§SEARCH", string.Join(", ", hits)), ("§NUMBER", number), ("§BOUNDS", bounds),
            ("§COLUMNS", string.Join(", ", columns)),
            ("§ITEM", $"{{\"id\": row.{n.Key}, {pick(n.Title, "title")}, {pick(n.Code, "code")}, {pick(n.Detail, "detail")}}}"),
            ("§MAXFACETS", CatalogPlan.MaxFacets.ToString()), ("§MAXVALUES", CatalogPlan.MaxValues.ToString()), ("§PAGESIZE", CatalogPlan.DefaultPageSize), ("§MAXPAGE", CatalogPlan.MaxPageSize.ToString()),
            ("§BADFILTERTITLE", CatalogWords.BadFilterTitle), ("§BADFILTERTYPE", CatalogWords.BadFilterType), ("§W_BADFILTER", CatalogWords.BadFilter),
            ("§BADPAGETITLE", CatalogWords.BadPageTitle), ("§BADPAGETYPE", CatalogWords.BadPageType), ("§W_BADPAGE", CatalogWords.BadPage),
        };
        values.AddRange(more);
        return TemplateText.Fill(text, [.. values]);
    }

    private const string FullTextText = """"
def catalog_full_text(text: str | None) -> str | None:
    """The text as a CONTAINS condition: each word as a prefix, all of them required."""
    words = [word.replace('"', "").replace("*", "") for word in (text or "").split()]
    words = [word for word in words if word]
    return " AND ".join(f'"{word}*"' for word in words) if words else None


"""";

    private const string HelperText = """"
def catalog_number(text: str) -> Decimal | None:
    """A number of a range, or None when the end is left empty; anything else is a bad filter."""
    if not text:
        return None
    if not re.fullmatch(r"[+-]?(\d+\.?\d*|\.\d+)([eE][+-]?\d+)?", text):
        raise ApiError(400, "§BADFILTERTITLE", problem_type="§BADFILTERTYPE", detail="§W_BADFILTER")
    return Decimal(text)


def catalog_filters(filters: list[str] | None, ranges: list[str] | None = None):
    """The chosen values by attribute name (each is written Name:Value) and the chosen ranges (each is written Name:Min:Max): several values of one attribute are alternatives, different attributes all have to match."""
    chosen: dict[str, list[str]] = {}
    spelled: dict[str, str] = {}
    for raw in filters or []:
        name, colon, value = raw.partition(":")
        name, value = name.strip(), value.strip()
        if not colon or not name or not value:
            raise ApiError(400, "§BADFILTERTITLE", problem_type="§BADFILTERTYPE", detail="§W_BADFILTER")
        values = chosen.setdefault(spelled.setdefault(name.lower(), name), [])
        if value not in values:
            values.append(value)
    bounds: list[tuple[str, Decimal | None, Decimal | None]] = []
    for raw in ranges or []:
        head, _, high = raw.rpartition(":")
        name, _, low = head.rpartition(":")
        name, low, high = name.strip(), low.strip(), high.strip()
        if not name or (not low and not high):
            raise ApiError(400, "§BADFILTERTITLE", problem_type="§BADFILTERTYPE", detail="§W_BADFILTER")
        bounds.append((name, catalog_number(low), catalog_number(high)))
    return chosen, bounds


def catalog_matching(text: str | None, chosen: dict[str, list[str]], bounds: list, except_name: str | None = None):
    """The keys of the products that match the keyword (in the product's text columns and in the attribute values) and every chosen attribute but the one named in except_name."""
    query = select(§PRODUCT.§KEY)
    §SEARCHTERM
    §SEARCHIF
        query = query.where(or_(§SEARCH))
    for name, values in chosen.items():
        if except_name is not None and name.lower() == except_name.lower():
            continue
        query = query.where(§PRODUCT.§KEY.in_(select(§ATTR.§APRODUCT).where(§ATTR.§ANAME == name, §ATTR.§AVALUE.in_(values))))§NUMBER
    return query
"""";

    private const string RouteText = """"
@router.get("§ROUTE/§FACETSROUTE"§READ)
def catalog_facets(q: str | None = Query(None), filters: list[str] | None = Query(None, alias="filter"), ranges: list[str] | None = Query(None, alias="range"), max_facets: int | None = Query(None, alias="maxFacets"), max_values: int | None = Query(None, alias="maxValues"), session: Session = Depends(get_session)§USER):
    """The attributes of the products that match the keyword and the filters (the most common first), each with its values and the number of products that carry them, and the lowest and highest number it has. An attribute is counted without its own filter."""
    chosen, bounds = catalog_filters(filters, ranges)
    facet_limit = min(max(§MAXFACETS if max_facets is None else max_facets, 1), 50)
    value_limit = min(max(§MAXVALUES if max_values is None else max_values, 1), 100)
    products = func.count(func.distinct(§ATTR.§APRODUCT))
    names = [row[0] for row in session.execute(select(§ATTR.§ANAME, products).where(§ATTR.§APRODUCT.in_(catalog_matching(q, chosen, bounds))).group_by(§ATTR.§ANAME).order_by(products.desc(), §ATTR.§ANAME).limit(facet_limit)).all()]
    for name in [*chosen, *(b[0] for b in bounds)]:
        if not any(shown.lower() == name.lower() for shown in names):
            names.append(name)
    groups = []
    for name in names:
        selected = next((values for chosen_name, values in chosen.items() if chosen_name.lower() == name.lower()), [])
        picked = next((b for b in bounds if b[0].lower() == name.lower()), None)
        counted = func.count(func.distinct(§ATTR.§APRODUCT))
        rows = session.execute(select(§ATTR.§AVALUE, counted).where(§ATTR.§ANAME == name, §ATTR.§AVALUE.is_not(None), §ATTR.§APRODUCT.in_(catalog_matching(q, chosen, bounds, name))).group_by(§ATTR.§AVALUE).order_by(counted.desc(), §ATTR.§AVALUE).limit(value_limit)).all()
        values = [{"value": value, "count": count, "selected": value in selected} for value, count in rows]
        for value in selected:
            if not any(item["value"] == value for item in values):
                values.append({"value": value, "count": 0, "selected": True})
        §BOUNDS
        groups.append({"name": name, "values": values, "min": low, "max": high, "selectedMin": picked[1] if picked else None, "selectedMax": picked[2] if picked else None})
    return {"total": session.scalar(select(func.count()).select_from(catalog_matching(q, chosen, bounds).subquery())), "facets": groups}


@router.get("§ROUTE/§NARROWROUTE"§READ)
def catalog_narrow(q: str | None = Query(None), filters: list[str] | None = Query(None, alias="filter"), ranges: list[str] | None = Query(None, alias="range"), page: int | None = Query(None), page_size: int | None = Query(None, alias="pageSize"), session: Session = Depends(get_session)§USER):
    """The page of products that match the keyword and the filters, in key order."""
    chosen, bounds = catalog_filters(filters, ranges)
    if (page is not None and page < 1) or (page_size is not None and page_size < 1):
        raise ApiError(400, "§BADPAGETITLE", problem_type="§BADPAGETYPE", detail="§W_BADPAGE")
    size = min(§PAGESIZE if page_size is None else page_size, §MAXPAGE)
    number = 1 if page is None else page
    matching = catalog_matching(q, chosen, bounds)
    total = session.scalar(select(func.count()).select_from(matching.subquery()))
    rows = session.execute(select(§COLUMNS).where(§PRODUCT.§KEY.in_(matching)).order_by(§PRODUCT.§KEY).offset((number - 1) * size).limit(size)).all()
    return {"total": total, "page": number, "pageSize": size, "items": [§ITEM for row in rows]}
"""";
}
