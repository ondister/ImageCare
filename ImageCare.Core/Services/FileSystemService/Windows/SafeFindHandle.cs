using System.Runtime.InteropServices;

using Microsoft.Win32.SafeHandles;

namespace ImageCare.Core.Services.FileSystemService.Windows;

internal sealed class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeFindHandle()
        : base(true) { }

    protected override bool ReleaseHandle()
    {
        return FindClose(handle);
    }

    [DllImport("kernel32.dll")]
    private static extern bool FindClose(nint handle);
}