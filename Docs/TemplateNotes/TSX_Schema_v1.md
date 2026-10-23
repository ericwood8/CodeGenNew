# TSX_Schema_v1

A zod schema, `schemas/<table>.ts`, of the fields a person fills in, and the form type inferred from it (`CustomerForm`). It is not wired to a form: pass it to a resolver (react-hook-form) or call `safeParse`. Needs the `zod` package.

- Each field follows the column: `z.number()` (`.int()` for a whole number), `z.boolean()`, `z.string()` (a GUID adds `.uuid()`), `z.enum([...])` for a CHECK list, `.nullable()` when the column can be NULL.
- The limits are the ones `CS_Validator` writes, from the shared `ColumnRules`: required text, maximum length, a range from the column's name or CHECK (a strict bound is `.gt` / `.lt`), email, phone and URL shapes for columns named like them, degrees for a latitude and a longitude.
- Left out: identity, computed, audit, create and modify columns (the database sets them). The messages use the column's words ("Billing Email is not an email address.").
- Not in a plan unless the project names it in `PlanAlso`; the files go under `src/schemas`.
- **Checked** by compiling the output for the PostgreSQL sample's customer table with `tsc --strict` and running `safeParse` on a good row and a bad one (all four faults reported).
- **Shapes and dates:** a postal code and a social security number get `.regex(...)` with the shared patterns; a column in `NotFutureDateColumns` gets `.refine(...)` against `new Date().toLocaleDateString("en-CA")`.

**Dates in order.** A pair of dates (`DatePairs`, see CS_Validator_v1) is a `.refine` on the whole object, with the sentence on the end field: the same rule as the API validators, on the first 10 characters of the two values.
