CREATE TABLE IF NOT EXISTS LOGS (
    logID       INTEGER PRIMARY KEY AUTOINCREMENT,
    objectID    INTEGER,
    objectType  TEXT NOT NULL CHECK (length(trim(objectType)) > 0),
    actionType  TEXT NOT NULL CHECK (length(trim(actionType)) > 0),
    description TEXT NOT NULL CHECK (length(trim(description)) > 0),
    timeStamp   TEXT NOT NULL CHECK (length(trim(timeStamp)) > 0)
);