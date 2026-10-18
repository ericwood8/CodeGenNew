# CS_Validation_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: <TableName>Validation.cs   (see OutputName in CS_Validation_v1.tt.config)

A "buddy class" validation companion: [Required]/[StringLength]/[DataType]/[Display] attributes on a separate <Table>Metadata
class, attached to the real entity via [MetadataType(typeof(<Table>Metadata))] rather than written
directly onto the entity - so it works whether the entity came from CS_Entity.tt or is
hand-maintained (a project may keep some entities, such as name/active tables, hand-maintained), without editing that file at all.

Why a separate template instead of folding into CS_Entity.tt: CS_Entity.tt already writes
[StringLength]/[DataType]/[Display] itself (its own header comment lists what it does NOT write:
"[Range]/[RegularExpression]/[Required] validation" - a deliberate choice, not an oversight) -
[Required] is this template's actual new contribution for a CS_Entity-generated entity.  C#'s
`required` modifier (which CS_Entity does use) is a compile-time "must be set somewhere" obligation,
not the same thing as a RUNTIME [Required] DataAnnotations check (e.g. Validator.TryValidateObject,
or an ASP.NET Core MVC model-validation pipeline) - the two are complementary, not redundant.

Which columns get validated: every column except computed columns (not assignable), audit-classified
columns (IsAuditColumn - Create*/Modif*/Change*/Delete*/Update*/Activ*/Inactiv*, per
AuditColumnClassifier: server-set, not something a caller fills in a form and needs validated), and an
IDENTITY primary key (database-assigned). A non-identity primary key (a natural or caller-supplied
key) IS included - the caller must supply it, same reasoning SP_Insert.tt already uses to decide
whether the key is a parameter.

  - IsNullable=false -> [Required].
  - An integer column whose name says what it holds (Year, Month, a *Percent, a *Qty, ...) -> [Range(min, max)], the same
    limits the generated number boxes use (see NumericClassifier and ProjectSettings.RangeFor).
  - IsStringColumn with a known length -> [StringLength(n)] (same byte/character halving CS_Entity.tt
    uses for nchar/nvarchar).
  - A text column named like an email address, a phone number or a URL -> [EmailAddress], [Phone] or
    [Url] (they check the value and carry the DataType themselves). The classification is the one the
    validators of every language read (ColumnRules.RuleOf), so the project's PlainTextColumns list
    (EmailTemplate, or Site.PhoneNotes for one table) switches it off here too. A decimal column named
    Latitude or Longitude gets [Range(-90, 90)] / [Range(-180, 180)] unless a CHECK constraint says otherwise.
  - *Password* -> [DataType(DataType.Password)], else MultilineText from 100 characters up (same
    threshold CS_Entity.tt uses) - checked in that order, since a semantically-named column should win
    over a generic length guess.
  - [Display(Name/Description)] from the column name split into words (CS_Entity.tt's own helper).

Deliberately does NOT write EF-mapping attributes ([Column], [ForeignKey], [Precision],
[DatabaseGenerated], [Timestamp]) or display-formatting ones ([DisplayFormat]) - those belong to the
entity's own mapping (CS_Entity.tt already writes them) and would be redundant or conflicting coming
from a second class. Does not write [RegularExpression] (needs judgment CodeGenNew cannot
supply) or a custom foreign-key-exists validator attribute (the research example's
[RequiredIntForeignKeyValidator] assumes a hand-written attribute class no target project can be
assumed to have) - a NOT NULL foreign-key column still gets a plain [Required].

Integration this template does NOT do for you, so it stays a plain add-a-file generator like every
other template: the target entity class must be declared `partial` for [MetadataType] to attach to it
(a one-line edit, the same category of by-hand step TS_Component.tt's own route/sidebar/app.config
wiring already leaves to the developer) - CS_Entity.tt's own output is not `partial` today, so this
is required even for a CS_Entity-generated entity. Whatever in the target project actually calls
Validator.TryValidateObject (or wires ASP.NET Core's own model-validation pipeline) is also up to that
project, the same way this tool never wires a DbSet registration or an Angular route either.

Requires a primary key (Requires=PrimaryKey, the default) and a table. Unlike CS_Entity.tt, does
NOT require a single-column key - a validation-attribute carrier has no [Key]/Fluent-API concern, so
a composite-key table's columns can still be validated even though CS_Entity.tt itself would refuse
to generate an entity for it.
```

With `ApiAnnotations=true` the class carries no `[MetadataType]` partial stub: the API (`Annotations.cs`, written by `API_EssentialProgram`) finds `<Entity>Metadata` by name and attaches it with `TypeDescriptor.AddProvider`, so the entity need not be partial, and the create and update handlers run `Validator.TryValidateObject` after the audit dates are stamped (400 with the messages). A postal code or social security number gets `[RegularExpression]`.

The attribute list of a column is written by `ValidationAttributes.Of` (Core), shared with `CS_DtoInput` and `BLZ_Model`. A decimal with a minimum and no maximum (a column in `NonNegativeColumns`) gets `[Range(0, double.MaxValue)]`; before, the template stopped on it.

On an update `Annotations.Check(row, stored)` drops the problems of a property whose value equals the stored row's (`StoredAsync` reads it untracked), so a stored value that breaks an attribute does not stop an edit of another column. The generated forms (React, Angular, Blazor, WinUI3) still check every field, and the browser's own `type="email"` check stops the save before a request is sent.
