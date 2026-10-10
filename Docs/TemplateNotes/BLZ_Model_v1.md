# BLZ_Model_v1

`Models/<Table>.cs`: the class a table's JSON becomes in the Blazor app, one property per column, in the namespace `<ProjectName>.Blazor.Models`. The API writes camelCase and reads either case, so the property names are the column names. A non-null text property starts as `""` and a binary one as an empty array, so the nullable checks stay quiet.

Written for the tables that have an API of their own, a single int, guid or text key and no Name + IsActive pair; any other table is refused with the reason. See `BLZ_Screens_v1.md` for the whole Blazor stack.
