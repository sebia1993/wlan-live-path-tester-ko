using System.Runtime.InteropServices;

namespace WlanLivePathTester.Windows.Interop;

internal enum WlanInterfaceState : uint
{
    NotReady = 0,
    Connected = 1,
    AdHocNetworkFormed = 2,
    Disconnecting = 3,
    Disconnected = 4,
    Associating = 5,
    Discovering = 6,
    Authenticating = 7
}

internal enum WlanConnectionMode : uint
{
    Profile = 0,
    TemporaryProfile = 1,
    DiscoverySecure = 2,
    DiscoveryUnsecure = 3,
    Auto = 4,
    Invalid = 5
}

internal enum WlanIntfOpcode : uint
{
    CurrentConnection = 7,
    ChannelNumber = 8,
    Rssi = 0x10000102
}

internal enum WlanOpcodeValueType : uint
{
    QueryOnly = 0,
    SetByGroupPolicy = 1,
    SetByUser = 2,
    Invalid = 3
}

internal enum Dot11BssType : uint
{
    Infrastructure = 1,
    Independent = 2,
    Any = 3
}

internal enum Dot11PhyType : uint
{
    Unknown = 0,
    Any = 0,
    Fhss = 1,
    Dsss = 2,
    IrBaseband = 3,
    Ofdm = 4,
    HrDsss = 5,
    Erp = 6,
    Ht = 7,
    Vht = 8,
    Dmg = 9,
    He = 10,
    Eht = 11
}

internal enum Dot11AuthAlgorithm : uint
{
    Open = 1,
    SharedKey = 2,
    Wpa = 3,
    WpaPsk = 4,
    WpaNone = 5,
    Rsna = 6,
    RsnaPsk = 7,
    Wpa3Enterprise192 = 8,
    Wpa3Sae = 9,
    Owe = 10,
    Wpa3Enterprise = 11
}

internal enum Dot11CipherAlgorithm : uint
{
    None = 0,
    Wep40 = 1,
    Tkip = 2,
    Ccmp = 4,
    Wep104 = 5,
    Bip = 6,
    Gcmp = 8,
    Gcmp256 = 9,
    Ccmp256 = 10,
    BipGmac128 = 11,
    BipGmac256 = 12,
    BipCmac256 = 13,
    WpaUseGroup = 0x100,
    RsnUseGroup = 0x100,
    Wep = 0x101
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WlanInterfaceInfoNative
{
    internal Guid InterfaceGuid;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    internal string? InterfaceDescription;

    internal WlanInterfaceState State;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Dot11SsidNative
{
    internal uint Length;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
    internal byte[]? Value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct Dot11MacAddressNative
{
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 6)]
    internal byte[]? Value;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanAssociationAttributesNative
{
    internal Dot11SsidNative Ssid;
    internal Dot11BssType BssType;
    internal Dot11MacAddressNative Bssid;
    internal Dot11PhyType PhyType;
    internal uint PhyIndex;
    internal uint SignalQuality;
    internal uint ReceiveRateKbps;
    internal uint TransmitRateKbps;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanSecurityAttributesNative
{
    [MarshalAs(UnmanagedType.Bool)]
    internal bool SecurityEnabled;

    [MarshalAs(UnmanagedType.Bool)]
    internal bool OneXEnabled;

    internal Dot11AuthAlgorithm AuthenticationAlgorithm;
    internal Dot11CipherAlgorithm CipherAlgorithm;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct WlanConnectionAttributesNative
{
    internal WlanInterfaceState State;
    internal WlanConnectionMode ConnectionMode;

    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
    internal string? ProfileName;

    internal WlanAssociationAttributesNative Association;
    internal WlanSecurityAttributesNative Security;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanRateSetNative
{
    internal uint Length;

    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 126)]
    internal ushort[]? RateSet;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WlanBssEntryNative
{
    internal Dot11SsidNative Ssid;
    internal uint PhyId;
    internal Dot11MacAddressNative Bssid;
    internal Dot11BssType BssType;
    internal Dot11PhyType BssPhyType;
    internal int Rssi;
    internal uint LinkQuality;

    [MarshalAs(UnmanagedType.U1)]
    internal bool InRegDomain;

    internal ushort BeaconPeriod;
    internal ulong Timestamp;
    internal ulong HostTimestamp;
    internal ushort CapabilityInformation;
    internal uint ChannelCenterFrequencyKHz;
    internal WlanRateSetNative RateSet;
    internal uint InformationElementOffset;
    internal uint InformationElementSize;
}
