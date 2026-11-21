
using System;
using System.Diagnostics;
using System.Windows;
using CoffeeLoyalty.Core.Data;

namespace CoffeeLoyalty.WPF;

/// <summary>
/// Application startup and initialization.
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // Initialize database before opening the UI.
            var connectionFactory = new SqliteConnectionFactory();
            var seedFactory = new SeedDataFactory();

            var initializer = new DatabaseInitializer(
                connectionFactory,
                seedFactory);

            initializer.Initialize();

            // Open the main application window.
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            Trace.TraceError(ex.ToString());

            MessageBox.Show(
                $"Database initialization failed:\n{ex.Message}",
                "CoffeeLoyalty - Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }
}
