namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of the content part of <see cref="RowShapeWriter"/>: the slug and status rules, the publish, unpublish and archive routes and the two public reads of a table listed in
/// <c>ContentTables</c>, written into the table's own router (PY_Routes). The words are <see cref="ContentWords"/> and <see cref="ContentApiWords"/>. </summary>
public static class PythonContentWriter
{
    /// <summary> The names a content table needs from its model: class, schema, key, title, slug and status attributes, the optional published-on attribute with the expression that is now, the slug length, and the
    /// attributes the public routes answer (null for every column). </summary>
    public sealed record Names(string Class, string Schema, string Key, string Title, string Slug, string Status, string? Published, string PublishedNow, int SlugMax, IReadOnlyList<string>? PublicFields, bool Scoped);

    /// <summary> The helpers placed above the routes: <c>make_slug</c>, <c>content_slug</c>, <c>new_content</c>, <c>change_content</c>, <c>set_content_status</c> and <c>public_row</c>. </summary>
    public static string Helpers(Names n) => Fill(HelperText, n,
        ("§CLEARPUBLISHED", n.Published is null ? "" : $"    values[\"{n.Published}\"] = None\n"),
        ("§DROPPUBLISHED", n.Published is null ? "" : $"    changes.pop(\"{n.Published}\", None)\n"),
        ("§STAMP", n.Published is null ? "" : $"    if status == \"{ContentWords.Published}\" and row.{n.Published} is None:\n        row.{n.Published} = {n.PublishedNow}\n"),
        ("§SCOPEARG", n.Scoped ? ", user: CurrentUser" : ""),
        ("§SCOPELINE", n.Scoped ? "    refuse_other_owners(session, user, row)\n" : ""),
        ("§PUBLICFIELDS", n.PublicFields is null ? "None" : "{" + string.Join(", ", n.PublicFields.Select(PythonNames.Str)) + "}"));

    /// <summary> The routes: the three state changes and the two public reads. The public reads go on <paramref name="publicRouter"/> (the router the sign-in check leaves out) and come before the route of one row, so the
    /// fixed path wins. </summary>
    public static string Routes(Names n, string route, string keyType, string writeDepends, string userParameter, string publicRouter) => Fill(RoutesText, n,
        ("§ROUTE", route), ("§KT", keyType), ("§WRITE", writeDepends), ("§USER", userParameter), ("§PUBLIC", publicRouter), ("§SCOPECALL", n.Scoped ? ", user" : ""),
        ("§ORDER", n.Published is null ? $"{n.Class}.{n.Key}.desc()" : $"{n.Class}.{n.Published}.desc(), {n.Class}.{n.Key}.desc()"));

    private static string Fill(string text, Names n, params (string Token, string Value)[] more)
    {
        var values = new List<(string, string)>
        {
            ("§CLASS", n.Class), ("§SCHEMA", n.Schema), ("§KEY", n.Key), ("§TITLE", n.Title), ("§SLUG", n.Slug), ("§STATUS", n.Status), ("§SLUGMAX", n.SlugMax.ToString()),
            ("§DRAFT", ContentWords.Draft), ("§PUBLISHED_WORD", ContentWords.Published), ("§ARCHIVED", ContentWords.Archived), ("§FALLBACK", ContentWords.FallbackSlug),
            ("§TAKENTYPE", ContentWords.SlugTakenType), ("§TAKENTITLE", ContentApiWords.SlugTakenTitle), ("§W_TAKEN", ContentWords.SlugTaken),
            ("§PAGE", ContentApiWords.PublicPageSize.ToString()), ("§MAXPAGE", ContentApiWords.PublicMaxPageSize.ToString())
        };
        values.AddRange(more);
        return TemplateText.FillAll(text, [.. values]);
    }

    private const string HelperText = """"
CONTENT_SLUG_MAX = §SLUGMAX
CONTENT_PUBLIC_FIELDS = §PUBLICFIELDS


def make_slug(text: str | None, max_length: int) -> str:
    """A slug is lower case ASCII letters and digits with single hyphens, at most max_length characters; text with nothing to make one from gives "§FALLBACK"."""
    slug: list[str] = []
    hyphen = False
    for c in unicodedata.normalize("NFD", text or ""):
        if unicodedata.category(c) == "Mn":
            continue
        if c.isascii() and c.isalnum():
            slug.append(c.lower())
            hyphen = False
        elif not hyphen and slug:
            slug.append("-")
            hyphen = True
    made = "".join(slug).strip("-")
    if len(made) > max_length:
        made = made[:max_length].strip("-")
    return made or "§FALLBACK"


def content_slug(session: Session, row_id, named: str | None, title: str | None) -> str:
    """The slug to store: made from the title when none is named, numbered (-2, -3) when a made slug is taken and refused (409) when a slug the caller named is taken."""
    wanted = named if named is not None else make_slug(title, CONTENT_SLUG_MAX)
    candidate = wanted
    number = 2
    while True:
        statement = select(§CLASS.§KEY).where(§CLASS.§SLUG == candidate)
        if row_id is not None:
            statement = statement.where(§CLASS.§KEY != row_id)
        if session.scalar(statement.limit(1)) is None:
            return candidate
        if named is not None:
            raise ApiError(409, "§TAKENTITLE", problem_type="§TAKENTYPE", detail="§W_TAKEN")
        suffix = f"-{number}"
        candidate = (wanted[: CONTENT_SLUG_MAX - len(suffix)].rstrip("-") if len(wanted) + len(suffix) > CONTENT_SLUG_MAX else wanted) + suffix
        number += 1


def new_content(session: Session, values: dict) -> None:
    """A new row is a draft whatever it says, and its slug is made from the title when it is blank."""
    values["§STATUS"] = "§DRAFT"
§CLEARPUBLISHED    slug = values.get("§SLUG")
    named = make_slug(slug, CONTENT_SLUG_MAX) if slug and slug.strip() else None
    values["§SLUG"] = content_slug(session, None, named, values.get("§TITLE"))


def change_content(session: Session, row, changes: dict) -> None:
    """A save never changes the status. The slug stays when it is blank or unchanged (a new title does not move the page); a different slug is a rename on purpose and is checked like a new one."""
    changes.pop("§STATUS", None)
§DROPPUBLISHED    slug = changes.get("§SLUG")
    named = make_slug(slug, CONTENT_SLUG_MAX) if slug and slug.strip() else None
    if named is None or named == row.§SLUG:
        changes.pop("§SLUG", None)
        return
    changes["§SLUG"] = content_slug(session, row.§KEY, named, None)


def set_content_status(session: Session, id, status: str§SCOPEARG) -> §CLASS:
    """Publish (the first time also stamps the published-on time), unpublish (back to a draft) and archive; the row comes back, 404 when there is none. Doing it again changes nothing."""
    row = session.get(§CLASS, id)
    if row is None:
        raise ApiError(404, "Not found.")
§SCOPELINE    row.§STATUS = status
§STAMP    session.commit()
    session.refresh(row)
    return row


def public_row(row) -> dict:
    """What a reader who is not signed in sees of a row: the columns of ContentPublicColumns, or all of them."""
    return §SCHEMA.model_validate(row).model_dump(mode="json", by_alias=True, include=CONTENT_PUBLIC_FIELDS)
"""";

    private const string RoutesText = """"
@router.post("§ROUTE/{id}/publish", response_model=§SCHEMA§WRITE)
def publish(id: §KT, session: Session = Depends(get_session)§USER):
    return set_content_status(session, id, "§PUBLISHED_WORD"§SCOPECALL)


@router.post("§ROUTE/{id}/unpublish", response_model=§SCHEMA§WRITE)
def unpublish(id: §KT, session: Session = Depends(get_session)§USER):
    return set_content_status(session, id, "§DRAFT"§SCOPECALL)


@router.post("§ROUTE/{id}/archive", response_model=§SCHEMA§WRITE)
def archive(id: §KT, session: Session = Depends(get_session)§USER):
    return set_content_status(session, id, "§ARCHIVED"§SCOPECALL)


@§PUBLIC.get("§ROUTE/public")
def get_public(session: Session = Depends(get_session), page_number: int = Query(1, alias="pageNumber"), page_size: int = Query(§PAGE, alias="pageSize")):
    """The published rows, the newest first, a page at a time (pageSize at most §MAXPAGE), for a reader who is not signed in."""
    size = max(1, min(page_size, §MAXPAGE))
    statement = select(§CLASS).where(§CLASS.§STATUS == "§PUBLISHED_WORD").order_by(§ORDER)
    return [public_row(row) for row in session.scalars(statement.offset((max(page_number, 1) - 1) * size).limit(size)).all()]


@§PUBLIC.get("§ROUTE/public/{slug}")
def get_public_by_slug(slug: str, session: Session = Depends(get_session)):
    """One published row by its slug; a draft or an archived row is a 404, as if it were not there."""
    row = session.scalars(select(§CLASS).where(§CLASS.§SLUG == slug, §CLASS.§STATUS == "§PUBLISHED_WORD")).first()
    if row is None:
        raise ApiError(404, "Not found.")
    return public_row(row)


"""";
}
