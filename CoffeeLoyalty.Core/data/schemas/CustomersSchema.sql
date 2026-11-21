CREATE TABLE IF NOT EXISTS CUSTOMERS (
    customerID      INTEGER PRIMARY KEY AUTOINCREMENT,
    categoryID      INTEGER NOT NULL REFERENCES CATEGORIES(categoryID),
    customerName    TEXT NOT NULL,
    bonusBalance    INTEGER NOT NULL DEFAULT 0 CHECK (bonusBalance >= 0),
    birthDate       TEXT,
    isActive        INTEGER NOT NULL DEFAULT 1 CHECK (isActive IN (0, 1)),
    timeStamp       TEXT NOT NULL
);