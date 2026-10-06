# CS_CqrsHandlers_v1

Commands and queries without a package. Whole-database; for the tables with an API of their own, a single int key and no Name + IsActive pair (the tables that have a repository). Opinionated, so it runs only in a plan that names it: `PlanAlso=CS_CqrsHandlers`.

Writes, under `Application/` of the API project:

- `Abstractions.cs`: `ICommand<TResult>`, `IQuery<TResult>`, `ICommandHandler<TCommand, TResult>`, `IQueryHandler<TQuery, TResult>`, `DeleteOutcome` (`Deleted`, `NotFound`, `InUse`) and `PagedList<T>`.
- `<Table>Handlers.cs` per table: `Create<Table>Command` (returns the row), `Update<Table>Command` (returns the row, or null when there is no such row), `Delete<Table>Command` (a `DeleteOutcome`), `Get<Table>ByIdQuery` and `Get<Table>ListQuery` (page number, page size, sort column and direction, and a text filter per searchable column), each with its handler.
- `ApplicationRegistration.cs`: `AddApplicationHandlers()` registers every handler as scoped.

The command handlers use the table's repository (`<Table>Repo`), so the create, update and delete behave as the generated API does (a delete blocked by a foreign key is `InUse`). The list handler queries the context directly (`AsNoTracking`, `Contains` filters, a `switch` over the sortable columns, the page clamped to 1 to 200 rows), so it works in either access mode. With `ApiValidation=true` the create and update handlers take the table's `IValidator<T>` and run it first (`ValidationException`).

Nothing calls the handlers: an endpoint, a controller or a mediator of your own asks for `ICommandHandler<...>` / `IQueryHandler<...>`. The existing repository and API stay the default. Namespace: `ApplicationNamespace` (default `<ProjectName>.App.Application`).

**Checked:** generated for the PostgreSQL InvoiceSystem sample, compiled in the generated API, and run through a throwaway endpoint: create, update, get by id, a filtered and sorted list, a second page, an update of a missing id (null), and delete twice (`Deleted`, then `NotFound`); the row was removed again.

Not done: `CS_Specification` (a query object per table) and the MediatR form, which needs the package.
