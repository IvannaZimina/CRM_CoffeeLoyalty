CREATE TABLE IF NOT EXISTS PURCHASES (
    purchaseID      INTEGER PRIMARY KEY AUTOINCREMENT,
    cardID          INTEGER REFERENCES CARDS(cardID),
    employeeID      INTEGER NOT NULL REFERENCES EMPLOYEES(employeeID),
    totalAmount     REAL NOT NULL CHECK (totalAmount >= 0),
    bonusesSpent    INTEGER NOT NULL DEFAULT 0 CHECK (bonusesSpent >= 0),
    bonusesEarned   INTEGER NOT NULL DEFAULT 0 CHECK (bonusesEarned >= 0),
    timeStamp       TEXT NOT NULL
);