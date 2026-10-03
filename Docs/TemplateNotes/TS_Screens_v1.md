# TS_Screens_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: app.routes.ts   (see OutputName in TS_Screens_v1.tt.config; point the output folder at the Angular app's src\app folder)

The whole of an Angular app's route file: the list of screens (route, menu text, component) in menu order, and the routes built from it. app.ts and
app.html (the hamburger button, the menu) read `screens` from here and stay hand-written; adding a table is a regenerate, not an edit.
  - route: ScreenNames.Route, the same text TS_DetailMasterComponent uses to link a child row to its screen (/sales-invoice?edit=<id>), so a link and a
    menu entry cannot disagree;
  - component: <Base>DetailMasterComponent (folder <stem>-detail-master) for a master-detail table, <Base>Component (folder <stem>) for any other,
    the names TS_DetailMasterComponent / TS_Component write.
A master-detail screen whose child table has no screen is named in a comment at the top of the file: its child-row link would open nothing.
```
