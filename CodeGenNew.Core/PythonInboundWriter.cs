namespace CodeGenNew.Core;

/// <summary> The Python (FastAPI) counterpart of <see cref="InboundWriter"/>: <c>app/routes/inbound.py</c>, the <c>receive</c> and <c>receive/{source}</c> routes (open to anyone, a signature instead of a login) and the
/// <c>unprocessed</c> and <c>{id}/processed</c> routes of every table in <c>InboundTables</c>, with the same sentences (<see cref="InboundWords"/>), problem types and status codes. The routes are written once; each table adds one
/// <c>InboundSpec</c> that names its model and the attributes of its columns. The receive routes are on <c>public_router</c>, which main.py includes without the sign-in check. </summary>
public static class PythonInboundWriter
{
    public static string File(IReadOnlyList<InboundShape> tables, ProjectSettings project)
    {
        var imports = new SortedSet<string>(StringComparer.Ordinal);
        var specs = new System.Text.StringBuilder();
        var partitioned = tables.Where(q => InboundPlan.HasPartitionScript(q.Table.TableName, project)).Select(q => $"\"{q.Table.TableName}\"").ToList();
        foreach (var q in tables)
        {
            var main = PythonTable.Of(q.Table, project);
            string Attr(ColumnModel? c) => c is null ? "None" : $"\"{main.FieldOf(c).Name}\"";
            imports.Add($"from app.models.{main.Module} import {main.Class}");
            imports.Add($"from app.schemas.{main.Module} import {main.Schema}");
            specs.Append($"    InboundSpec(\n        table=\"{q.Table.TableName}\", route=\"{main.Route}\", row={main.Class}, schema={main.Schema},\n" +
                $"        key={Attr(q.Key)}, body={Attr(q.Body)}, body_max={q.BodyLength},\n" +
                $"        event_id={Attr(q.EventId)}, event_length={Length(q.EventId)}, source={Attr(q.Source)}, source_length={Length(q.Source)},\n" +
                $"        received={Attr(q.Received)}, processed={Attr(q.Processed)}, flag={(q.ProcessedIsFlag ? "True" : "False")}{(q.Client ? ", client=True" : "")},\n    ),\n");
        }
        return TemplateText.FillAll(Template, new (string, string)[]
        {
            ("§CLIENTCONSTANTS", tables.Any(q => q.Client) ? ClientConstants : ""), ("§CLIENTCODE", tables.Any(q => q.Client) ? ClientCode : ""), ("§CLIENTROUTE", tables.Any(q => q.Client) ? ClientRoute : ""),
            ("§CLIENTRATE", project.InboundClientPerMinute.ToString()), ("§CLIENTBYTES", project.InboundClientMaxBytes.ToString()), ("§CLIENTSOURCE", InboundWords.ClientSource),
            ("§T_RATE", InboundWords.RateLimitedType), ("§W_RATE", PythonNames.Str(InboundWords.RateLimitedText)),
            ("§IMPORTS", string.Join("\n", imports)), ("§SPECS", specs.ToString().TrimEnd('\n')),
            ("§AUTHIMPORT\n", project.ApiAuth ? "from app.auth import require_user\n" : ""), ("§DEPS", project.ApiAuth ? ", dependencies=[Depends(require_user)]" : ""),
            ("§PARTITIONED", string.Join(", ", partitioned)), ("§UNPROCLABEL", InboundScreens.UnprocessedLabel), ("§OLDESTLABEL", InboundScreens.OldestLabel),
            ("§SENDERSTEXT", InboundScreens.SendersText(project)), ("§COLLECTTEXT", InboundScreens.CollectText(project)), ("§PARTITIONTEXT", InboundScreens.PartitionText), ("§SENDERS", string.Join(", ", InboundPlan.SourceSecrets(project).Select(s => $"\"{s.Source}\""))), ("§SECRETNAME", project.InboundSecret), ("§SOURCESECRETS\n", SourceSecretsText(project)), ("§SECRETFN\n", SourceSecretFn(project)), ("§SECRETPICK", !HasOwn(project) ? "" : "    secret_name, source = source_secret(source)\n"),
            ("§SECRETPARAM", HasOwn(project) ? ", secret_name: str" : ""), ("§SECRETARG", HasOwn(project) ? ", secret_name" : ""), ("§SECRETVAR", HasOwn(project) ? "secret_name" : "SECRET_NAME"), ("§TOLERANCE", project.InboundToleranceSeconds.ToString()), ("§MAXBYTES", project.InboundMaxBytes.ToString()),
            ("§HEADER", InboundWords.SignatureHeader), ("§EVENTHEADER", InboundWords.EventHeader), ("§ROWS", InboundWords.UnprocessedRows.ToString()),
            ("§T_SIGNATURE", InboundWords.SignatureType), ("§T_EXPIRED", InboundWords.ExpiredType), ("§T_LARGE", InboundWords.TooLargeType),
            ("§W_UNCONFIGURED", PythonNames.Str(InboundWords.UnconfiguredText)), ("§W_SIGNATURE", PythonNames.Str(InboundWords.SignatureText)), ("§W_EXPIRED", PythonNames.Str(InboundWords.ExpiredText)),
            ("§W_LARGE", PythonNames.Str(InboundWords.TooLargeText)), ("§W_EMPTY", PythonNames.Str(InboundWords.EmptyText)), ("§W_NOTSAVED", PythonNames.Str(InboundWords.NotSavedText))
        });
    }

    private static bool HasOwn(ProjectSettings project) => InboundPlan.SourceSecrets(project).Count > 0;

    private static string SourceSecretsText(ProjectSettings project) =>
        HasOwn(project) ? "SOURCE_SECRETS = {" + string.Join(", ", InboundPlan.SourceSecrets(project).Select(s => $"\"{s.Source}\": \"{s.Secret}\"")) + "}\n" : "";

    private static string SourceSecretFn(ProjectSettings project) => !HasOwn(project) ? "" : SourceFn + "\n\n\n";

    private const string SourceFn = """"
def source_secret(source: str | None) -> tuple[str, str | None]:
    """The environment variable that holds the secret of the sender and the sender's name as configured; the shared secret for any other sender."""
    for name, secret in SOURCE_SECRETS.items():
        if source is not None and name.lower() == source.lower():
            return secret, name
    return SECRET_NAME, source
"""";

    private static string Length(ColumnModel? column) => column is null ? "0" : (column.CharacterLength > 0 ? column.CharacterLength : 0).ToString();

    private const string ClientConstants = """
CLIENT_PER_MINUTE = §CLIENTRATE
CLIENT_MAX_BYTES = §CLIENTBYTES
CLIENT_SOURCE = "§CLIENTSOURCE"
PROBLEM_RATE_LIMITED = "§T_RATE"
RATE_LIMITED = §W_RATE

""";

    private const string ClientCode = """"
client_counts: dict[str, tuple[int, int]] = {}


def reset_client_limit() -> None:
    """Forgets the counts of the callers; the generated tests use it."""
    client_counts.clear()


def client_allowed(table: str, caller: str) -> bool:
    """A fixed window of one minute for each table and caller address, kept in the memory of this process."""
    minute = int(time.time()) // 60
    if len(client_counts) > 10000:
        for old in [key for key, (window, _) in client_counts.items() if window != minute]:
            del client_counts[old]
    key = f"{table}|{caller}"
    window, count = client_counts.get(key, (minute, 0))
    count = count + 1 if window == minute else 1
    client_counts[key] = (minute, count)
    return count <= CLIENT_PER_MINUTE


async def collect(spec: InboundSpec, request: Request) -> Response:
    """For a browser: open to anyone, no signature, write only (the answer is 202 with no body), a small body and a limit of requests a minute for each caller address."""
    caller = request.client.host if request.client else "unknown"
    if not client_allowed(spec.table, caller):
        raise ApiError(429, "Too many requests", problem_type=PROBLEM_RATE_LIMITED, detail=RATE_LIMITED, headers={"Retry-After": str(60 - int(time.time()) % 60)})
    raw = await request.body()
    text = raw.decode("utf-8", errors="replace")
    if len(raw) > CLIENT_MAX_BYTES or (spec.body_max and len(text) > spec.body_max):
        raise ApiError(413, "Too large", problem_type=PROBLEM_TOO_LARGE, detail=TOO_LARGE)
    if not text:
        raise ApiError(400, "Bad request", detail=EMPTY)
    values: dict[str, Any] = {spec.body: text, spec.received: datetime.now()}
    if spec.source:
        values[spec.source] = fit(CLIENT_SOURCE, spec.source_length)
    if spec.flag:
        values[spec.processed] = False
    with SessionLocal() as session:
        session.add(spec.row(**values))
        try:
            session.commit()
        except IntegrityError:
            session.rollback()
            raise ApiError(400, "Bad request", detail=NOT_SAVED)
    return Response(status_code=202)



"""";

    private const string ClientRoute = """
    if spec.client:

        @public_router.post(spec.route + "/collect", status_code=202, name=spec.table + "Collect")
        async def collect_route(request: Request):
            return await collect(spec, request)

""";

    private const string Template = """"
"""The inbound event landing tables (InboundTables): a sender posts a signed message that is kept as text, a worker lists the unprocessed events and marks each processed. The same routes, sentences and problem types as the
ASP.NET API (API_Inbound). The receive routes are open to anyone, since the signature is the check. Generated by PY_Inbound."""
import hashlib
import hmac
import json
import os
import time
from dataclasses import dataclass
from datetime import datetime
from typing import Any

from fastapi import APIRouter, Depends, Request, Response
from sqlalchemy import false, func, or_, select, update
from sqlalchemy.exc import IntegrityError
from sqlalchemy.orm import Session

§AUTHIMPORT
from app.db import SessionLocal, get_session
§IMPORTS
from app.support import ApiError

SECRET_NAME = "§SECRETNAME"
SENDERS = [§SENDERS]
PARTITION_SCRIPTS = [§PARTITIONED]
SENDERS_TEXT = "§SENDERSTEXT"
COLLECT_TEXT = "§COLLECTTEXT"
PARTITION_TEXT = "§PARTITIONTEXT"
§SOURCESECRETS
SIGNATURE_HEADER = "§HEADER"
EVENT_HEADER = "§EVENTHEADER"
TOLERANCE = §TOLERANCE
MAX_BYTES = §MAXBYTES
UNPROCESSED_ROWS = §ROWS
PROBLEM_SIGNATURE = "§T_SIGNATURE"
PROBLEM_EXPIRED = "§T_EXPIRED"
PROBLEM_TOO_LARGE = "§T_LARGE"
UNCONFIGURED = §W_UNCONFIGURED
BAD_SIGNATURE = §W_SIGNATURE
EXPIRED = §W_EXPIRED
TOO_LARGE = §W_LARGE
EMPTY = §W_EMPTY
NOT_SAVED = §W_NOTSAVED
§CLIENTCONSTANTS

@dataclass(frozen=True)
class InboundSpec:
    """One table of InboundTables: its model and schema and the attribute names of its columns (event_id and source are optional; a length of 0 means no limit)."""

    table: str
    route: str
    row: Any
    schema: Any
    key: str
    body: str
    body_max: int
    event_id: str | None
    event_length: int
    source: str | None
    source_length: int
    received: str
    processed: str
    flag: bool
    client: bool = False

    def col(self, name: str) -> Any:
        return getattr(self.row, getattr(self, name))


SPECS = [
§SPECS
]

router = APIRouter()
public_router = APIRouter()   # the receive routes: main.py includes it without the sign-in check


def fit(text: str | None, length: int) -> str | None:
    return text if text is None or length == 0 or len(text) <= length else text[:length]


def json_event_id(text: str) -> str | None:
    """The id property of a JSON body, as text."""
    try:
        message = json.loads(text)
    except ValueError:
        return None
    if not isinstance(message, dict) or message.get("id") is None:
        return None
    value = message["id"]
    return value if isinstance(value, str) else json.dumps(value, separators=(",", ":"))


§CLIENTCODEdef unprocessed_test(spec: InboundSpec) -> Any:
    column = spec.col("processed")
    return or_(column.is_(None), column == false()) if spec.flag else column.is_(None)


def find_event(session: Session, spec: InboundSpec, event: str, source: str | None) -> Any | None:
    statement = select(spec.col("key")).where(getattr(spec.row, spec.event_id) == event)
    if spec.source:
        statement = statement.where(getattr(spec.row, spec.source) == source)
    return session.scalars(statement.limit(1)).first()


§SECRETFN
def verified_text(spec: InboundSpec, request_headers: Any, raw: bytes§SECRETPARAM) -> str:
    """The body as text once the secret, the size, the signature and the signed time are right; raises the problem otherwise."""
    secret = os.environ.get(§SECRETVAR, "")
    if not secret:
        raise ApiError(503, "Not configured", detail=UNCONFIGURED)
    if len(raw) > MAX_BYTES:
        raise ApiError(413, "Too large", problem_type=PROBLEM_TOO_LARGE, detail=TOO_LARGE)
    parts = dict(item.strip().split("=", 1) for item in request_headers.get(SIGNATURE_HEADER, "").split(",") if "=" in item)
    try:
        signed_at = int(parts.get("t", ""))
    except ValueError:
        signed_at = None
    expected = hmac.new(secret.encode(), f"{signed_at}.".encode() + raw, hashlib.sha256).hexdigest()
    if signed_at is None or not hmac.compare_digest(expected, parts.get("v1", "").lower()):
        raise ApiError(401, "Bad signature", problem_type=PROBLEM_SIGNATURE, detail=BAD_SIGNATURE)
    if abs(time.time() - signed_at) > TOLERANCE:
        raise ApiError(400, "Message expired", problem_type=PROBLEM_EXPIRED, detail=EXPIRED)
    text = raw.decode("utf-8", errors="replace")
    if not text:
        raise ApiError(400, "Bad request", detail=EMPTY)
    if spec.body_max and len(text) > spec.body_max:
        raise ApiError(413, "Too large", problem_type=PROBLEM_TOO_LARGE, detail=TOO_LARGE)
    return text


async def receive(spec: InboundSpec, request: Request, source: str | None) -> dict[str, Any]:
§SECRETPICK    text = verified_text(spec, request.headers, await request.body()§SECRETARG)
    source_text = fit(source, spec.source_length)
    event = None
    if spec.event_id:
        event = request.headers.get(EVENT_HEADER) or json_event_id(text)
        event = fit(event, spec.event_length)
    with SessionLocal() as session:
        if event:
            existing = find_event(session, spec, event, source_text)
            if existing is not None:
                return {"received": False, "duplicate": True, "id": existing}
        values: dict[str, Any] = {spec.body: text, spec.received: datetime.now()}
        if spec.event_id and event:
            values[spec.event_id] = event
        if spec.source and source_text is not None:
            values[spec.source] = source_text
        if spec.flag:
            values[spec.processed] = False
        row = spec.row(**values)
        session.add(row)
        try:
            session.commit()
        except IntegrityError:
            session.rollback()
            if event:
                winner = find_event(session, spec, event, source_text)
                if winner is not None:
                    return {"received": False, "duplicate": True, "id": winner}
            raise ApiError(400, "Bad request", detail=NOT_SAVED)
        return {"received": True, "duplicate": False, "id": getattr(row, spec.key)}


def unprocessed(session: Session, spec: InboundSpec) -> list[Any]:
    return list(session.scalars(select(spec.row).where(unprocessed_test(spec)).order_by(spec.col("received"), spec.col("key")).limit(UNPROCESSED_ROWS)).all())


def status(session: Session, spec: InboundSpec) -> dict[str, Any]:
    """How many events there are and how many wait, the oldest one that waits, and what the generator was told about this table."""
    waiting = unprocessed_test(spec)
    total = session.scalar(select(func.count()).select_from(spec.row)) or 0
    count = session.scalar(select(func.count()).select_from(spec.row).where(waiting)) or 0
    oldest = session.execute(select(spec.col("key"), spec.col("received")).where(waiting).order_by(spec.col("received"), spec.col("key")).limit(1)).first()
    summary = f"§UNPROCLABEL{count} of {total}" + (f"§OLDESTLABEL{oldest[0]}" if oldest else "") + SENDERS_TEXT + (COLLECT_TEXT if spec.client else "") + (PARTITION_TEXT if spec.table in PARTITION_SCRIPTS else "") + "."
    return {
        "summary": summary,
        "total": total,
        "unprocessed": count,
        "oldestUnprocessedId": oldest[0] if oldest else None,
        "oldestUnprocessedOn": oldest[1].isoformat() if oldest else None,
        "senders": SENDERS,
        "collect": {"enabled": spec.client, "perMinute": §CLIENTRATE, "maxBytes": §CLIENTBYTES},
        "partitionScript": spec.table in PARTITION_SCRIPTS,
    }


def mark_processed(session: Session, spec: InboundSpec, key: int) -> dict[str, Any]:
    """One UPDATE that still tests that the event is unprocessed, so two workers never both succeed."""
    value = True if spec.flag else datetime.now()
    changed = session.execute(update(spec.row).where(spec.col("key") == key, unprocessed_test(spec)).values({spec.processed: value})).rowcount
    session.commit()
    session.expire_all()
    row = session.get(spec.row, key)
    if row is None:
        raise ApiError(404, "Not found.")
    return {"processed": changed == 1, "alreadyProcessed": changed == 0, "row": spec.schema.model_validate(row).model_dump(by_alias=True, mode="json")}


def add_routes(spec: InboundSpec) -> None:
    """The routes of one table."""
§CLIENTROUTE
    @public_router.post(spec.route + "/receive", name=spec.table + "Receive")
    async def receive_route(request: Request):
        return await receive(spec, request, None)

    @public_router.post(spec.route + "/receive/{source}", name=spec.table + "ReceiveFrom")
    async def receive_from_route(source: str, request: Request):
        return await receive(spec, request, source)

    @router.get(spec.route + "/unprocessed", response_model=list[spec.schema], name=spec.table + "Unprocessed"§DEPS)
    def unprocessed_route(session: Session = Depends(get_session)):
        return unprocessed(session, spec)

    @router.get(spec.route + "/status", name=spec.table + "Status"§DEPS)
    def status_route(session: Session = Depends(get_session)):
        return status(session, spec)

    @router.post(spec.route + "/{id}/processed", name=spec.table + "Processed"§DEPS)
    def processed_route(id: int, session: Session = Depends(get_session)):
        return mark_processed(session, spec, id)


for _spec in SPECS:
    add_routes(_spec)
"""";
}
