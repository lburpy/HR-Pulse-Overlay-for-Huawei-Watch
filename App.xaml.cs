using System.Windows;

namespace PulseOverlay;

public partial class App : Application
{
    Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second copy would fight over the Bluetooth link and the server port
        _singleInstance = new Mutex(true, @"Local\PulseOverlay.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            MessageBox.Show("Pulse Overlay zaten çalışıyor.", "Pulse Overlay",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show($"Beklenmeyen hata: {args.Exception.Message}", "Pulse Overlay",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
