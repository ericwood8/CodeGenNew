# CS_EfConfiguration_v1

`Data/Configurations/<Table>Configuration.cs`: one `IEntityTypeConfiguration<Entity>` per table, in the namespace `<ContextNamespace>.Configurations`. Opt in with `EfConfigurations=true` (the plan then writes these files, and `CS_DbContext` changes as below) or `PlanAlso=CS_EfConfiguration`. Needs a primary key, like the entity class.

What a configuration says:

- `ToTable("name")` (with the schema for a SQL Server or PostgreSQL table that is not in `dbo` / `public`);
- `HasKey`, a composite key too;
- per column, only what the class does not state by its type: `HasColumnName` (when `NamingStyle` gave the property another name), `HasColumnType` (money, a PostgreSQL `timestamp` or `jsonb`), `HasMaxLength`, `HasPrecision`, `IsRequired` for text, `ValueGeneratedOnAdd` (identity), `ValueGeneratedOnAddOrUpdate` (computed), `IsRowVersion`;
- each index (`HasIndex`, `HasDatabaseName`, `IsUnique`), except the one that is the primary key.

Foreign keys stay where they are (the `[ForeignKey]` attributes and navigation properties of `CS_Entity`). The entity class keeps its attributes, so the two say the same thing; the configuration is the place to make a hand edit.

**`CS_DbContext` with `EfConfigurations=true`:** `OnModelCreating` is `modelBuilder.ApplyConfigurationsFromAssembly(typeof(<Context>).Assembly)` followed by `OnModelCreatingPartial`; the composite keys are no longer written there (they are in their configurations). Without the flag the context is as it was.

**Checked:** generated for the PostgreSQL InvoiceSystem sample with the flag, compiled in the generated API, and run: the EF model loaded with every configuration applied and the CSV export read every table through it.
