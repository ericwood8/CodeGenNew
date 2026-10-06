# MD_Postman_v1

`collections/<ProjectName>.postman_collection.json`: a Postman collection (v2.1) for the generated API. Whole-database; runs only in a plan that names it (`PlanAlso=MD_Postman`).

A folder per table that has an API of its own, with the requests `API_Http` writes (`ApiRequests` in Core is the one list): all, one by id, search (a table that has a search endpoint, with the first two filters, paging and a sort), add and change (a body with a sample value for every column that cannot be NULL, shaped by the column; the key is `{{id}}` in a change), remove, and copy (a table that can be cloned). `baseUrl` (`http://localhost:<ApiPort>`) and `id` (1) are collection variables. The request bodies are raw JSON with the JSON content type.

Import it in Postman, or run it with newman. It does not log in: add an Authorization header if the API has authentication. `MD_Bruno` writes the same requests for Bruno.
