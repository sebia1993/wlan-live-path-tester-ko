using System.Runtime.CompilerServices;
using WlanLivePathTester.Windows.Adapters;

namespace WlanLivePathTester.WindowsSmoke;

internal static class NetworkAdapterInventoryReaderTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        VerifyGuidNormalization();
        VerifyLocalInventoryBoundary();
        Console.WriteLine("PASS local network adapter inventory tests");
    }

    private static void VerifyGuidNormalization()
    {
        const string expected = "12345678-1234-1234-1234-1234567890ab";
        string normalized = NetworkAdapterInventoryReader.NormalizeInterfaceId(
            "{12345678-1234-1234-1234-1234567890AB}");
        Ensure(normalized == expected,
            "중괄호와 대문자가 있는 인터페이스 GUID를 표준 D 형식으로 정규화해야 합니다.");

        string textId = NetworkAdapterInventoryReader.NormalizeInterfaceId(
            "  SYNTHETIC-ADAPTER-ID  ");
        Ensure(textId == "synthetic-adapter-id",
            "GUID가 아닌 인터페이스 ID도 공백 제거와 소문자 정규화를 적용해야 합니다.");
    }

    private static void VerifyLocalInventoryBoundary()
    {
        NetworkAdapterInventoryReadResult result =
            NetworkAdapterInventoryReader.Read(
                connectedNativeWlanInterfaceId: null);

        Ensure(result.Adapters.All(adapter =>
                !string.IsNullOrWhiteSpace(adapter.Id)),
            "인벤토리 항목에는 비어 있지 않은 로컬 ID가 필요합니다.");
        Ensure(result.Adapters
                .Select(adapter => adapter.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count()
               == result.Adapters.Count,
            "같은 Windows 인터페이스 ID를 중복 반환하면 안 됩니다.");
        Ensure(result.Adapters.All(adapter =>
                adapter.SpeedBitsPerSecond >= 0),
            "링크 속도는 음수가 아니어야 합니다.");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
