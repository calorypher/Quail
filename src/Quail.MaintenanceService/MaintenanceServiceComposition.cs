namespace Quail.MaintenanceService;

/// <summary>
/// Composition boundary for the privileged maintenance runtime. Filesystem,
/// configuration, control, and security concerns are intentionally not part of
/// the service-host plumbing.
/// </summary>
public static class MaintenanceServiceComposition
{
    public static MaintenanceService CreateService(
        Func<IMaintenanceServiceRuntime> runtimeFactory,
        TimeSpan? stopTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        var runtime = runtimeFactory()
            ?? throw new InvalidOperationException("The maintenance runtime factory returned null.");
        return new MaintenanceService(runtime, stopTimeout ?? TimeSpan.FromSeconds(30));
    }
}
