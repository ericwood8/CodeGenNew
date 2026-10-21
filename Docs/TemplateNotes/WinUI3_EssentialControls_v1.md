# WinUI3_EssentialControls_v1

The `Controls` essentials group of the WinUI3 stack: the controls and helpers every generated list page and dialog shares. No table and no database.

- `ViewModels/ViewModelBase.cs`: the base of every generated view model (`ErrorMessage` and `HasError`, which the pages show in their error bar, and `IsBusy` with `RunBusyAsync(work)`, which sets the flag around the work and clears it even when the work throws; nested calls clear it when the last one ends).
- `Views/BusyOverlay.xaml` and `.xaml.cs`: a dimmed cover with a progress ring over the area it is placed in while `IsActive` is true; it blocks clicks on what is under it. The list page puts one over its grid, bound to `ViewModel.IsBusy`, so a load shows it.
- `Views/PaginationBar.xaml` and `.xaml.cs`: Previous, Next, the page label and the rows-per-page box under a grid.
- `ViewModels/GridSortStore.cs`: remembers each grid's sort between runs in `GridSorts.json` in the local application data folder (a list of `{ Column, Descending }` per grid).
- `Views/CurrencyNumberFormatter.cs`: formats a number as currency and reads it with or without the symbol, for the currency number boxes.

The table templates (`WinUI3_MasterScreen`, `WinUI3_DetailScreen`, `WinUI3_DetailMasterScreen`) use them and no longer write them, so run this group once before generating screens (`codegen essentials --stack winui3 --groups controls`, the **Essentials** button, or `--essentials` on `generate`). Existing files are kept unless `--replace` is set.

Namespaces and folders follow the project: `ViewNamespace` (default `<AppNamespace>.Views`), `ViewModelNamespace`, `ViewsFolder` and `ViewModelsFolder`.

The three table view models derive from `ViewModelBase` and no longer repeat `ErrorMessage` and `HasError`; the list view model's `LoadAsync` runs through `RunBusyAsync`. The detail dialogs do not show the overlay (a save is quick and has its own error bar). The Uno group (`UNO_EssentialControls`) writes the same files.
