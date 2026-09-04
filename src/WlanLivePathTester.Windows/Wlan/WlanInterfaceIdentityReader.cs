using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Windows.Interop;

namespace WlanLivePathTester.Windows.Wlan;

public sealed record WlanInterfaceIdentity(
    string InterfaceId,
    string Description,
    bool IsConnected);

public sealed record WlanInterfaceIdentityReadResult(
    bool IsSuccess,
    IReadOnlyList<WlanInterfaceIdentity> Interfaces,
    string Message);

[SupportedOSPlatform("windows")]
public static class WlanInterfaceIdentityReader
{
    private const uint ClientVersion = 2;
    private const uint ErrorSuccess = 0;
    private const int InterfaceListHeaderSize = sizeof(uint) * 2;
    private const int MaximumInterfaceCount = 64;

    public static WlanInterfaceIdentityReadResult ReadCurrent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return new WlanInterfaceIdentityReadResult(
                IsSuccess: false,
                Interfaces: Array.Empty<WlanInterfaceIdentity>(),
                Message: "Windows에서만 WLAN 인터페이스 ID를 확인할 수 있습니다.");
        }

        nint clientHandle = nint.Zero;
        nint interfaceList = nint.Zero;

        try
        {
            uint openResult = WlanNative.WlanOpenHandle(
                ClientVersion,
                nint.Zero,
                out _,
                out clientHandle);
            if (openResult != ErrorSuccess)
            {
                return Failure(openResult);
            }

            uint enumResult = WlanNative.WlanEnumInterfaces(
                clientHandle,
                nint.Zero,
                out interfaceList);
            if (enumResult != ErrorSuccess || interfaceList == nint.Zero)
            {
                return Failure(
                    enumResult == ErrorSuccess ? 13U : enumResult);
            }

            int count = Marshal.ReadInt32(interfaceList, 0);
            if (count < 0 || count > MaximumInterfaceCount)
            {
                return new WlanInterfaceIdentityReadResult(
                    IsSuccess: false,
                    Interfaces: Array.Empty<WlanInterfaceIdentity>(),
                    Message: "WLAN 인터페이스 ID 목록 개수가 허용 범위를 벗어났습니다.");
            }

            int itemSize = Marshal.SizeOf<WlanInterfaceInfoNative>();
            List<WlanInterfaceIdentity> identities = new(count);
            for (int index = 0; index < count; index++)
            {
                int offset = checked(
                    InterfaceListHeaderSize + itemSize * index);
                nint itemPointer = IntPtr.Add(interfaceList, offset);
                WlanInterfaceInfoNative item =
                    Marshal.PtrToStructure<WlanInterfaceInfoNative>(
                        itemPointer);
                identities.Add(new WlanInterfaceIdentity(
                    InterfaceId: item.InterfaceGuid.ToString("D"),
                    Description: NormalizeDescription(
                        item.InterfaceDescription),
                    IsConnected:
                        item.State == WlanInterfaceState.Connected));
            }

            return new WlanInterfaceIdentityReadResult(
                IsSuccess: true,
                Interfaces: identities,
                Message: $"WLAN 인터페이스 ID {identities.Count}개를 로컬 WLAN API에서 확인했습니다.");
        }
        catch (Exception exception) when (
            exception is DllNotFoundException
                or EntryPointNotFoundException
                or BadImageFormatException
                or ArgumentException
                or OverflowException)
        {
            return new WlanInterfaceIdentityReadResult(
                IsSuccess: false,
                Interfaces: Array.Empty<WlanInterfaceIdentity>(),
                Message: $"WLAN 인터페이스 ID를 읽지 못했습니다: {exception.GetType().Name}");
        }
        finally
        {
            if (interfaceList != nint.Zero)
            {
                WlanNative.WlanFreeMemory(interfaceList);
            }

            if (clientHandle != nint.Zero)
            {
                _ = WlanNative.WlanCloseHandle(
                    clientHandle,
                    nint.Zero);
            }
        }
    }

    public static WlanSnapshot? AttachIdentity(
        WlanSnapshot? snapshot,
        WlanInterfaceIdentityReadResult identities)
    {
        if (snapshot is null
            || !snapshot.IsConnected
            || !string.IsNullOrWhiteSpace(snapshot.InterfaceId)
            || !identities.IsSuccess)
        {
            return snapshot;
        }

        string description = NormalizeDescription(
            snapshot.InterfaceDescription);
        WlanInterfaceIdentity[] matches = identities.Interfaces
            .Where(identity => identity.IsConnected)
            .Where(identity => identity.Description.Equals(
                description,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length == 1
            ? snapshot with { InterfaceId = matches[0].InterfaceId }
            : snapshot;
    }

    private static WlanInterfaceIdentityReadResult Failure(
        uint errorCode) =>
        new(
            IsSuccess: false,
            Interfaces: Array.Empty<WlanInterfaceIdentity>(),
            Message: $"WLAN 인터페이스 ID API가 오류 {errorCode}를 반환했습니다.");

    private static string NormalizeDescription(string? value) =>
        string.Join(
            ' ',
            (value ?? string.Empty)
                .Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
}
