# CS_Faker_v1

A Bogus fake-data generator for the table's entity: `<Table>Faker.Create(seed, maxForeignKey)` returns a `Faker<Entity>` and `<Table>Faker.Generate(count, seed, maxForeignKey)` the rows. For demos, tests and load; the same seed gives the same rows.

- **Text** is of a kind that fits its column name (an email, a phone number, a URL, first / last name, address lines, city, state, zip, country, a company name for a customer or vendor table, a product name for an item table, a sentence for a note, an upper-case code) and is cut to the column's length (`ClampLength`).
- **Numbers:** a whole number inside the range its name or its CHECK gives (a year, a month, a percentage), else 1 to 1000 inside the SQL type; a decimal inside its CHECK range and its precision (a strict bound steps just above it), 0 to 10000 otherwise, rounded to its scale. **Choices:** a random value of the CHECK list. **Dates:** the past five years. A column that can be NULL is NULL about one time in five.
- **Left out:** identity, computed, audit, create and modify columns (the database or the repository sets them). A non-identity key is numbered from 1; a foreign key is a random id from 1 to `maxForeignKey`, so pass ids that exist.
- Needs the Bogus package (the Program essentials add the reference when `ApiFakers=true`) and the entity class, so a primary key. Namespace: `FakerNamespace` (default `<ProjectName>.App.Fakers`). `ApiFakers=true` adds the template to a plan; the files go under `Fakers`.
- **Checked** on a PostgreSQL database built from the getting-started schema: 25 rows of each of three tables all passed their `CS_Validator` validators, ten were accepted by the running API, and a second run with the same seed gave the same rows.
