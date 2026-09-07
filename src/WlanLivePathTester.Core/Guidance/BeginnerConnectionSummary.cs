using System.Text;

namespace WlanLivePathTester.Core.Guidance;

public sealed record LocalConnectionEvidence(
    string Kind, bool IsUp, bool ReadSucceeded, bool HasUsableAddress,
    bool HasLinkLocalAddress, bool HasGateway, bool HasDnsServer);

public static class BeginnerConnectionSummary
{
    public static string Render(IReadOnlyList<LocalConnectionEvidence> adapters)
    {
        StringBuilder text = new();
        text.AppendLine("관찰된 설정 · 현재 PC에 등록된 값이며 통신 성공 판정이 아닙니다.");
        if (adapters.Count == 0) text.AppendLine("판단 불가 — 확인할 네트워크 인터페이스 정보가 없습니다.");
        for (int i = 0; i < adapters.Count; i++)
        {
            LocalConnectionEvidence adapter = adapters[i];
            text.AppendLine($"{i + 1}. {adapter.Kind} · {(adapter.IsUp ? "링크 활성" : "링크 비활성")}");
            if (!adapter.ReadSucceeded)
            {
                text.AppendLine("   판단 불가 — IP 설정을 읽지 못했습니다. Windows 어댑터 상태를 확인하세요.");
                continue;
            }
            if (!adapter.IsUp)
            {
                text.AppendLine("   다음 점검: 사용할 연결인지 확인하세요. 사용하지 않는 어댑터의 비활성은 장애 증거가 아닙니다.");
                continue;
            }
            text.AppendLine("   IP: " + (adapter.HasUsableAddress ? "사용 가능한 주소 설정 있음" : "주의 · 일반 통신용 주소를 확인하지 못함"));
            if (adapter.HasLinkLocalAddress && !adapter.HasUsableAddress)
                text.AppendLine("   링크 로컬 주소만 관찰됨. DHCP 또는 고정 IP 설정을 확인하세요. DHCP 장애로 확정하지 않습니다.");
            text.AppendLine("   게이트웨이: " + (adapter.HasGateway ? "설정 있음 · 도달 여부 미검사" : "설정 없음 · 다른 망 접속이 필요하면 경로 확인"));
            text.AppendLine("   DNS 서버: " + (adapter.HasDnsServer ? "설정 있음 · 이름 해석 성공 여부 미검사" : "설정 없음 · 이름으로 접속하려면 Windows DNS 설정 확인"));
        }
        text.AppendLine("아직 확인하지 못한 부분: 게이트웨이 응답, DHCP 교환, DNS 응답, 개별 TCP/TLS 연결, 802.1X 인증 원인.");
        text.Append("지금 할 일: Wi-Fi 연결이 안 되면 Windows Wi-Fi 오류와 인증 로그를 확인하세요. 연결된 경우 대상별 실제 경로를 확인한 뒤 승인된 대상의 HTTP 측정을 실행하세요.");
        return text.ToString();
    }
}
