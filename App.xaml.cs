using System.Windows;

namespace Madoyaku;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            TestMode.Initialize(e.Args);
        }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "窓訳", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(2);
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        window.Activate();
    }
}
