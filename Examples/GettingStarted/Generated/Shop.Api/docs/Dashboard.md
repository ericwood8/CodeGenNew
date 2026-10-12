# Dashboard of shop.db

Statements are written for SQLite. Each one is fixed text with no parameter and returns a label and a number (the recent list returns a label and a date).

## On the dashboard

### Customers and Sales Orders

ChildCount widget on Customer. Customer is a master with Sales Order rows under it, so both counts sit on one card.

```sql
SELECT 'Customers' AS "Label", CAST(COUNT(*) AS REAL) AS "Value" FROM "customer" UNION ALL SELECT 'Sales Orders' AS "Label", CAST(COUNT(*) AS REAL) AS "Value" FROM "sales_order"
```

### Customers by Status

Breakdown widget on Customer. Status accepts only 3 values, so the rows can be counted per value.

```sql
SELECT COALESCE("status", '(none)') AS "Label", CAST(COUNT(*) AS REAL) AS "Value" FROM "customer" GROUP BY "status" ORDER BY COUNT(*) DESC
```

### Sales Orders by Product

Breakdown widget on SalesOrder. Product is a lookup of 2 rows, so the rows can be counted per Product.

```sql
SELECT COALESCE(p."name", '(none)') AS "Label", CAST(COUNT(*) AS REAL) AS "Value" FROM "sales_order" t INNER JOIN "product" p ON p."product_id" = t."product_id" GROUP BY p."name" ORDER BY COUNT(*) DESC
```

### Sales Orders per Month

Trend widget on SalesOrder. OrderDate dates each Sales Order row, so the rows can be counted per month.

```sql
SELECT date(b) AS "Label", CAST(COUNT(*) AS REAL) AS "Value" FROM (SELECT strftime('%Y-%m-01', "order_date") AS b FROM "sales_order" WHERE "order_date" >= datetime(strftime('%Y-%m-01', (SELECT MAX("order_date") FROM "sales_order")), '-11 months')) x GROUP BY b ORDER BY b
```

### Latest Customers

Recent widget on Customer. The newest rows by CustomerId, each shown by Name.

```sql
SELECT CAST("name" AS TEXT) AS "Label", CAST("customer_id" AS TEXT) AS "Detail" FROM "customer" ORDER BY "customer_id" DESC LIMIT 10 OFFSET 0
```

## Left out by the caps

At most 4 cards, 2 top lists, 2 breakdowns, 2 trends and 1 recent list are shown. These candidates scored lower:

| Widget | Kind | Table | Why |
|:-|:-|:-|:-|
| Customers | Count | Customer | Every screen's table gets its row count. |
| Sales Orders | Count | SalesOrder | Every screen's table gets its row count. |

