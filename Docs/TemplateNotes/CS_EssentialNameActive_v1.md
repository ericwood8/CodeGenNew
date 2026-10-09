# CS_EssentialNameActive_v1

The `NameActive` group of the API and WinUI3 essentials: three files for the whole project, `Entities/<BaseNameActiveEntity>.cs`, `Repositories/INameActiveRepo.cs` and `Repositories/NameActiveRepo.cs` (`codegen essentials --stack api --groups nameactive --project <name> -o <project root>`). No table and no database are needed. Needs the BaseClasses group (`BaseEntity`, `GenericRepo`) and the context.

They are the bases of a table with a NOT NULL text `Name` and a NOT NULL bit `IsActive` column: `CS_Entity` derives the entity from the entity base (the project's `BaseNameActiveEntity`, default `BaseNameActiveEntity`), `CS_Repo` derives the repository from `NameActiveRepo<T>`, and `API_Crud` writes the API on `NameActiveCrudApi` (the CrudApi group).

- `BaseNameActiveEntity` is `BaseEntity` plus `required string Name` and `required bool IsActive = true`.
- `NameActiveRepo<T>` has `GenericRepo`'s calls plus `GetAllActive()`, `GetByName(text)` (active rows that start with the text), `IsDupOnCreateAsync` and `IsDupOnUpdateAsync`. The name is trimmed on create and update. A row is a duplicate when another **active** row has the same name (upper and lower case follow the database collation); an inactive row never blocks a name and an update that makes a row inactive is never refused. `AddAsync` answers false and `UpdateAsync` null for a duplicate. `DeleteAsync` answers 0 (deleted), -1 (gone), 1 (a foreign key blocks it), like `GenericRepo`. `GetAll` lists by name.

Written once and kept, like every essentials file.
