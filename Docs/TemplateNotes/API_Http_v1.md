# API_Http_v1

A `.http` request file per table (`<Table>.http`, under `http` in a plan), for Visual Studio, Visual Studio Code (REST Client) and Rider.

- **Requests:** all rows, one row by `{{id}}`, search (the first two searchable columns as filters, `pageNumber`, `pageSize`, `sortBy`, `sortDir`) where `API_Search` writes one, create, update, delete and, where the clone routine exists, clone. The host is `http://localhost:<ApiPort>`; `@host` and `@id` are at the top.
- **Bodies:** one value for every column that cannot be NULL (and the key), by the column: a choice from its CHECK list, a whole number inside its range (the minimum or 1), a decimal inside its CHECK range (a strict bound steps just above it), an email address for `*Email*`, a phone number, a URL, a date, `false`, a GUID; the identity key is `0` on create and `{{id}}` on update. A nullable column is left out. JSON names are camel case (`JsonNames.Camel`).
- **Which tables:** the ones `API_Crud` writes an API for (`TableModel.HasCrudApi`, the rule the OpenAPI document and the database model use too); any other table is refused with the reason.
- **Plan:** `ApiHttp=true` in the project file adds it to a whole-project run (or name it in `PlanAlso`).
- No login: add an `Authorization` header by hand if the API has authentication.
