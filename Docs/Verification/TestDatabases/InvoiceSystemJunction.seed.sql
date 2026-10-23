-- Links for the EmployeeCustomer junction table of the InvoiceSystem sample (SQL Server): employees 2 to 6 each work with the customers 2 to 50
-- whose number leaves the employee's number when divided by 5. The first and last employee and customer are left unlinked, because the generated
-- junction test takes a row from the start of each list and unlinks that pair. Safe to run again.
INSERT INTO dbo.EmployeeCustomer (EmployeeId, CustomerId)
SELECT e.EmployeeId, c.CustomerId
FROM dbo.Employee e
JOIN dbo.Customer c ON c.CustomerId % 5 = e.EmployeeId % 5
WHERE e.EmployeeId BETWEEN 2 AND 6
  AND c.CustomerId BETWEEN 2 AND 50
  AND NOT EXISTS (SELECT 1 FROM dbo.EmployeeCustomer x WHERE x.EmployeeId = e.EmployeeId AND x.CustomerId = c.CustomerId);
