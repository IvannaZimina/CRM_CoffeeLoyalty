using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using CoffeeLoyalty.Core.Data;

namespace CoffeeLoyalty.WPF;

// Starts the WPF application and initializes the SQLite database before showing the main window.
public partial class App : Application
{
    // Prepares the database on startup and shows a clear error dialog if initialization fails.
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Create shared database infrastructure for startup initialization.
            var connectionFactory = new SqliteConnectionFactory();
            var seedDataFactory = new SeedDataFactory();

            var initializer = new DatabaseInitializer(
                connectionFactory,
                seedDataFactory);

            // Create or open the SQLite database, run registered schemas, and seed initial data.
            initializer.Initialize();

            // Verify repeated initialization idempotency and seeded DRINKS rows during development.
            VerifyDrinksInitialization(connectionFactory, initializer);

            // Open the main window only after database setup completes successfully.
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            // Log full diagnostic details for debugging.
            Trace.TraceError(ex.ToString());

            // Inform the user and exit cleanly when database startup fails.
            MessageBox.Show(
                $"Database initialization failed:\n{ex.Message}",
                "CoffeeLoyalty - Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    // Checks that DRINKS exists, seeded menu rows are present, and repeated Initialize calls do not duplicate them.
    private static void VerifyDrinksInitialization(
        SqliteConnectionFactory connectionFactory,
        DatabaseInitializer initializer)
    {
        using var connection = connectionFactory.OpenConnection();
        using var command = connection.CreateCommand();

        // Confirm DRINKS exists in SQLite metadata.
        command.CommandText =
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DRINKS';";
        long tableCount = Convert.ToInt64(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);

        // Count seeded rows before running Initialize a second time.
        command.CommandText = "SELECT COUNT(*) FROM DRINKS;";
        long countBefore = Convert.ToInt64(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);

        // Run initialization again to verify idempotency.
        initializer.Initialize();

        long countAfter = Convert.ToInt64(
            command.ExecuteScalar(),
            CultureInfo.InvariantCulture);

        if (tableCount != 1 || countBefore <= 0 || countBefore != countAfter)
        {
            throw new InvalidOperationException(
                $"DRINKS check failed: Table={tableCount}, " +
                $"CountBefore={countBefore}, CountAfter={countAfter}");
        }

        Debug.WriteLine(
            $"DRINKS check PASSED: table exists, rows={countAfter}, repeated Initialize idempotent.");
    }
}