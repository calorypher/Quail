namespace Quail.App;

internal sealed class HotkeyRegistration
{
    private readonly Func<HotkeyDefinition, bool> _register;
    private readonly Func<bool> _unregister;

    internal HotkeyRegistration(Func<HotkeyDefinition, bool> register, Func<bool> unregister)
    {
        _register = register;
        _unregister = unregister;
    }

    internal HotkeyDefinition Registered { get; private set; }
    internal bool IsRegistered { get; private set; }

    internal bool TryRegister(string value, out string error)
    {
        error = string.Empty;
        if (!HotkeyDefinition.TryParse(value, out var requested))
        {
            error = "Hotkey must use Ctrl, Alt, Shift, or Win plus one letter, digit, or Space.";
            return false;
        }

        if (IsRegistered)
        {
            _ = _unregister();
            IsRegistered = false;
        }

        if (_register(requested))
        {
            Registered = requested;
            IsRegistered = true;
            return true;
        }

        var restored = Registered.VirtualKey != 0 && _register(Registered);
        IsRegistered = restored;
        error = restored
            ? "That hotkey is unavailable. The previous Quail hotkey remains active."
            : "That hotkey is unavailable, and Quail could not restore the previous hotkey.";
        return false;
    }

    internal bool Suspend()
    {
        if (!IsRegistered || Registered.VirtualKey == 0)
        {
            return true;
        }

        if (!_unregister())
        {
            return false;
        }

        IsRegistered = false;
        return true;
    }

    internal bool Restore(HotkeyDefinition previous)
    {
        if (IsRegistered || previous.VirtualKey == 0)
        {
            return IsRegistered;
        }

        if (!_register(previous))
        {
            return false;
        }

        Registered = previous;
        IsRegistered = true;
        return true;
    }
}
