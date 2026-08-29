namespace Quail.App;

internal sealed class HotkeyCaptureSession
{
    internal bool IsActive { get; private set; }

    internal bool Begin()
    {
        if (IsActive)
        {
            return false;
        }

        IsActive = true;
        return true;
    }

    internal bool CompleteSave()
    {
        if (!IsActive)
        {
            return false;
        }

        IsActive = false;
        return true;
    }

    internal bool CompleteCancel(bool previousHotkeyRestored)
    {
        if (!IsActive || !previousHotkeyRestored)
        {
            return false;
        }

        IsActive = false;
        return true;
    }
}
