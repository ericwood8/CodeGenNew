# FS_Rop_v1

The shared module of the F# family (no table, no database).

- `succeed`, `fail`, `either`, `bind`, `bind2` over F#'s own `Result`; `apply` and the `<*>` operator, which join the errors of several failed fields instead of stopping at the first.
- `Parse`: text readers (`int32`, `decimal`, `dateTime`, `boolean` ...) and the field helpers `required`, `optional` and `orDefault` that read a column's text out of a `Map`.
- Run it by hand once: `codegen -T FS_Rop.tt --project <name> -o <folder>`.
