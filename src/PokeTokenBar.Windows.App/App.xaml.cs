using System.Windows;

namespace PokeTokenBar.Windows.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var viewModel = AppComposition.CreateUsageViewModel();
        MainWindow = new MainWindow(viewModel);
        MainWindow.Show();
    }
}
