# TSX_GridSort_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: components/gridSort.tsx   (point the output folder at the React app's src folder)

The support code every generated React grid imports for sorting (the same way PaginationBar is shared). It does not depend on any table, so it is the
same text for every database; it is a template only so that it is written the same way as the screens and kept in step with them.
  - SortHeader: a <th> with a button; clicking it sorts by that column, clicking it again flips the direction. The sorted column shows an arrow.
  - GridMenu: wraps a grid; a right-click opens a small menu with "Clear sort" (disabled when the grid is not sorted).
  - loadSort / saveSort: the sort is remembered in the browser (local storage), one entry per grid ("gridSort:<grid>"). It is saved as a list of
    { column, descending } so that sorting by several columns can be added later without changing what is stored. Clearing the sort removes the entry.
    A saved column that the grid no longer has is ignored. Local storage that is unavailable just means the sort is not remembered.
  - nextSort: a click on another column sorts it ascending; a click on the sorted column flips the direction.
  - sortRows: sorts the rows of a grid that holds all of them (a master dialog's child grids) in the browser, comparing real values and the names a column shows.
The sort of a paged grid is applied by the server (a page of 20 rows sorted on its own would be wrong): the pages pass it to the api's getPage as sortBy / sortDir.
```
