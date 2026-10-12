# What the generated SQL does with user text

The generated routines and endpoints take text from people: search boxes, a sort column, a page number, a name to save. This page says where such a value is a **parameter**, where it is **text inside a statement**, and what keeps the second kind safe. The short answer: no value from a request is ever pasted into a statement. Every value is a parameter, and the only text that reaches a statement from a request is a *choice among fixed names* written when the code was generated.

codegen itself only reads the database and writes files. It never creates, alters or runs anything in the target database: you review and apply what it writes.

## Values

| Value | How it reaches the database |
|---|---|
| A search box (a contains match) | A bound parameter in every stack. The routine or query escapes the pattern characters first (see below). |
| A key, a foreign key, any saved column | A bound parameter (`@p`, `$1`, `?`, `{0}`). Insert, update, delete, clone, junction and lookup statements are fixed text with placeholders. |
| `pageNumber` and `pageSize` | Bound integers. The API clamps the size to 1 to 1000 and answers 400 for a page below 1 or an overflowing offset before the database is called. |
| The sort column and direction | A name compared with a fixed list (below), never joined into text. The direction is a boolean or the word `desc` compared in code. |
| A password | Read from the environment or a prompt, never written into a generated file. |

## Search text and the LIKE wildcards

A search is "the column contains the text, ignoring case". In a `LIKE` pattern `%` and `_` (and `[` on SQL Server) mean "anything", so a value must be escaped or a search for `%` matches every row and `a_b` also matches `axb`. Every stack escapes them, so what a person types means itself:

| Stack | Where |
|---|---|
| SQL Server, PostgreSQL, MySQL routines (`SP_Search`) | In the routine: `DialectInfo.EscapeLikeSql` wraps the parameter in nested `REPLACE` calls for `\`, `%`, `_` (and `[`), and the `LIKE` or `ILIKE` says `ESCAPE '\'`. |
| Entity Framework (`AccessMode=Ef`, SQLite) | `CS_SearchQuery` compares with `Contains`, which EF escapes. |
| Python | `contains()` in `app/support.py` escapes with `escape_like` and passes `escape="\\"`. |
| Rust | `escape_like` in `src/support.rs`, with the placeholder bound by sqlx. |

The unescaped form was found while writing this page: the routines passed the text as a parameter, so it could not break out of the statement, but its wildcards still acted as wildcards. That was changed, and a search for `50%` now finds the three characters. If a project wants wildcard searching it can edit the generated routine.

A search is a scan: `LIKE '%text%'` cannot use an index, so on a big table add one by hand or narrow the searched columns.

## The sort column

A sort name from a request is compared with the table's own column names, and only a match chooses an `ORDER BY` expression that was written at generation time:

* **Routines:** `CASE WHEN @SortColumn = N'Name' AND @SortDescending = 0 THEN t.[Name] END ASC, ...` for every sortable column, then the fixed default order. An unknown name matches no `CASE` branch, so the default order applies. The name is never concatenated or executed (no `EXEC(...)`, `sp_executesql` or `PREPARE`).
* **Entity Framework:** a `switch` on the lower-cased name with one `OrderBy` per column.
* **Python:** a dictionary `SORTS` of column objects; an unknown name is `None`.
* **Rust:** `sort_expression` maps a name to a fixed fragment of SQL; the fragment, not the name, is pushed into the statement.

A sort name longer than the longest column name (128 characters; 64 in Python) is ignored before it is compared.

## Statements that contain names

Table and column names appear as text in the generated statements, but they come from the database that was read, not from a request. They are quoted and escaped by the generator (`[x]]y]` for SQL Server, `"x""y"` for PostgreSQL and SQLite, `` `x``y` `` for MySQL), so a name containing a quote, a bracket or a space still produces a valid statement. The dashboard queries are built the same way from the table and column names in the project's `DashboardMeasures` setting, which is the project author's input and not a request value.

No template or Core class gives `FromSqlRaw`, `SqlQueryRaw` or `ExecuteSqlRaw` an interpolated or joined string; `SqlSecurityTests` scans for that, so a new template cannot add one unnoticed.

## What is checked

* `SqlSecurityTests` (unit tests): every filter in every database's `SP_Search` escapes its value and names the escape character; the sort comparison is a `CASE` on fixed names and nothing is executed from text; no raw SQL call is built from an interpolated string; the Python and Rust helpers escape and use a fixed sort map.
* `Docs/Verification/Test-SearchHostileText.py sqlserver|postgres|mysql` (a live check): creates a scratch table, has codegen write `SP_Search` for it, applies it and feeds the routine `%`, `_`, `a_b`, a backslash, a quote, `[x]`, a quote followed by a DROP TABLE and a comment marker, an always-true condition, a `UNION` attempt, unknown and hostile sort names and paging edges. It asserts what comes back, that the table survives, and drops everything it made. `--prove-failure` removes the escaping first and the wildcard checks then fail on all three databases, which shows the checks can fail.
* `Test-ApiSignIn.py` covers the sign-in, the credential column and the owner scope of the Python and Rust APIs.

## What a reviewer should still look at

* A project that edits a generated routine or writes its own raw SQL is outside these guarantees.
* The API has no rate limit on search by default (`ApiProduction=true` adds a per-client limit); a contains search over a large table is expensive.
* Sign-in, tokens and the owner scope are described in `Docs/TemplateNotes/API_EssentialJwt_v1.md` and the Python and Rust route notes.
