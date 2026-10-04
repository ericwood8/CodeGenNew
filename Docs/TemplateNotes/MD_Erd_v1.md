# MD_Erd_v1

One Markdown file (`ErDiagram.md`) with a Mermaid `erDiagram` of the whole database; GitHub, GitLab, Visual Studio Code and most documentation sites draw it.

- Each table lists its columns as `type name PK,FK,UK` (the length stays out of the type; a NULL-able column is marked `"null"`); each foreign key is one relationship line, labelled with the foreign-key columns. The parent is "exactly one" (`||`), or "zero or one" (`|o`) when every foreign-key column can be NULL; the child is "zero or many" (`o{`).
- A foreign key to a table outside the database is left out. `ErdTables=Customer,SalesInvoice` draws only those tables and the relationships among them (a few hundred tables make a diagram nobody can read); an unknown name is an error.
- **Checked:** the diagram of the PostgreSQL sample (182 lines) was parsed once by hand with Mermaid 11's own parser (a deliberately broken line failed it). The suite does not run that parser; it checks every line of the diagram against the grammar the template writes (entity, attribute, relationship), which catches the same slips without Node.
