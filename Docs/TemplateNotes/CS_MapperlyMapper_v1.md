# CS_MapperlyMapper_v1

`<Table>Mapper.cs`: a `[Mapper] public static partial class` with `ToDto`, `ToEntity` and `ProjectToDto(this IQueryable<Entity>)`. Mapperly's source generator writes the bodies at compile time, so a property added to the entity and the transfer class is copied without editing this file. The hand-written alternative is `CS_Mapper`.

- Needs the `Riok.Mapperly` package, the entity (`CS_Entity`) and the transfer class (`CS_Dto`).
- `ToDto` requires every target member to be mapped; `ToEntity` requires every source member. The entity's navigation properties have no counterpart in the transfer class, so they are neither required nor mapped.
- Namespaces: `EntityNamespace` and `DtoNamespace`. Not in a plan unless the project names it in `PlanAlso`; the files go under `Mappers`.
- **Checked** by building the PostgreSQL sample's customer entity, transfer class and this mapper in a scratch project: no Mapperly warnings, and an entity maps to the transfer class and back.
