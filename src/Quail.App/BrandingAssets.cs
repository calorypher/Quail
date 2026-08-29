using System.Drawing;

namespace Quail.App;

internal static class BrandingAssets
{
    private const string AssetsDirectory = "Assets";
    private const string ApplicationSmallIcon = "quail-app-icon-32px.png";
    private const string ApplicationLargeIcon = "quail-app-icon-48px.png";
    private const string TrayIcon = "quail-tray-icon-16px.png";

    public static nint CreateApplicationSmallIcon() => CreateIcon(ApplicationSmallIcon);

    public static nint CreateApplicationLargeIcon() => CreateIcon(ApplicationLargeIcon);

    public static nint CreateTrayIcon() => CreateIcon(TrayIcon);

    private static nint CreateIcon(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, AssetsDirectory, fileName);
        using var bitmap = new Bitmap(path);
        return bitmap.GetHicon();
    }
}
