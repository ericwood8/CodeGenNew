# TS_Model_v1

The full design notes that used to head the template. The template keeps a short summary.

```text
Generates: models/<table>.ts   (an Angular TypeScript interface; the path is written by the template itself, see
GeneratedFiles - point the output folder at the Angular app's src/app folder)

The shape of one row as the API sends and receives it:

    export interface Holiday {
        holidayId?: number; // Optional for new ones
        sY_IsoCountry_Alpha3Code: string;
        date: string;
        name: string;
    }

  - File / interface name: the table name without its "E_" or "SY_" prefix (E_TimeSheet -> models/timesheet.ts,
    interface TimeSheet), the same rule BaseApi uses for the API's route.
  - Property names: exactly what ASP.NET Core's JSON camel-casing produces from the column name (System.Text.Json
    lower-cases the leading capitals up to the last one that starts a word), so the interface matches the JSON the
    server really sends: HolidayId -> holidayId, IsActive -> isActive, but SY_IsoCountry_Alpha3Code ->
    sY_IsoCountry_Alpha3Code and E_TimeSheetId -> e_TimeSheetId.
  - Types: number (all numeric and money columns), boolean (bit), string (text, uniqueidentifier and DATES). A date is
    a string because JSON has no date type - the server sends "2025-12-25T00:00:00" and takes the same back - so
    typing it Date would lie about what a row holds.
  - Optional ("?"): the primary key (a new row has none yet) and every column that allows NULL. NOT NULL columns are
    required, matching the C# entity's "required" properties, which the API refuses to bind without.
  - One optional navigation property per single-column foreign key (employee?: Employee), named for the FK's role, of
    the parent's interface, imported from its model file - unless the parent is an enum / lookup table listed in
    noNavigationTables, in which case the foreign key stays a plain number/string.

Not written: DisplayName / validation attributes, or extra fields a screen adds to a row (TimeSheetDetail's
projectName); an interface that needs those stays hand-maintained.
```
