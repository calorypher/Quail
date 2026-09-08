using System.ServiceProcess;
using Quail.FileSystem;

namespace Quail.MaintenanceService;

internal static class Program
{
    private static void Main()
    {
        ServiceBase.Run(MaintenanceServiceComposition.CreateService(
            static () => new FileSystemMaintenanceServiceRuntime()));
    }
}
