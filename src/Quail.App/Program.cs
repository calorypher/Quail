using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Quail.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (AdminIndexWorker.TryParse(args, out var request, out var workerError))
        {
            if (workerError is not null)
            {
                Environment.ExitCode = 2;
                return;
            }
            Environment.ExitCode = AdminIndexWorker.Run(request!);
            return;
        }
        var options = AppLaunchOptions.Parse(args);
        StartupOptions.Current = options;
        AppLog.Configure(options.DiagnosticsPath);
        AppLog.Write("Program startup.");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(_ =>
        {
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread()));
            new App();
        });
    }
}
