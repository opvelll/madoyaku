using System.Windows;

namespace HonnyakuKun;

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
            MessageBox.Show(exception.Message, "翻訳くん", MessageBoxButton.OK, MessageBoxImage.Error);
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
