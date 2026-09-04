using Microsoft.Win32.SafeHandles;

namespace WlanLivePathTester.Windows.Interop;

internal sealed class SafeWinHttpHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private SafeWinHttpHandle()
        : base(ownsHandle: true)
    {
    }

    private SafeWinHttpHandle(nint rawHandle)
        : base(ownsHandle: true)
    {
        SetHandle(rawHandle);
    }

    internal static SafeWinHttpHandle FromRaw(nint rawHandle) => new(rawHandle);

    protected override bool ReleaseHandle() =>
        WinHttpNative.WinHttpCloseHandle(handle);
}
