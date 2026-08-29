namespace Quail.App;

internal static class ResultSelection
{
    public static bool TryGetMoveTarget(int count, int selectedIndex, int delta, out int target)
    {
        if (count == 0)
        {
            target = -1;
            return false;
        }

        var current = selectedIndex < 0 ? 0 : selectedIndex;
        target = Math.Clamp(current + delta, 0, count - 1);
        return true;
    }

    public static bool TryGetBoundaryTarget(int count, bool last, out int target)
    {
        if (count == 0)
        {
            target = -1;
            return false;
        }

        target = last ? count - 1 : 0;
        return true;
    }
}
