# UNO_Model_v1

`Models/<Table>.cs`: the class a table's JSON becomes in the Uno app, one property per column, in the namespace `<AppNamespace>.Models`. The API writes camelCase and reads either case, so the property names are the column names. A non-null text property starts as `""` and a binary one as an empty array, so the nullable checks stay quiet. With `ApiAnnotations=true` the class carries the same validation attributes as the API's entity.

The tables that get a model are the tables that get a client (`UnoNames.HasClient`, the Blazor stack's rule): the API has CRUD for the table, its key is a single int, long, guid or text column, and it is not a name / active table. See `UNO_Screens_v1.md` for the whole stack.
