using System.ServiceProcess;

namespace Quail.MaintenanceService;

internal static class Program
{
    private static void Main()
    {
        // The lead supplies the real filesystem runtime at integration time.
        // Keeping this explicit prevents the host from inventing privileged
        // behavior before the runtime composition is complete.
        ServiceBase.Run(MaintenanceServiceComposition.CreateService(
            static () => throw new InvalidOperationException(
                "No maintenance runtime has been composed for this service build.")));
    }
}
