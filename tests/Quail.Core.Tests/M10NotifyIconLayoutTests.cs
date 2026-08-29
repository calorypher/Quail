using System.Reflection;
using System.Runtime.InteropServices;
using Quail.App;

namespace Quail.Core.Tests;

public sealed class M10NotifyIconLayoutTests
{
    [Fact]
    public void NotifyIconData_UsesTheExpectedUnicodeLayout()
    {
        var expectedSize = IntPtr.Size == 8 ? 976 : 956;
        var expectedVersionOffset = IntPtr.Size == 8 ? 816 : 800;

        Assert.Equal(expectedSize, NativeMethods.NotifyIconDataSize);
        Assert.Equal(expectedVersionOffset, NativeMethods.NotifyIconDataVersionOffset);
    }

    [Fact]
    public void ShellNotifyIcon_UsesTheExplicitUnicodeEntryPoint()
    {
        var method = typeof(NativeMethods).GetMethod("Shell_NotifyIconW", BindingFlags.Static | BindingFlags.NonPublic);
        var import = method!.GetCustomAttribute<DllImportAttribute>();

        Assert.NotNull(import);
        Assert.Equal("Shell_NotifyIconW", import!.EntryPoint);
        Assert.Equal(CharSet.Unicode, import.CharSet);
        Assert.True(import.ExactSpelling);
    }
}
