-- A many-to-many junction table for the InvoiceSystem sample (MySQL): which items a customer favours.
-- The key is the pair of columns; deleting a customer removes their links, an item that is linked cannot be deleted.
-- Safe to run again.
CREATE TABLE IF NOT EXISTS customer_favorite_item (
    customer_id int NOT NULL,
    item_id int NOT NULL,
    CONSTRAINT PK_customer_favorite_item PRIMARY KEY (customer_id, item_id),
    CONSTRAINT FK_customer_favorite_item_customer FOREIGN KEY (customer_id) REFERENCES customer (customer_id) ON DELETE CASCADE,
    CONSTRAINT FK_customer_favorite_item_item FOREIGN KEY (item_id) REFERENCES item (item_id)
);
