-- CustomerAddress for the standing PostgreSQL InvoiceSystem database (it has the Customer table): a type and one default per customer and type, for
-- DefaultRowTables=CustomerAddress:IsDefault:CustomerId+AddressType (generate with --only CustomerAddress,Customer; play it with default_check.py --existing).
-- PGPASSWORD=... psql -h localhost -U <login> -d InvoiceSystem -f default_rows_postgres.sql      Can be run again.
CREATE TABLE IF NOT EXISTS "CustomerAddress" (
    "CustomerAddressId" integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    "CustomerId" integer NOT NULL REFERENCES "Customer" ("CustomerId"),
    "AddressType" varchar(10) NOT NULL CHECK ("AddressType" IN ('billing', 'shipping')),
    "Line1" varchar(200) NOT NULL,
    "Line2" varchar(200) NULL,
    "City" varchar(100) NOT NULL,
    "Region" varchar(50) NULL,
    "PostalCode" varchar(25) NULL,
    "Country" varchar(100) NOT NULL,
    "IsDefault" boolean NOT NULL DEFAULT false
);
CREATE UNIQUE INDEX IF NOT EXISTS "UX_CustomerAddress_Default" ON "CustomerAddress" ("CustomerId", "AddressType") WHERE "IsDefault";
