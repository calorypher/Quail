namespace Quail.App;

internal static class StartupOptions
{
    public static AppLaunchOptions Current { get; set; } = AppLaunchOptions.Parse([]);
}
