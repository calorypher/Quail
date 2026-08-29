using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Quail.M08.WinUi;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var diagnosticsPath = M08Diagnostics.PathFromArguments(args);
        M08Diagnostics.WriteMessage(diagnosticsPath, "Program.Main entered.");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        M08Diagnostics.WriteMessage(diagnosticsPath, "COM wrappers initialized.");
        Application.Start(_ =>
        {
            M08Diagnostics.WriteMessage(diagnosticsPath, "Application.Start callback entered.");
            var synchronizationContext = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            M08Diagnostics.WriteMessage(diagnosticsPath, "Dispatcher queue synchronization context installed.");
            new App();
            M08Diagnostics.WriteMessage(diagnosticsPath, "App constructed.");
        });
    }
}
