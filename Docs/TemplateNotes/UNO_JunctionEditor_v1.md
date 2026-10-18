# UNO_JunctionEditor_v1

For each many-to-many junction table (`TableModel.IsJunctionTable`): `Services/<Table>JunctionClient.cs`, `Views/<Table>JunctionEditor.xaml` and `.xaml.cs`, `ViewModels/<Table>JunctionEditorViewModel.cs`. The Uno twin of `WinUI3_JunctionEditor`: a `ContentDialog` with an Available and a Selected list and `>>`, `>`, `<`, `<<` buttons that move rows between them. Each move commits at once through the routes `API_Junction` writes (`GET <route>/junction/{anchorId}`, `POST .../junction/link` and `.../junction/unlink`), so there is no Save; a refused or failed call stops the move and shows its sentence in the dialog.

The client class, `<Table>JunctionItem` (the target side's display columns, `TargetId`, `IsSelected` and `DisplayText`) and `<Table>JunctionLinkRequest` are in the client file; `ApiClients` holds the client as `<Table>Junction`. Open the editor from the page of the anchor table (the junction's first foreign key): `new <Table>JunctionEditor(api, anchorId) { XamlRoot = XamlRoot }` and `ShowAsync()`. Like the WinUI 3 editor it is not wired into a list page by the generator.

**Checked** (2026-10-18): a SQLite shop (Person, Tag, PersonTag) with the generated API and the desktop app: the editor opened for person 1 with the right lists, `>` wrote the row into `PersonTag`, `<<` emptied it.
