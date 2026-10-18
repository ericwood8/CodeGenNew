# BLZ_Junction_v1

For each many-to-many junction table (`TableModel.IsJunctionTable`) whose anchor table has a lookup endpoint: `Services/<Table>JunctionClient.cs` and `Pages/<Table>JunctionPage.razor`. The Blazor twin of `UNO_JunctionEditor` and `TSX_JunctionPage`, written as a routed page (`/<table>-links`, for example `/item-warehouse-links`) rather than a dialog. A drop-down picks the row of the anchor table (the junction's first foreign key, read from its lookup endpoint); the Available and Selected lists show the target table's display columns, and `>>`, `>`, `<`, `<<` move rows between them. Click a row to pick it (click again to unpick), then `>` or `<` moves the picked rows; `>>` and `<<` move every row.

Each move commits at once through the routes `API_Junction` writes (`GET <route>/junction/{anchorId}`, `POST .../junction/link` and `.../junction/unlink`), so there is no Save; a refused or failed call stops the move and shows its sentence above the lists. The client, `<Table>JunctionItem` and `<Table>JunctionLinkRequest` are in the client file; `BLZ_Screens` registers the client in `ApiClients.cs` and adds the menu entry `<Table> Links` after the other screens. With `Auth=true` the page needs a signed-in person.

The lists are plain `div` rows with an outer border and a line under each row, and the four buttons sit on a shaded bar; the styles are in the page itself.

A junction table whose anchor table has no lookup endpoint (`NoApi` or `NoLookup` for it) gets no page: the template refuses it with that reason.
