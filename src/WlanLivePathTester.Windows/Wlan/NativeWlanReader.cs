using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Wlan;
using WlanLivePathTester.Windows.Interop;

namespace WlanLivePathTester.Windows.Wlan;

[SupportedOSPlatform("windows")]
public static class NativeWlanReader
{
    private const uint ClientVersion = 2;
    private const uint ErrorSuccess = 0;
    private const uint ErrorAccessDenied = 5;
    private const uint ErrorInvalidData = 13;
    private const int InterfaceListHeaderSize = sizeof(uint) * 2;
    private const int BssListHeaderSize = sizeof(uint) * 2;
    private const int MaximumInterfaceCount = 64;
    private const int MaximumBssCount = 4096;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static WlanReadResult ReadCurrent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WlanReadResult(
                WlanReadStatus.UnsupportedPlatform,
                [],
                null,
                "Windows에서만 WLAN 상태를 확인할 수 있습니다.");
        }

        nint clientHandle = nint.Zero;

        try
        {
            uint openResult = WlanNative.WlanOpenHandle(
                ClientVersion,
                nint.Zero,
                out _,
                out clientHandle);

            if (openResult != ErrorSuccess)
            {
                return FailureFromNativeCode(openResult, []);
            }

            uint enumResult = WlanNative.WlanEnumInterfaces(
                clientHandle,
                nint.Zero,
                out nint interfaceList);

            if (enumResult != ErrorSuccess)
            {
                return FailureFromNativeCode(enumResult, []);
            }

            try
            {
                return ReadInterfaceList(clientHandle, interfaceList);
            }
            finally
            {
                FreeWlanMemory(interfaceList);
            }
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or ArgumentException
            or OverflowException)
        {
            return new WlanReadResult(
                WlanReadStatus.NativeError,
                [],
                null,
                $"Windows WLAN API를 처리하지 못했습니다: {exception.Message}");
        }
        finally
        {
            if (clientHandle != nint.Zero)
            {
                _ = WlanNative.WlanCloseHandle(clientHandle, nint.Zero);
            }
        }
    }

    private static WlanReadResult ReadInterfaceList(nint clientHandle, nint interfaceList)
    {
        if (interfaceList == nint.Zero)
        {
            return new WlanReadResult(
                WlanReadStatus.NativeError,
                [],
                ErrorInvalidData,
                "WLAN 인터페이스 목록 포인터가 비어 있습니다.");
        }

        int rawCount = Marshal.ReadInt32(interfaceList, 0);
        if (rawCount < 0 || rawCount > MaximumInterfaceCount)
        {
            return new WlanReadResult(
                WlanReadStatus.NativeError,
                [],
                ErrorInvalidData,
                "WLAN 인터페이스 개수가 허용 범위를 벗어났습니다.");
        }

        if (rawCount == 0)
        {
            return new WlanReadResult(
                WlanReadStatus.NoWirelessInterfaces,
                [],
                null,
                "Windows에서 사용할 수 있는 무선 인터페이스를 찾지 못했습니다.");
        }

        int itemSize = Marshal.SizeOf<WlanInterfaceInfoNative>();
        List<WlanSnapshot> snapshots = new(rawCount);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;

        for (int index = 0; index < rawCount; index++)
        {
            int offset = checked(InterfaceListHeaderSize + (itemSize * index));
            nint itemPointer = IntPtr.Add(interfaceList, offset);
            WlanInterfaceInfoNative interfaceInfo =
                Marshal.PtrToStructure<WlanInterfaceInfoNative>(itemPointer);

            if (interfaceInfo.State != WlanInterfaceState.Connected)
            {
                snapshots.Add(new WlanSnapshot(
                    Timestamp: timestamp,
                    IsConnected: false,
                    Ssid: null,
                    Bssid: null,
                    RssiDbm: null,
                    Channel: null,
                    PhyType: null,
                    ReceiveLinkSpeedBps: null,
                    TransmitLinkSpeedBps: null,
                    InterfaceDescription: interfaceInfo.InterfaceDescription,
                    InterfaceState: GetInterfaceStateText(interfaceInfo.State)));
                continue;
            }

            snapshots.Add(ReadConnectedInterface(clientHandle, interfaceInfo, timestamp));
        }

        WlanSnapshot? connected = snapshots.FirstOrDefault(
            item => item.IsConnected && item.Ssid is not null);

        if (connected is not null)
        {
            int partialCount = snapshots.Count(item => item.ReadError is not null);
            string message = partialCount == 0
                ? "현재 WLAN 연결 정보를 확인했습니다."
                : $"현재 WLAN 연결을 확인했지만 {partialCount}개 인터페이스에서 일부 세부 정보를 읽지 못했습니다.";

            return new WlanReadResult(WlanReadStatus.Success, snapshots, null, message);
        }

        WlanSnapshot? failedConnected = snapshots.FirstOrDefault(item => item.IsConnected);
        if (failedConnected?.NativeErrorCode == ErrorAccessDenied)
        {
            return new WlanReadResult(
                WlanReadStatus.AccessDenied,
                snapshots,
                ErrorAccessDenied,
                "WLAN 세부 정보 접근이 거부되었습니다. Windows 위치 권한과 회사 정책을 확인하십시오.");
        }

        if (failedConnected?.NativeErrorCode is uint failureCode)
        {
            return FailureFromNativeCode(failureCode, snapshots);
        }

        return new WlanReadResult(
            WlanReadStatus.NotConnected,
            snapshots,
            null,
            "무선 인터페이스는 존재하지만 현재 연결된 WLAN이 없습니다.");
    }

    private static WlanSnapshot ReadConnectedInterface(
        nint clientHandle,
        WlanInterfaceInfoNative interfaceInfo,
        DateTimeOffset timestamp)
    {
        uint connectionResult = WlanNative.WlanQueryInterface(
            clientHandle,
            in interfaceInfo.InterfaceGuid,
            WlanIntfOpcode.CurrentConnection,
            nint.Zero,
            out uint connectionSize,
            out nint connectionPointer,
            out _);

        if (connectionResult != ErrorSuccess || connectionPointer == nint.Zero)
        {
            FreeWlanMemory(connectionPointer);
            uint errorCode = connectionResult == ErrorSuccess ? ErrorInvalidData : connectionResult;
            return new WlanSnapshot(
                Timestamp: timestamp,
                IsConnected: true,
                Ssid: null,
                Bssid: null,
                RssiDbm: null,
                Channel: null,
                PhyType: null,
                ReceiveLinkSpeedBps: null,
                TransmitLinkSpeedBps: null,
                InterfaceDescription: interfaceInfo.InterfaceDescription,
                InterfaceState: GetInterfaceStateText(interfaceInfo.State),
                NativeErrorCode: errorCode,
                ReadError: DescribeError(errorCode));
        }

        try
        {
            int expectedSize = Marshal.SizeOf<WlanConnectionAttributesNative>();
            if (connectionSize < expectedSize)
            {
                return new WlanSnapshot(
                    Timestamp: timestamp,
                    IsConnected: true,
                    Ssid: null,
                    Bssid: null,
                    RssiDbm: null,
                    Channel: null,
                    PhyType: null,
                    ReceiveLinkSpeedBps: null,
                    TransmitLinkSpeedBps: null,
                    InterfaceDescription: interfaceInfo.InterfaceDescription,
                    InterfaceState: GetInterfaceStateText(interfaceInfo.State),
                    NativeErrorCode: ErrorInvalidData,
                    ReadError: "현재 연결 정보 구조체의 크기가 예상보다 작습니다.");
            }

            WlanConnectionAttributesNative attributes =
                Marshal.PtrToStructure<WlanConnectionAttributesNative>(connectionPointer);

            (int? rssi, uint? rssiError) = QueryInt32(
                clientHandle,
                interfaceInfo.InterfaceGuid,
                WlanIntfOpcode.Rssi);

            (uint? channel, uint? channelError) = QueryUInt32(
                clientHandle,
                interfaceInfo.InterfaceGuid,
                WlanIntfOpcode.ChannelNumber);

            (uint? centerFrequencyMhz, uint? bssError) = TryReadCenterFrequency(
                clientHandle,
                interfaceInfo.InterfaceGuid,
                attributes.Association.Bssid);

            channel ??= WlanChannelCalculator.FromCenterFrequencyMhz(centerFrequencyMhz);

            List<string> partialErrors = [];
            AddPartialError(partialErrors, "RSSI", rssiError);
            AddPartialError(partialErrors, "채널", channelError);
            AddPartialError(partialErrors, "주파수", bssError);

            return new WlanSnapshot(
                Timestamp: timestamp,
                IsConnected: true,
                Ssid: DecodeSsid(attributes.Association.Ssid),
                Bssid: FormatBssid(attributes.Association.Bssid),
                RssiDbm: rssi,
                Channel: channel,
                PhyType: GetPhyTypeText(attributes.Association.PhyType),
                ReceiveLinkSpeedBps: checked((ulong)attributes.Association.ReceiveRateKbps * 1000UL),
                TransmitLinkSpeedBps: checked((ulong)attributes.Association.TransmitRateKbps * 1000UL),
                InterfaceDescription: interfaceInfo.InterfaceDescription,
                InterfaceState: GetInterfaceStateText(attributes.State),
                SignalQualityPercent: (int)Math.Min(attributes.Association.SignalQuality, 100U),
                CenterFrequencyMhz: centerFrequencyMhz,
                Authentication: GetAuthenticationText(attributes.Security.AuthenticationAlgorithm),
                Cipher: GetCipherText(attributes.Security.CipherAlgorithm),
                ReadError: partialErrors.Count == 0 ? null : string.Join(" ", partialErrors));
        }
        finally
        {
            FreeWlanMemory(connectionPointer);
        }
    }

    private static (int? Value, uint? ErrorCode) QueryInt32(
        nint clientHandle,
        Guid interfaceGuid,
        WlanIntfOpcode opcode)
    {
        uint result = WlanNative.WlanQueryInterface(
            clientHandle,
            in interfaceGuid,
            opcode,
            nint.Zero,
            out uint dataSize,
            out nint data,
            out _);

        if (result != ErrorSuccess || data == nint.Zero)
        {
            FreeWlanMemory(data);
            return (null, result == ErrorSuccess ? ErrorInvalidData : result);
        }

        try
        {
            return dataSize >= sizeof(int)
                ? (Marshal.ReadInt32(data), null)
                : (null, ErrorInvalidData);
        }
        finally
        {
            FreeWlanMemory(data);
        }
    }

    private static (uint? Value, uint? ErrorCode) QueryUInt32(
        nint clientHandle,
        Guid interfaceGuid,
        WlanIntfOpcode opcode)
    {
        (int? value, uint? errorCode) = QueryInt32(clientHandle, interfaceGuid, opcode);
        return value is int signedValue
            ? (unchecked((uint)signedValue), errorCode)
            : (null, errorCode);
    }

    private static (uint? FrequencyMhz, uint? ErrorCode) TryReadCenterFrequency(
        nint clientHandle,
        Guid interfaceGuid,
        Dot11MacAddressNative currentBssid)
    {
        uint result = WlanNative.WlanGetNetworkBssList(
            clientHandle,
            in interfaceGuid,
            nint.Zero,
            Dot11BssType.Any,
            false,
            nint.Zero,
            out nint bssList);

        if (result != ErrorSuccess || bssList == nint.Zero)
        {
            FreeWlanMemory(bssList);
            return (null, result == ErrorSuccess ? ErrorInvalidData : result);
        }

        try
        {
            int rawCount = Marshal.ReadInt32(bssList, sizeof(uint));
            if (rawCount < 0 || rawCount > MaximumBssCount)
            {
                return (null, ErrorInvalidData);
            }

            int entrySize = Marshal.SizeOf<WlanBssEntryNative>();
            for (int index = 0; index < rawCount; index++)
            {
                int offset = checked(BssListHeaderSize + (entrySize * index));
                nint entryPointer = IntPtr.Add(bssList, offset);
                WlanBssEntryNative entry = Marshal.PtrToStructure<WlanBssEntryNative>(entryPointer);

                if (!BssidEquals(entry.Bssid, currentBssid))
                {
                    continue;
                }

                uint frequencyMhz = entry.ChannelCenterFrequencyKHz >= 1000
                    ? entry.ChannelCenterFrequencyKHz / 1000
                    : entry.ChannelCenterFrequencyKHz;

                return (frequencyMhz, null);
            }

            return (null, null);
        }
        finally
        {
            FreeWlanMemory(bssList);
        }
    }

    private static bool BssidEquals(Dot11MacAddressNative left, Dot11MacAddressNative right)
    {
        byte[]? leftBytes = left.Value;
        byte[]? rightBytes = right.Value;

        return leftBytes is { Length: >= 6 }
            && rightBytes is { Length: >= 6 }
            && leftBytes.AsSpan(0, 6).SequenceEqual(rightBytes.AsSpan(0, 6));
    }

    private static string DecodeSsid(Dot11SsidNative ssid)
    {
        byte[] bytes = ssid.Value ?? [];
        int length = checked((int)Math.Min(ssid.Length, (uint)bytes.Length));

        if (length == 0)
        {
            return "(숨김 또는 빈 SSID)";
        }

        ReadOnlySpan<byte> value = bytes.AsSpan(0, length);

        try
        {
            return StrictUtf8.GetString(value);
        }
        catch (DecoderFallbackException)
        {
            return $"[HEX:{Convert.ToHexString(value)}]";
        }
    }

    private static string? FormatBssid(Dot11MacAddressNative bssid)
    {
        byte[]? bytes = bssid.Value;
        if (bytes is not { Length: >= 6 })
        {
            return null;
        }

        return string.Join(":", bytes.Take(6).Select(value => value.ToString("X2")));
    }

    private static string GetInterfaceStateText(WlanInterfaceState state)
    {
        return state switch
        {
            WlanInterfaceState.NotReady => "준비되지 않음",
            WlanInterfaceState.Connected => "연결됨",
            WlanInterfaceState.AdHocNetworkFormed => "Ad-hoc 구성됨",
            WlanInterfaceState.Disconnecting => "연결 해제 중",
            WlanInterfaceState.Disconnected => "연결 안 됨",
            WlanInterfaceState.Associating => "연결 시도 중",
            WlanInterfaceState.Discovering => "검색 중",
            WlanInterfaceState.Authenticating => "인증 중",
            _ => $"알 수 없음 ({(uint)state})"
        };
    }

    private static string GetPhyTypeText(Dot11PhyType phyType)
    {
        return phyType switch
        {
            Dot11PhyType.Fhss => "FHSS",
            Dot11PhyType.Dsss => "DSSS",
            Dot11PhyType.IrBaseband => "IR Baseband",
            Dot11PhyType.Ofdm => "802.11a",
            Dot11PhyType.HrDsss => "802.11b",
            Dot11PhyType.Erp => "802.11g",
            Dot11PhyType.Ht => "802.11n",
            Dot11PhyType.Vht => "802.11ac",
            Dot11PhyType.Dmg => "802.11ad",
            Dot11PhyType.He => "802.11ax",
            Dot11PhyType.Eht => "802.11be",
            _ => $"알 수 없음 ({(uint)phyType})"
        };
    }

    private static string GetAuthenticationText(Dot11AuthAlgorithm algorithm)
    {
        return algorithm switch
        {
            Dot11AuthAlgorithm.Open => "Open",
            Dot11AuthAlgorithm.SharedKey => "Shared Key",
            Dot11AuthAlgorithm.Wpa => "WPA-Enterprise",
            Dot11AuthAlgorithm.WpaPsk => "WPA-Personal",
            Dot11AuthAlgorithm.WpaNone => "WPA-None",
            Dot11AuthAlgorithm.Rsna => "RSNA-Enterprise",
            Dot11AuthAlgorithm.RsnaPsk => "RSNA-Personal",
            Dot11AuthAlgorithm.Wpa3Enterprise192 => "WPA3-Enterprise 192-bit",
            Dot11AuthAlgorithm.Wpa3Sae => "WPA3-SAE",
            Dot11AuthAlgorithm.Owe => "OWE",
            Dot11AuthAlgorithm.Wpa3Enterprise => "WPA3-Enterprise",
            _ => $"알 수 없음 ({(uint)algorithm})"
        };
    }

    private static string GetCipherText(Dot11CipherAlgorithm algorithm)
    {
        return algorithm switch
        {
            Dot11CipherAlgorithm.None => "None",
            Dot11CipherAlgorithm.Wep40 => "WEP-40",
            Dot11CipherAlgorithm.Tkip => "TKIP",
            Dot11CipherAlgorithm.Ccmp => "CCMP/AES",
            Dot11CipherAlgorithm.Wep104 => "WEP-104",
            Dot11CipherAlgorithm.Bip => "BIP",
            Dot11CipherAlgorithm.Gcmp => "GCMP",
            Dot11CipherAlgorithm.Gcmp256 => "GCMP-256",
            Dot11CipherAlgorithm.Ccmp256 => "CCMP-256",
            Dot11CipherAlgorithm.BipGmac128 => "BIP-GMAC-128",
            Dot11CipherAlgorithm.BipGmac256 => "BIP-GMAC-256",
            Dot11CipherAlgorithm.BipCmac256 => "BIP-CMAC-256",
            Dot11CipherAlgorithm.WpaUseGroup => "Use Group Cipher",
            Dot11CipherAlgorithm.Wep => "WEP",
            _ => $"알 수 없음 ({(uint)algorithm})"
        };
    }

    private static void AddPartialError(
        ICollection<string> messages,
        string field,
        uint? errorCode)
    {
        if (errorCode is uint code)
        {
            messages.Add($"{field} 확인 실패({code}: {DescribeError(code)}).");
        }
    }

    private static WlanReadResult FailureFromNativeCode(
        uint errorCode,
        IReadOnlyList<WlanSnapshot> snapshots)
    {
        WlanReadStatus status = errorCode == ErrorAccessDenied
            ? WlanReadStatus.AccessDenied
            : WlanReadStatus.NativeError;

        return new WlanReadResult(
            status,
            snapshots,
            errorCode,
            $"Windows WLAN API 오류 {errorCode}: {DescribeError(errorCode)}");
    }

    private static string DescribeError(uint errorCode)
    {
        return new Win32Exception(checked((int)errorCode)).Message;
    }

    private static void FreeWlanMemory(nint pointer)
    {
        if (pointer != nint.Zero)
        {
            WlanNative.WlanFreeMemory(pointer);
        }
    }
}
