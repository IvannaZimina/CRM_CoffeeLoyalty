
using System;
using System.Diagnostics;
using System.Windows;
using CoffeeLoyalty.Core.Data;

namespace CoffeeLoyalty.WPF;

/// <summary>
/// Starts the WPF application and prepares the database first.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Runs when the application starts, before opening the main window.
    /// Shows an error and closes the app if the database cannot be prepared.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Use the same connection settings for the whole app.
            var connectionFactory = new SqliteConnectionFactory();
            var seedDataFactory = new SeedDataFactory();

            var initializer = new DatabaseInitializer(
                connectionFactory,
                seedDataFactory);

            // Create or open the database and run registered SQL scripts.
            initializer.Initialize();

            // TEMPORARY TEST: check SQLite and repeated initialization.
            RunSqliteSmokeTest(connectionFactory, initializer);

            // Open the WPF window only when database startup succeeded.
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            // Store technical error details in the Visual Studio output.
            Trace.TraceError(ex.ToString());

            // Show an error instead of crashing silently.
            MessageBox.Show(
                $"Database initialization failed:\n{ex.Message}",
                "CoffeeLoyalty - Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    /// <summary>
    /// Temporary test for SQLite database initialization.
    /// Checks foreign keys, table creation, and saved data.
    /// Remove this method after testing.
    /// </summary>
    private static void RunSqliteSmokeTest(
        SqliteConnectionFactory connectionFactory,
        DatabaseInitializer initializer)
    {
        // Insert one test row into our temporary table.
        // INSERT OR IGNORE prevents duplicate rows on repeated runs.
        using (var connection = connectionFactory.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                "INSERT OR IGNORE INTO TestInitialization (Id, Name) " +
                "VALUES (1, 'Keep this row');";

            command.ExecuteNonQuery();
        }

        // Run initialization again.
        // CREATE TABLE IF NOT EXISTS should prevent errors.
        initializer.Initialize();

        // Open a new connection for checking the results.
        using var testConnection = connectionFactory.OpenConnection();
        using var testCommand = testConnection.CreateCommand();

        // Foreign keys must be enabled. SQLite should return 1.
        testCommand.CommandText = "PRAGMA foreign_keys;";

        int foreignKeys =
            Convert.ToInt32(testCommand.ExecuteScalar());

        // Check if the SQL file created our test table.
        testCommand.CommandText =
            "SELECT COUNT(*) FROM sqlite_master " +
            "WHERE type='table' AND name='TestInitialization';";

        long tableCount =
            Convert.ToInt64(testCommand.ExecuteScalar());

        // Check that repeated initialization did not erase the row.
        testCommand.CommandText =
            "SELECT COUNT(*) FROM TestInitialization WHERE Id = 1;";

        long rowCount =
            Convert.ToInt64(testCommand.ExecuteScalar());

        // Stop the test if any of the checks failed.
        if (foreignKeys != 1 || tableCount != 1 || rowCount != 1)
        {
            throw new InvalidOperationException(
                $"SQLite test failed: ForeignKeys={foreignKeys}, " +
                $"Table={tableCount}, SavedRow={rowCount}");
        }

        // Show the result in Visual Studio's Debug Output window.
        Debug.WriteLine(
            "SQLite smoke test PASSED: foreign keys ON, " +
            "test table created, data preserved, repeated Initialize OK.");
    }
}
