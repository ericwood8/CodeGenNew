# TS_GridSort_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: grid-sort.ts   (point the output folder at the Angular app's src\app folder; the components import it as '../../grid-sort')

The support code every generated Angular grid imports for sorting. It does not depend on any table, so it is the same text for every database; it is a
template only so that it is written the same way as the screens and kept in step with them. The header buttons, the right-click menu and the styles are
in each generated component (TS_Component, TS_DetailMasterComponent); what is shared is how the sort is remembered and flipped:
  - loadSort / saveSort: the sort is remembered in the browser (local storage), one entry per grid ("gridSort:<grid>"). It is saved as a list of
    { column, descending } so that sorting by several columns can be added later without changing what is stored. Clearing the sort removes the entry.
    A saved column that the grid no longer has is ignored. Local storage that is unavailable just means the sort is not remembered.
  - nextSort: a click on another column sorts it ascending; a click on the sorted column flips the direction.
  - sortRows: sorts the rows of a grid that holds all of them (a master dialog's child grids) in the browser, comparing real values and the names a column shows.
The sort of a paged grid is applied by the server (a page of 20 rows sorted on its own would be wrong): the components pass it to the service's getPage as sortBy / sortDescending.
```
