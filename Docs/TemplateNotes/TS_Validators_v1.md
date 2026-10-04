# TS_Validators_v1

The Angular twin of `TSX_Schema`: `validators/<table>.validators.ts` exports `customerValidators`, a `Record<string, ValidatorFn[]>` keyed by the JSON property name, for `FormBuilder` or `new FormControl(value, validators.name)`.

- Only a field the schema says something about appears: `Validators.required` and `maxLength` for text, `pattern` for a CHECK list and for phone and URL shapes, `Validators.email`, `min` and `max` for a range. The rules are the shared `ColumnRules` of `CS_Validator` and `TSX_Schema`.
- A strict bound (`> 0`) has no Angular validator, so the file gets a small `greaterThan` or `lessThan` function, and only when one is needed.
- Not in a plan unless the project names it in `PlanAlso`; the files go under `src/app/validators`. A table the schema says nothing about gets an empty record.
- **Checked** by compiling the output for the PostgreSQL sample's customer table against `@angular/forms` with `tsc --strict`.
