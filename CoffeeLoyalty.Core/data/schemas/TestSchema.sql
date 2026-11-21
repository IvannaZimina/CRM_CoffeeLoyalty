
-- Temporary test table for checking database initialization.
-- IF NOT EXISTS prevents errors when the application starts again.

CREATE TABLE IF NOT EXISTS TestInitialization (
    Id INTEGER PRIMARY KEY,
    Name TEXT NOT NULL
);
