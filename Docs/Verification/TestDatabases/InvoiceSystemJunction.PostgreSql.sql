-- A many-to-many junction table for the InvoiceSystem sample (PostgreSQL): which items a customer favours.
-- The key is the pair of columns; deleting a customer removes their links, an item that is linked cannot be deleted.
-- Safe to run again.
CREATE TABLE IF NOT EXISTS "CustomerFavoriteItem" (
    "CustomerId" integer NOT NULL,
    "ItemId" integer NOT NULL,
    CONSTRAINT "PK_CustomerFavoriteItem" PRIMARY KEY ("CustomerId", "ItemId"),
    CONSTRAINT "FK_CustomerFavoriteItem_Customer" FOREIGN KEY ("CustomerId") REFERENCES "Customer" ("CustomerId") ON DELETE CASCADE,
    CONSTRAINT "FK_CustomerFavoriteItem_Item" FOREIGN KEY ("ItemId") REFERENCES "Item" ("ItemId")
);
