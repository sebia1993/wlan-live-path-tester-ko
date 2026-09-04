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

    internal void CancelPendingOperation()
    {
        // This project currently opens WinHTTP in synchronous mode. Microsoft
        // requires a synchronous request handle to remain open while another
        // thread is blocked inside a WinHTTP function that uses it. Cancellation
        // is therefore cooperative between calls until the transport is moved
        // to WINHTTP_FLAG_ASYNC and completion callbacks.
    }

    protected override bool ReleaseHandle() =>
        WinHttpNative.WinHttpCloseHandle(handle);
}
