# CS_MapperlyTest_v1

An MSTest class per table (`<Table>MapperlyTests.cs`) for the mapper `CS_MapperlyMapper` writes. It needs no database.

- An entity is copied to the transfer class with `ToDto` and back with `ToEntity`, and every column is compared.
- `ProjectToDto` over an `IQueryable` of the entity gives the same row.
- A table without a primary key is refused (the mapper needs one), as is a table with a column of an unsupported type.

Needs the `Riok.Mapperly` package in the API project: `CS_MapperlyMapper` needs it too and nothing adds it, so `dotnet add package Riok.Mapperly` first. Name both templates in `PlanAlso` (`PlanAlso=CS_MapperlyMapper,CS_MapperlyTest`) for a whole-project run; the test goes to the test project (`ApiTests=true` writes it).
