using Microsoft.Win32.SafeHandles;

namespace WlanLivePathTester.Windows.Interop;

internal sealed class SafeWinHttpHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    private int _closeInitiated;

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

    internal void CancelPendingOperation()
    {
        if (Interlocked.Exchange(ref _closeInitiated, 1) != 0)
        {
            return;
        }

        nint rawHandle = handle;
        if (rawHandle == nint.Zero || rawHandle == new nint(-1))
        {
            SetHandleAsInvalid();
            return;
        }

        SetHandleAsInvalid();
        _ = WinHttpNative.WinHttpCloseHandle(rawHandle);
    }

    protected override bool ReleaseHandle()
    {
        if (Interlocked.Exchange(ref _closeInitiated, 1) != 0)
        {
            return true;
        }

        return WinHttpNative.WinHttpCloseHandle(handle);
    }
}
