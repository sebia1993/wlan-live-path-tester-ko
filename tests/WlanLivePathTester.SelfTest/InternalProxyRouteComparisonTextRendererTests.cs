using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Core.Reporting;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.SelfTest;

internal static class InternalProxyRouteComparisonTextRendererTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RendersSafeComparisonAndOrderedCandidates();
        SanitizesUntrustedDisplayFields();
        DoesNotReadRawRouteEvidenceOrInputHosts();
        Console.WriteLine(
            "PASS redacted internal and proxy route text renderer tests");
    }

    private static void RendersSafeComparisonAndOrderedCandidates()
    {
        InternalProxyRouteComparisonResult comparison =
            CreateComparison();
        ProxyEndpointRouteAnalysisResult proxy = new(
            Status: ProxyEndpointRouteAnalysisStatus.Success,
            ParseStatus: ProxyDirectiveParseStatus.Success,
            Entries:
            [
                CreateEntry(
                    sequence: 2,
                    ProxyRouteDirectiveKind.HttpsProxy,
                    hostFingerprint: "abcdef0123",
                    interfaceFingerprint: "112233aabb",
                    category: "Tunnel"),
                CreateDirectEntry(sequence: 3),
                CreateEntry(
                    sequence: 1,
                    ProxyRouteDirectiveKind.HttpProxy,
                    hostFingerprint: "0123456789",
                    interfaceFingerprint: "ffeedd0011",
                    category: "Wireless")
            ],
            ParseIssues:
            [
                new ProxyDirectiveIssue(
                    SegmentIndex: 2,
                    Severity: ProxyDirectiveIssueSeverity.Warning,
                    Code: "DUPLICATE_DIRECTIVE",
                    Message: "원문을 출력하지 않는 합성 경고")
            ],
            EndpointLimit: 8,
            WasTruncated: false,
            Message: "합성 분석 결과");

        string text = InternalProxyRouteComparisonTextRenderer.Render(
            comparison,
            proxy);

        Ensure(text.Contains(
                "상태: Diverged",
                StringComparison.Ordinal)
               && text.Contains(
                   "관계: DifferentInterface",
                   StringComparison.Ordinal)
               && text.Contains(
                   "판정 코드: DifferentLocalInterface",
                   StringComparison.Ordinal),
            "구조화 비교 상태·관계·코드를 표시해야 합니다.");
        Ensure(text.Contains(
                "정확한 전체 인터페이스 ID 비교: 수행",
                StringComparison.Ordinal),
            "정확 ID 비교 여부를 표시해야 합니다.");
        Ensure(text.IndexOf("#1", StringComparison.Ordinal)
               < text.IndexOf("#2", StringComparison.Ordinal)
               && text.IndexOf("#2", StringComparison.Ordinal)
               < text.IndexOf("#3", StringComparison.Ordinal),
            "프록시 후보와 DIRECT를 원본 sequence 순서로 표시해야 합니다.");
        Ensure(text.Contains(
                "호스트 지문 0123456789",
                StringComparison.Ordinal)
               && text.Contains(
                   "인터페이스 Wireless / 지문 ffeedd0011",
                   StringComparison.Ordinal),
            "안전한 호스트·인터페이스 지문과 범주를 표시해야 합니다.");
        Ensure(text.Contains(
                "#3 DIRECT · 범위 all · 구문 PacKeyword · 상태 Direct · 네트워크 조회 없음",
                StringComparison.Ordinal),
            "DIRECT 항목은 네트워크 조회가 없음을 표시해야 합니다.");
        Ensure(text.Contains(
                "구간 2 · Warning · DUPLICATE_DIRECTIVE",
                StringComparison.Ordinal),
            "프록시 파싱 경고는 구간·심각도·고정 코드만 표시해야 합니다.");
        Ensure(!text.Contains(
                "원문을 출력하지 않는 합성 경고",
                StringComparison.Ordinal),
            "렌더러는 Issue의 임의 메시지를 직접 출력하지 않아야 합니다.");
    }

    private static void SanitizesUntrustedDisplayFields()
    {
        ProxyEndpointRouteEntry untrusted = new(
            Sequence: -50,
            Kind: (ProxyRouteDirectiveKind)999,
            SourceSyntax: (ProxyDirectiveSourceSyntax)999,
            Scope: "https://secret.example.invalid/path",
            Port: 99999,
            HostFingerprint: "secret-host-value",
            RedactedDisplay:
                "proxy-secret.example.invalid 전체 원문",
            Status: (ProxyEndpointRouteEntryStatus)999,
            SelectedInterfaceFingerprint:
                "C2B2C3D4-E5F6-47A8-9123-1234567890AB",
            SelectedInterfaceCategory:
                "Corporate Secret Adapter",
            SelectedInterfaceOperationalState:
                "Highly Secret State",
            WlanCorrelationStatus:
                "user@example.invalid",
            RouteEvidence: null,
            Message: "10.20.30.40 https://secret.example.invalid");
        ProxyEndpointRouteAnalysisResult proxy = new(
            ProxyEndpointRouteAnalysisStatus.PartialSuccess,
            ProxyDirectiveParseStatus.PartialSuccess,
            [untrusted],
            [
                new ProxyDirectiveIssue(
                    SegmentIndex: -1,
                    Severity: (ProxyDirectiveIssueSeverity)999,
                    Code:
                        "=HYPERLINK(\"https://evil.invalid\",\"x\")",
                    Message: "proxy-secret.example.invalid")
            ],
            EndpointLimit: 8,
            WasTruncated: false,
            Message: "secret");

        string text = InternalProxyRouteComparisonTextRenderer.Render(
            CreateComparison(),
            proxy);

        string[] forbidden =
        [
            "proxy-secret.example.invalid",
            "secret.example.invalid",
            "C2B2C3D4-E5F6-47A8-9123-1234567890AB",
            "Corporate Secret Adapter",
            "Highly Secret State",
            "user@example.invalid",
            "10.20.30.40",
            "evil.invalid",
            "HYPERLINK"
        ];
        foreach (string value in forbidden)
        {
            Ensure(!text.Contains(
                    value,
                    StringComparison.OrdinalIgnoreCase),
                $"안전하지 않은 표시 필드가 렌더링됐습니다: {value}");
        }

        Ensure(text.Contains(
                "#0 Unknown · 범위 unknown · 포트 - · 호스트 지문 확인 불가 · 상태 Unknown",
                StringComparison.Ordinal),
            "알 수 없는 enum·scope·port·fingerprint를 고정 안전값으로 치환해야 합니다.");
        Ensure(text.Contains(
                "구간 0 · Error · INVALID_ISSUE_CODE",
                StringComparison.Ordinal),
            "잘못된 Issue 심각도·코드는 안전한 고정값으로 치환해야 합니다.");
    }

    private static void DoesNotReadRawRouteEvidenceOrInputHosts()
    {
        const string secretHost =
            "proxy-route-secret.example.invalid";
        const string secretLabel =
            "https://internal-route-secret.example.invalid/private.bin";
        const string secretGuid =
            "D2B2C3D4-E5F6-47A8-9123-1234567890AB";
        ProxyEndpointRouteEntry entry = CreateEntry(
            sequence: 1,
            ProxyRouteDirectiveKind.HttpProxy,
            hostFingerprint: "1234567890",
            interfaceFingerprint: "0987654321",
            category: "Wireless") with
        {
            RedactedDisplay = secretHost,
            Message = secretLabel,
            RouteEvidence = null
        };
        ProxyEndpointRouteAnalysisResult proxy = new(
            ProxyEndpointRouteAnalysisStatus.Success,
            ProxyDirectiveParseStatus.Success,
            [entry],
            Array.Empty<ProxyDirectiveIssue>(),
            EndpointLimit: 8,
            WasTruncated: false,
            Message: secretGuid);

        string text = InternalProxyRouteComparisonTextRenderer.Render(
            CreateComparison(),
            proxy);

        Ensure(!text.Contains(
                secretHost,
                StringComparison.OrdinalIgnoreCase)
               && !text.Contains(
                   secretLabel,
                   StringComparison.OrdinalIgnoreCase)
               && !text.Contains(
                   secretGuid,
                   StringComparison.OrdinalIgnoreCase),
            "렌더러는 RedactedDisplay·Message·RouteEvidence 원문을 읽지 않아야 합니다.");
        Ensure(text.Contains(
                "호스트 지문 1234567890",
                StringComparison.Ordinal)
               && text.Contains(
                   "인터페이스 Wireless / 지문 0987654321",
                   StringComparison.Ordinal),
            "검증된 안전 필드만 사용해야 합니다.");
    }

    private static InternalProxyRouteComparisonResult
        CreateComparison() =>
        new(
            Status: InternalProxyRouteComparisonStatus.Diverged,
            Relation: InternalProxyRouteRelation.DifferentInterface,
            Code:
                InternalProxyRouteComparisonCode.DifferentLocalInterface,
            InternalRouteStatus: "Success",
            ProxyAnalysisStatus: "Success",
            InternalInterfaceFingerprint: "aabbccddee",
            InternalInterfaceCategory: "Wireless",
            ProxyInterfaceFingerprints: ["ffeedd0011"],
            ProxyInterfaceCategories: ["Tunnel"],
            ProxyEndpointCount: 2,
            SuccessfulProxyRouteCount: 2,
            DirectDirectiveCount: 1,
            ProxyAnalysisWasTruncated: false,
            ExactIdentityComparisonPerformed: true,
            Message:
                "내부 DIRECT 대상과 확인된 프록시 엔드포인트가 서로 다른 Windows 로컬 인터페이스를 선택했습니다.",
            Interpretation:
                "현재 PC에서 내부 경로와 프록시 경로의 첫 로컬 송출 NIC가 분리돼 있습니다.",
            Limitation:
                "인터페이스 차이만으로 장애를 확정할 수 없습니다.",
            NextStep:
                "각 인터페이스 범주와 VPN 정책을 확인하십시오.");

    private static ProxyEndpointRouteEntry CreateEntry(
        int sequence,
        ProxyRouteDirectiveKind kind,
        string hostFingerprint,
        string interfaceFingerprint,
        string category) =>
        new(
            Sequence: sequence,
            Kind: kind,
            SourceSyntax: ProxyDirectiveSourceSyntax.PacKeyword,
            Scope: "all",
            Port: kind == ProxyRouteDirectiveKind.HttpsProxy
                ? 8443
                : 8080,
            HostFingerprint: hostFingerprint,
            RedactedDisplay: "사용하지 않는 필드",
            Status: ProxyEndpointRouteEntryStatus.Success,
            SelectedInterfaceFingerprint: interfaceFingerprint,
            SelectedInterfaceCategory: category,
            SelectedInterfaceOperationalState: "Up",
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.DifferentInterface.ToString(),
            RouteEvidence: null,
            Message: "사용하지 않는 메시지");

    private static ProxyEndpointRouteEntry CreateDirectEntry(
        int sequence) =>
        new(
            Sequence: sequence,
            Kind: ProxyRouteDirectiveKind.Direct,
            SourceSyntax: ProxyDirectiveSourceSyntax.PacKeyword,
            Scope: "all",
            Port: null,
            HostFingerprint: "없음",
            RedactedDisplay: "DIRECT",
            Status: ProxyEndpointRouteEntryStatus.Direct,
            SelectedInterfaceFingerprint: null,
            SelectedInterfaceCategory: null,
            SelectedInterfaceOperationalState: null,
            WlanCorrelationStatus:
                RouteWlanCorrelationStatus.NotEvaluated.ToString(),
            RouteEvidence: null,
            Message: "네트워크 조회 없음");

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
