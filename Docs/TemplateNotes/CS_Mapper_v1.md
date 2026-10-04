# CS_Mapper_v1

A static class copying one table between the entity (`CS_Entity`) and the transfer class (`CS_Dto`).

- `ToDto`, `ToEntity`, `ToDtos`, `ToEntities` (a null in gives a null or an empty list out) and `FromReader`.
- One property line per column. Navigation properties are not copied: the transfer class has none.
- Needs a primary key (the entity does). Run `CS_Entity` and `CS_Dto` for the same table; the entity namespace is `EntityNamespace`, the mapper and the transfer class are in `DtoNamespace`.
