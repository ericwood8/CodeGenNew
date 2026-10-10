# BLZ_Page_v1

`Pages/<Table>Page.razor`: the Blazor counterpart of `TSX_Page`. The route is the screen's route (`ScreenNames.Route`), the same one `BLZ_Screens` puts in the menu.

- **Grid:** the columns `ForGrid` picks (never a long text column), a header button that sorts for each sortable column (an arrow shows the direction), 20 rows a page with Previous / Next and a "Rows per page" box (10, 20, 50, 100), "Nothing found." when a loaded page has no rows, a page past the end (the last row of the last page was deleted) that falls back to the last page there is, Edit, Clone and Delete buttons per row.
- **Search:** a box per searchable column, Search and Clear; the sort is kept while paging and searching.
- **Form:** a panel over the page with an `EditForm`. `InputText` (`InputTextArea` for a column of 100 characters or more), `InputNumber`, `InputDate`, `InputCheckbox`, an `InputSelect` of the parent's display column for a foreign key, and of the listed values for a column that has choices. A required column carries `required`. Add New starts date columns at today and yes / no columns at their default.
- **Errors:** a failed load, save, delete or clone shows a sentence (`ApiException.Explain`); deleting a row that is in use says so.

A text key (a country or currency code) is the first field and a grid column: an `InputText` that is typed on a new row and `readonly` on an existing one (the page already keeps `isNew`); a duplicate key (409) says "A country with this alpha3 code already exists!". A drop-down over a text-key parent holds codes and starts on a blank "Select" (`value=""`).

The row being edited is a copy (`ApiSupport.Copy`), so Cancel leaves the grid as it was. Limits are in `BLZ_Screens_v1.md`.
