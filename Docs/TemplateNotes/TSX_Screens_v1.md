# TSX_Screens_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: screens.tsx   (see OutputName in TSX_Screens_v1.tt.config; point the output folder at the React app's src folder)

The list of screens a React app's shell shows: one entry per table (route, menu text, page element), in menu order. App.tsx imports it

    import { screens } from './screens';

and renders the menu links and the <Route> elements from it, so adding a table is a regenerate, not an edit of App.tsx. The hamburger
button, the layout and the rest of App.tsx stay hand-written.
  - route: ScreenNames.Route, the same text TSX_DetailMasterPage uses to link a child row to its screen, so a link and a menu entry cannot disagree;
  - page: <Base>DetailMasterPage for a master-detail table, <Base>Page for any other (TSX_DetailMasterPage / TSX_Page file names).
The tables are Database.ScreenTables(Project, "React"): a table with a uniqueidentifier or text key is listed because React reads KeyType (KeyType.StackHandlesEveryKey).
A master-detail screen whose child table has no screen is named in a comment at the top of the file: its child-row link would open nothing.
```
