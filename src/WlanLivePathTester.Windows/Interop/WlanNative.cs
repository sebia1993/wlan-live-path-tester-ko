using System.Runtime.InteropServices;

namespace WlanLivePathTester.Windows.Interop;

internal static partial class WlanNative
{
    [LibraryImport("wlanapi.dll")]
    internal static partial uint WlanOpenHandle(
        uint clientVersion,
        nint reserved,
        out uint negotiatedVersion,
        out nint clientHandle);

    [LibraryImport("wlanapi.dll")]
    internal static partial uint WlanCloseHandle(
        nint clientHandle,
        nint reserved);

    [LibraryImport("wlanapi.dll")]
    internal static partial uint WlanEnumInterfaces(
        nint clientHandle,
        nint reserved,
        out nint interfaceList);

    [LibraryImport("wlanapi.dll")]
    internal static partial uint WlanQueryInterface(
        nint clientHandle,
        in Guid interfaceGuid,
        WlanIntfOpcode opcode,
        nint reserved,
        out uint dataSize,
        out nint data,
        out WlanOpcodeValueType opcodeValueType);

    [LibraryImport("wlanapi.dll")]
    internal static partial uint WlanGetNetworkBssList(
        nint clientHandle,
        in Guid interfaceGuid,
        nint ssid,
        Dot11BssType bssType,
        [MarshalAs(UnmanagedType.Bool)] bool securityEnabled,
        nint reserved,
        out nint bssList);

    [LibraryImport("wlanapi.dll")]
    internal static partial void WlanFreeMemory(nint memory);
}
