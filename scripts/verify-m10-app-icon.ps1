[CmdletBinding()]
param(
    [string] $Executable = 'src\Quail.App\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Quail.exe'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$executablePath = Join-Path $repositoryRoot $Executable
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Missing executable: $executablePath"
}

if (-not ('M10IconResourceReader' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class M10IconResourceReader
{
    private const uint LoadLibraryAsDataFile = 0x00000002;
    private static readonly IntPtr RtIcon = new IntPtr(3);
    private static readonly IntPtr RtGroupIcon = new IntPtr(14);

    private delegate bool EnumResourceNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumResourceNameProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SizeofResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LockResource(IntPtr resource);

    public static byte[][] ReadFrames(string executablePath)
    {
        var module = LoadLibraryEx(executablePath, IntPtr.Zero, LoadLibraryAsDataFile);
        if (module == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            IntPtr groupName = IntPtr.Zero;
            EnumResourceNames(module, RtGroupIcon, (currentModule, type, name, lParam) =>
            {
                groupName = name;
                return false;
            }, IntPtr.Zero);

            if (groupName == IntPtr.Zero)
            {
                throw new InvalidOperationException("No RT_GROUP_ICON resource was found.");
            }

            var group = ReadResource(module, groupName, RtGroupIcon);
            if (group.Length < 6 || ReadUInt16(group, 0) != 0 || ReadUInt16(group, 2) != 1)
            {
                throw new InvalidOperationException("The RT_GROUP_ICON resource is invalid.");
            }

            var count = ReadUInt16(group, 4);
            if (group.Length != 6 + (count * 14))
            {
                throw new InvalidOperationException("The RT_GROUP_ICON resource has an invalid frame table.");
            }

            var frames = new byte[count][];
            for (var index = 0; index < count; index++)
            {
                var resourceId = ReadUInt16(group, 6 + (index * 14) + 12);
                frames[index] = ReadResource(module, new IntPtr(resourceId), RtIcon);
            }

            return frames;
        }
        finally
        {
            FreeLibrary(module);
        }
    }

    private static byte[] ReadResource(IntPtr module, IntPtr name, IntPtr type)
    {
        var resource = FindResource(module, name, type);
        if (resource == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        var length = checked((int)SizeofResource(module, resource));
        var data = LockResource(LoadResource(module, resource));
        if (data == IntPtr.Zero)
        {
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        var bytes = new byte[length];
        Marshal.Copy(data, bytes, 0, length);
        return bytes;
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) => (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
}
'@
}

$expectedFrameHashes = @(
    'E19CE347F912941CC8118098AF9458712C65483B7E1F39AC1006911162838242',
    '015DA35E33C734B93BFA15B47DF1D9BA93E7DF0369427410908489F9D25A6637',
    'A889C25C22D80EE8B5AAFBF6E6E3FFBD8D5FE8091B52F965B2AC9EFFD45F3C9B',
    '25B1D79A758A2AFF5ABEAF671F2A8FCF1C66E290BC7351B290BF0F699045F9E5',
    '214D80584D034D9B9647E0E670793C9EF42DDED42A3EA0EFE5E349485DFA32B8',
    'B08E1DC37E3B42D06C835E733A0FE5A2F6143B33F580FB5D34A53E2EDEE18A4D'
)
$actualFrameHashes = [M10IconResourceReader]::ReadFrames($executablePath) |
    ForEach-Object { [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($_)) }

if (@($actualFrameHashes).Count -ne $expectedFrameHashes.Count -or
    @(Compare-Object -ReferenceObject $expectedFrameHashes -DifferenceObject $actualFrameHashes).Count -ne 0) {
    throw "The embedded Quail icon frames do not match the approved PNG hashes. Actual: $($actualFrameHashes -join ', ')"
}

Write-Output "PASS executable=$executablePath frames=$($actualFrameHashes.Count) hashes=$($actualFrameHashes -join ',')"
