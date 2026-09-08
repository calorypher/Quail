namespace Quail.App;

internal static class HotkeyStartupLifecycle
{
    public const string UnavailableMessage = "The configured Quick Search hotkey is unavailable. Choose another hotkey in Settings.";

    public static bool ShouldContinueAfterInitialRegistrationFailure => true;
}
