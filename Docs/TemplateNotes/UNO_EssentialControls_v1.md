# UNO_EssentialControls_v1

The `Controls` essentials group of the Uno stack: the Uno twin of `WinUI3_EssentialControls` (see `WinUI3_EssentialControls_v1.md`). No table and no database. It writes the same four files, the pager (`Views/PaginationBar.xaml` and `.xaml.cs`), `ViewModels/GridSortStore.cs` and `Views/CurrencyNumberFormatter.cs`, in the namespaces and folders of the project (`ViewNamespace`, `ViewModelNamespace`, `ViewsFolder`, `ViewModelsFolder`; default `<AppNamespace>.Views` and `.ViewModels`).

`UNO_MasterScreen`, `UNO_DetailScreen` and `UNO_DetailMasterScreen` use them and no longer write them, so run the group once before generating screens (`codegen essentials --stack uno --groups controls`, the **Essentials** button, or `--essentials` on `generate`). Existing files are kept unless `--replace` is set.
