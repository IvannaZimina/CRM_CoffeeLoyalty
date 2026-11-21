CREATE TABLE IF NOT EXISTS EMPLOYEES (
    employeeID       INTEGER PRIMARY KEY AUTOINCREMENT,
    employeeName     TEXT    NOT NULL UNIQUE,
    employeeRole     TEXT    NOT NULL CHECK (employeeRole IN ('Cashier', 'Manager')),
    employeePassword TEXT    NOT NULL,
    isActive         INTEGER NOT NULL DEFAULT 1 CHECK (isActive IN (0, 1))
);