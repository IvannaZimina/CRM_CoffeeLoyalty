CREATE TABLE IF NOT EXISTS DRINKS (
    drinkID INTEGER PRIMARY KEY AUTOINCREMENT,
    drinkName TEXT NOT NULL,
    drinkPrice REAL NOT NULL CHECK (drinkPrice > 0),
    isActive INTEGER NOT NULL DEFAULT 1 CHECK (isActive IN (0, 1)),
    drinkType TEXT NOT NULL,
    drinkImageLink TEXT
);