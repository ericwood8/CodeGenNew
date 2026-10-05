# RS_Validate_v1

`src/validation/<table>.rs`: `validate(&row)`, the rules the schema states for a row, written only for a project with `ApiValidation=true` (the Rust counterpart of `CS_Validator`).

- The rules are `ColumnRules` (Core), the list the FluentValidation validators and the TypeScript validators follow: required text (not NULL means not empty), the longest length, the values a CHECK list or an enum allows, an email, phone or URL shape by the column's name, and the range a CHECK or the column's name gives a number. A column the system fills in (identity, audit, computed) is not checked.
- It is plain Rust, with no validation crate: the messages are literals, the checks are `if` statements, and the shape checks (`looks_like_email`, `looks_like_phone`, `looks_like_url` in `support.rs`) are deliberately loose, catching a typo and not an unusual address. A table with nothing to check gets a `validate` that returns `Ok`.
- The answer is `400` with the ASP.NET validation-problem JSON: `{ "type", "title", "status", "errors": { "AccountNumber": [ "Account Number must be 50 characters or fewer." ] } }`, every problem of the row at once, by property name.
- **Not done:** the `validator` or `garde` derive the first plan named (a setting `RustValidator`); the generated functions need no crate and no version to keep in step.
