
-- Stores customer loyalty tiers: Basic, Silver, and Gold.
CREATE TABLE IF NOT EXISTS CATEGORIES (
    categoryID INTEGER PRIMARY KEY AUTOINCREMENT,

    -- Category names must be unique and cannot be blank.
    categoryName TEXT NOT NULL UNIQUE
        CHECK (length(trim(categoryName)) > 0),

    -- Bonus rate and upgrade threshold will be configured later.
    bonusAccrualRate REAL NOT NULL DEFAULT 0
        CHECK (bonusAccrualRate >= 0),

    upgradeThreshold INTEGER NOT NULL DEFAULT 0
        CHECK (upgradeThreshold >= 0)
);
