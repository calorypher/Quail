using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Avalonia.Threading;
using Quail.M08.Avalonia.Interop;

namespace Quail.M08.Avalonia;

public sealed class App : Application
{
    private PipeEventWriter? _events;
    private HotKeyService? _hotKey;
    private ShellIconService? _icons;
    private TrayIcon? _trayIcon;
    private QuickSearchWindow? _window;
    private M08Options? _options;
    private int _visibleReadyCount;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            throw new InvalidOperationException("M08 requires the classic desktop lifetime.");
        }

        var options = M08Options.Parse(desktop.Args ?? []);
        _options = options;
        RequestedThemeVariant = options.Theme switch
        {
            "light" => ThemeVariant.Light,
            "dark" => ThemeVariant.Dark,
            _ => ThemeVariant.Default
        };

        _events = new PipeEventWriter(options.PipeName);
        _icons = new ShellIconService();
        _window = new QuickSearchWindow(MockResult.Load(), _events, _icons, OnVisibleReady, options.TestExitAfterVisibleReadyCount != 1);
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        ConfigureTray();
        _hotKey = new HotKeyService(ToggleOverlay);
        _hotKey.Start();

        base.OnFrameworkInitializationCompleted();

        // A Window.Show call made before the base lifetime callback can block
        // before the classic desktop dispatcher is running. Queue the test-only
        // startup summon after that callback so it follows the same UI path as
        // the registered hotkey.
        if (options.ShowOnStart)
        {
            desktop.Startup += (_, _) => _window.ShowOverlay();
        }
        else
        {
            desktop.Startup += (_, _) => _events?.StartupHidden();
        }
    }

    private void ToggleOverlay()
    {
        if (_window?.IsVisible == true)
        {
            _window.HideOverlay();
        }
        else
        {
            _window?.ShowOverlay();
        }
    }

    private void ConfigureTray()
    {
        var show = new NativeMenuItem("Show");
        show.Click += (_, _) => _window?.ShowOverlay();
        var exit = new NativeMenuItem("Exit");
        exit.Click += (_, _) => Exit();

        _trayIcon = new TrayIcon
        {
            Icon = _icons!.GetTrayIcon(),
            ToolTipText = "Quail M08 Avalonia spike",
            Menu = new NativeMenu { Items = { show, exit } }
        };
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void OnVisibleReady()
    {
        _visibleReadyCount++;
        M08Diagnostics.WriteMessage(_options?.DiagnosticsPath, $"visible-ready count: {_visibleReadyCount}.");
        if (_options?.TestExitAfterVisibleReadyCount == _visibleReadyCount)
        {
            M08Diagnostics.WriteMessage(_options.DiagnosticsPath, "Test Exit queued.");
            DispatcherTimer.RunOnce(Exit, TimeSpan.FromMilliseconds(100), DispatcherPriority.Background);
        }
    }

    private void Exit()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Exit, DispatcherPriority.Normal);
            return;
        }

        var desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        M08Diagnostics.WriteMessage(_options?.DiagnosticsPath, $"Exit entered on UI dispatcher={Dispatcher.UIThread.CheckAccess()}, thread={Environment.CurrentManagedThreadId}, mainWindowPresent={desktop?.MainWindow is not null}, visible={_window?.IsVisible}.");
        _window?.PrepareForExit();
        _window?.Close();
        M08Diagnostics.WriteMessage(_options?.DiagnosticsPath, "Main window closed.");
        desktop!.MainWindow = null;
        _hotKey?.Dispose();
        M08Diagnostics.WriteMessage(_options?.DiagnosticsPath, "Hotkey disposed.");
        _trayIcon?.Dispose();
        M08Diagnostics.WriteMessage(_options?.DiagnosticsPath, "Tray disposed.");
        _events?.Dispose();
        M08Diagnostics.WriteMessage(_options?.DiagnosticsPath, "Pipe disposed.");
        _icons?.Dispose();
        M08Diagnostics.WriteMessage(_options?.DiagnosticsPath, "Icons disposed.");
        desktop.Shutdown();
        M08Diagnostics.WriteMessage(_options?.DiagnosticsPath, "Lifetime shutdown returned.");
    }
}
