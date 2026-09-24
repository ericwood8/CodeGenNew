using CodeGenNew.App.Services;
using Microsoft.UI.Xaml;

namespace CodeGenNew.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        FontAssetSeeder.EnsureFontsSeeded();
        _window = new MainWindow();
        _window.Activate();
    }
}
