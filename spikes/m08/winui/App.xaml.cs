using Microsoft.UI.Xaml;

namespace Quail.M08.WinUi;

public sealed partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var options = M08Options.Parse(Environment.GetCommandLineArgs().Skip(1));
        M08Diagnostics.WriteMessage(options.DiagnosticsPath, "OnLaunched entered.");
        try
        {
            _window = new MainWindow(options);
            M08Diagnostics.WriteMessage(options.DiagnosticsPath, "MainWindow constructed.");
            await _window.InitializeAsync();
            M08Diagnostics.WriteMessage(options.DiagnosticsPath, "MainWindow initialized.");
        }
        catch (Exception exception)
        {
            M08Diagnostics.Write(options.DiagnosticsPath, exception);
            Exit();
        }
    }
}
