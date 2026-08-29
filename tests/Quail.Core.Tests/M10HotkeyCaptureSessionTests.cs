using Quail.App;

namespace Quail.Core.Tests;

public sealed class M10HotkeyCaptureSessionTests
{
    [Fact]
    public void Begin_ActivatesTheSessionOnlyOnce()
    {
        var session = new HotkeyCaptureSession();

        Assert.True(session.Begin());
        Assert.True(session.IsActive);
        Assert.False(session.Begin());
    }

    [Fact]
    public void CompleteCancel_EndsAnActiveSessionOnlyAfterPreviousHotkeyIsRestored()
    {
        var session = new HotkeyCaptureSession();
        session.Begin();

        Assert.False(session.CompleteCancel(false));
        Assert.True(session.IsActive);
        Assert.True(session.CompleteCancel(true));
        Assert.False(session.IsActive);
        Assert.False(session.CompleteCancel(true));
    }

    [Fact]
    public void CompleteSave_EndsAnActiveSessionWithoutCancelRestore()
    {
        var session = new HotkeyCaptureSession();
        session.Begin();

        Assert.True(session.CompleteSave());
        Assert.False(session.IsActive);
        Assert.False(session.CompleteCancel(true));
    }
}
