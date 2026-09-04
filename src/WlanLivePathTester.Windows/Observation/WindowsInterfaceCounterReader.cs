using System.Net.NetworkInformation;
using System.Runtime.VersionServices;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Observation;

namespace WlanLivePathTester.Windows.Observation;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class WindowsInterfaceCounterReader
{
    public static InterfaceCounterReadResult ReadCurrent(
        string? preferredInterfaceId = null,
        string? preferredInterfaceDescription = null,
        InterfaceCounterSelectionMode selectionMode =
            InterfaceCounterSelectionMode.PreferExactIdentityThenDescription)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.UnsupportedPlatform,
                null,
                "Windows에서만 Wi-Fi 인터페이스 카운터를 읽을 수 있습니다.");
        }

        try
        {
            NetworkInterface[] interfaces =
                NetworkInterface.GetAllNetworkInterfaces();
            InterfaceCounterCandidate[] candidates = interfaces
                .Select((item, index) =>
                {
                    NetworkAdapterClassification classification =
                        NetworkAdapterClassifier.Classify(
                            item.NetworkInterfaceType.ToString(),
                            item.Name,
                            item.Description);
                    bool isEligibleWireless =
                        item.NetworkInterfaceType
                            == NetworkInterfaceType.Wireless80211
                        && !classification.IsVirtual
                        && !classification.IsVpn;
                    return new InterfaceCounterCandidate(
                        CandidateIndex: index,
                        InterfaceId: item.Id,
                        Description: item.Description,
                        IsWireless: isEligibleWireless,
                        IsOperational:
                            item.OperationalStatus
                                == OperationalStatus.Up);
                })
                .ToArray();
            InterfaceCounterSelectionDecision selection =
                InterfaceCounterSelectionPolicy.Select(
                    candidates,
                    preferredInterfaceId,
                    preferredInterfaceDescription,
                    selectionMode);

            if (!selection.IsSelected
                || !selection.SelectedCandidateIndex.HasValue)
            {
                return new InterfaceCounterReadResult(
                    MapFailureStatus(selection.Status),
                    null,
                    selection.Message);
            }

            int selectedIndex = selection.SelectedCandidateIndex.Value;
            if (selectedIndex < 0 || selectedIndex >= interfaces.Length)
            {
                return new InterfaceCounterReadResult(
                    InterfaceCounterReadStatus.Failed,
                    null,
                    "Wi-Fi 인터페이스 선택 결과가 로컬 목록 범위를 벗어났습니다.");
            }

            NetworkInterface selected = interfaces[selectedIndex];
            NetworkAdapterClassification selectedClassification =
                NetworkAdapterClassifier.Classify(
                    selected.NetworkInterfaceType.ToString(),
                    selected.Name,
                    selected.Description);
            if (selected.NetworkInterfaceType
                    != NetworkInterfaceType.Wireless80211
                || selectedClassification.IsVirtual
                || selectedClassification.IsVpn)
            {
                return new InterfaceCounterReadResult(
                    InterfaceCounterReadStatus.Failed,
                    null,
                    "선택 정책이 물리 Wi-Fi 후보가 아닌 인터페이스를 반환해 카운터를 읽지 않았습니다.");
            }

            string selectedInterfaceId =
                ObservationInterfaceBindingPolicy.NormalizeInterfaceId(
                    selected.Id);
            if (string.IsNullOrWhiteSpace(selectedInterfaceId))
            {
                return new InterfaceCounterReadResult(
                    InterfaceCounterReadStatus.CounterProviderMismatch,
                    null,
                    "선택된 Wi-Fi 인터페이스의 ID를 정규화하지 못해 카운터를 읽지 않았습니다.");
            }

            if (selectionMode
                    == InterfaceCounterSelectionMode.RequireExactInterfaceId
                && !selectedInterfaceId.Equals(
                    ObservationInterfaceBindingPolicy.NormalizeInterfaceId(
                        preferredInterfaceId),
                    StringComparison.OrdinalIgnoreCase))
            {
                return new InterfaceCounterReadResult(
                    InterfaceCounterReadStatus.CounterProviderMismatch,
                    null,
                    "카운터 공급자가 시작 시 고정한 Wi-Fi와 다른 인터페이스를 선택해 결과를 거부했습니다.");
            }

            IPInterfaceStatistics statistics = selected.GetIPStatistics();
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.Success,
                new InterfaceCounterSnapshot(
                    Timestamp: DateTimeOffset.UtcNow,
                    InterfaceId: selectedInterfaceId,
                    InterfaceName: selected.Name,
                    InterfaceDescription: selected.Description,
                    BytesReceived: statistics.BytesReceived,
                    BytesSent: statistics.BytesSent,
                    IsOperational:
                        selected.OperationalStatus == OperationalStatus.Up),
                selection.Message
                + " Wi-Fi 인터페이스 누적 바이트를 읽었습니다.");
        }
        catch (NetworkInformationException exception)
        {
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.StatisticsUnavailable,
                null,
                $"Wi-Fi 인터페이스 통계를 읽지 못했습니다: {exception.ErrorCode}");
        }
        catch (PlatformNotSupportedException)
        {
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.UnsupportedPlatform,
                null,
                "현재 운영체제에서 네트워크 인터페이스 통계를 지원하지 않습니다.");
        }
        catch (Exception exception)
        {
            return new InterfaceCounterReadResult(
                InterfaceCounterReadStatus.Failed,
                null,
                $"Wi-Fi 인터페이스 카운터 확인 중 오류가 발생했습니다: {exception.Message}");
        }
    }

    private static InterfaceCounterReadStatus MapFailureStatus(
        InterfaceCounterSelectionStatus status) =>
        status switch
        {
            InterfaceCounterSelectionStatus.PreferredInterfaceNotFound =>
                InterfaceCounterReadStatus.PreferredInterfaceNotFound,
            InterfaceCounterSelectionStatus.PreferredInterfaceNotOperational =>
                InterfaceCounterReadStatus.InterfaceNotOperational,
            InterfaceCounterSelectionStatus.AmbiguousWirelessInterfaces =>
                InterfaceCounterReadStatus.InterfaceAmbiguous,
            _ => InterfaceCounterReadStatus.InterfaceNotFound
        };
}
