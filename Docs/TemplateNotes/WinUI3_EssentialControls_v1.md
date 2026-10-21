# WinUI3_EssentialControls_v1

The `Controls` essentials group of the WinUI3 stack: the controls and helpers every generated list page and dialog shares. No table and no database.

- `Views/PaginationBar.xaml` and `.xaml.cs`: Previous, Next, the page label and the rows-per-page box under a grid.
- `ViewModels/GridSortStore.cs`: remembers each grid's sort between runs in `GridSorts.json` in the local application data folder (a list of `{ Column, Descending }` per grid).
- `Views/CurrencyNumberFormatter.cs`: formats a number as currency and reads it with or without the symbol, for the currency number boxes.

The table templates (`WinUI3_MasterScreen`, `WinUI3_DetailScreen`, `WinUI3_DetailMasterScreen`) use them and no longer write them, so run this group once before generating screens (`codegen essentials --stack winui3 --groups controls`, the **Essentials** button, or `--essentials` on `generate`). Existing files are kept unless `--replace` is set.

Namespaces and folders follow the project: `ViewNamespace` (default `<AppNamespace>.Views`), `ViewModelNamespace`, `ViewsFolder` and `ViewModelsFolder`.

Not part of the group: a status / busy overlay and an error base for the view models. Those need the table templates to adopt them first (item 56). The Uno stack has the same group (`UNO_EssentialControls`).
