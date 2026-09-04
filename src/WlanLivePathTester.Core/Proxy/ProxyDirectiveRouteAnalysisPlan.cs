using System.Diagnostics;
using System.Text.Json.Serialization;

namespace WlanLivePathTester.Core.Proxy;

public enum ProxyDirectiveRouteAnalysisPlanStatus
{
    AnalyzeProxyEndpoints,
    DirectOnly,
    Blocked,
    Unavailable
}

public enum ProxyDirectiveRouteAnalysisPlanCode
{
    TargetSpecificProxySelected,
    ManualProxySelected,
    TargetSpecificDirect,
    ManualDirect,
    InvalidSourceDecision,
    MissingSourceDecision,
    InconsistentSelectionResult
}

[DebuggerDisplay("{RedactedDisplay,nq}")]
public sealed class ProxyDirectiveRouteAnalysisPlan
{
    internal ProxyDirectiveRouteAnalysisPlan(
        ProxyDirectiveRouteAnalysisPlanStatus status,
        ProxyDirectiveRouteAnalysisPlanCode code,
        ProxyDirectiveSourceKind sourceKind,
        ProxyDirectiveSourceSelectionStatus selectionStatus,
        string? directiveText,
        ProxyDirectiveParseResult? parseResult,
        string message)
    {
        Status = status;
        Code = code;
        SourceKind = sourceKind;
        SelectionStatus = selectionStatus;
        DirectiveText = directiveText;
        ParseResult = parseResult;
        ProxyEndpointCount = parseResult?.Directives.Count(
            directive => !directive.IsDirect) ?? 0;
        DirectDirectiveCount = parseResult?.Directives.Count(
            directive => directive.IsDirect) ?? 0;
        HasParseErrors = parseResult?.Issues.Any(issue =>
            issue.Severity == ProxyDirectiveIssueSeverity.Error) == true;
        Message = message;
        RedactedDisplay =
            $"{Status} · {Code} · {SourceKind} · 프록시 후보 {ProxyEndpointCount}개 · DIRECT {DirectDirectiveCount}개 · 파싱 오류 {(HasParseErrors ? "있음" : "없음")}";
    }

    public ProxyDirectiveRouteAnalysisPlanStatus Status { get; }

    public ProxyDirectiveRouteAnalysisPlanCode Code { get; }

    public ProxyDirectiveSourceKind SourceKind { get; }

    public ProxyDirectiveSourceSelectionStatus SelectionStatus { get; }

    [JsonIgnore]
    public string? DirectiveText { get; }

    public ProxyDirectiveParseResult? ParseResult { get; }

    public int ProxyEndpointCount { get; }

    public int DirectDirectiveCount { get; }

    public bool HasParseErrors { get; }

    public string Message { get; }

    public string RedactedDisplay { get; }

    public bool ShouldAnalyzeProxyEndpoints =>
        Status
        == ProxyDirectiveRouteAnalysisPlanStatus.AnalyzeProxyEndpoints;

    public bool NetworkLookupAllowed => ShouldAnalyzeProxyEndpoints;

    public override string ToString() => RedactedDisplay;
}

public static class ProxyDirectiveRouteAnalysisPlanPolicy
{
    public static ProxyDirectiveRouteAnalysisPlan Create(
        ProxyDirectiveSourceSelectionResult selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return selection.Status switch
        {
            ProxyDirectiveSourceSelectionStatus.Selected
                or ProxyDirectiveSourceSelectionStatus.SelectedWithWarnings =>
                CreateProxyPlan(selection),
            ProxyDirectiveSourceSelectionStatus.Direct =>
                CreateDirectPlan(selection),
            ProxyDirectiveSourceSelectionStatus.Invalid =>
                new ProxyDirectiveRouteAnalysisPlan(
                    ProxyDirectiveRouteAnalysisPlanStatus.Blocked,
                    ProxyDirectiveRouteAnalysisPlanCode
                        .InvalidSourceDecision,
                    selection.SourceKind,
                    selection.Status,
                    directiveText: null,
                    selection.ParseResult,
                    "프록시 지시문 출처 판정이 유효하지 않아 DNS·프록시 엔드포인트 경로 조회를 시작하지 않습니다."),
            ProxyDirectiveSourceSelectionStatus.Unavailable =>
                new ProxyDirectiveRouteAnalysisPlan(
                    ProxyDirectiveRouteAnalysisPlanStatus.Unavailable,
                    ProxyDirectiveRouteAnalysisPlanCode
                        .MissingSourceDecision,
                    selection.SourceKind,
                    selection.Status,
                    directiveText: null,
                    selection.ParseResult,
                    "사용할 수 있는 대상별 또는 수동 프록시 지시문이 없어 DNS·프록시 엔드포인트 경로 조회를 시작하지 않습니다."),
            _ => CreateInconsistentPlan(selection)
        };
    }

    private static ProxyDirectiveRouteAnalysisPlan CreateProxyPlan(
        ProxyDirectiveSourceSelectionResult selection)
    {
        ProxyDirectiveParseResult? parsed = selection.ParseResult;
        string directiveText = selection.SelectedDirectiveText
            ?? string.Empty;
        bool consistent = directiveText.Length > 0
            && parsed is not null
            && parsed.HasProxyEndpoint
            && selection.ProxyEndpointCount > 0;
        if (!consistent)
        {
            return CreateInconsistentPlan(selection);
        }

        ProxyDirectiveRouteAnalysisPlanCode code = selection.SourceKind
            == ProxyDirectiveSourceKind.TargetSpecificAutoProxy
                ? ProxyDirectiveRouteAnalysisPlanCode
                    .TargetSpecificProxySelected
                : ProxyDirectiveRouteAnalysisPlanCode.ManualProxySelected;
        return new ProxyDirectiveRouteAnalysisPlan(
            ProxyDirectiveRouteAnalysisPlanStatus.AnalyzeProxyEndpoints,
            code,
            selection.SourceKind,
            selection.Status,
            directiveText,
            parsed,
            selection.Status
                == ProxyDirectiveSourceSelectionStatus.SelectedWithWarnings
                    ? "유효한 프록시 후보만 순서대로 분석할 수 있지만 제외된 원문 구간이 있으므로 전체 경로 비교는 불완전할 수 있습니다."
                    : "선택된 프록시 후보를 사용자가 명시적으로 실행할 때만 DNS·Windows 로컬 경로 분석에 전달할 수 있습니다.");
    }

    private static ProxyDirectiveRouteAnalysisPlan CreateDirectPlan(
        ProxyDirectiveSourceSelectionResult selection)
    {
        ProxyDirectiveParseResult? parsed = selection.ParseResult;
        string directiveText = selection.SelectedDirectiveText
            ?? string.Empty;
        bool consistent = directiveText.Length > 0
            && parsed is not null
            && !parsed.HasProxyEndpoint
            && parsed.Directives.Any(directive => directive.IsDirect)
            && !parsed.Issues.Any(issue =>
                issue.Severity == ProxyDirectiveIssueSeverity.Error);
        if (!consistent)
        {
            return CreateInconsistentPlan(selection);
        }

        ProxyDirectiveRouteAnalysisPlanCode code = selection.SourceKind
            == ProxyDirectiveSourceKind.TargetSpecificAutoProxy
                ? ProxyDirectiveRouteAnalysisPlanCode.TargetSpecificDirect
                : ProxyDirectiveRouteAnalysisPlanCode.ManualDirect;
        return new ProxyDirectiveRouteAnalysisPlan(
            ProxyDirectiveRouteAnalysisPlanStatus.DirectOnly,
            code,
            selection.SourceKind,
            selection.Status,
            directiveText,
            parsed,
            "선택 결과가 DIRECT-only이므로 프록시 엔드포인트 DNS·Windows 경로 조회를 수행하지 않습니다.");
    }

    private static ProxyDirectiveRouteAnalysisPlan
        CreateInconsistentPlan(
            ProxyDirectiveSourceSelectionResult selection) =>
        new(
            ProxyDirectiveRouteAnalysisPlanStatus.Blocked,
            ProxyDirectiveRouteAnalysisPlanCode
                .InconsistentSelectionResult,
            selection.SourceKind,
            selection.Status,
            directiveText: null,
            selection.ParseResult,
            "선택 상태·원문·파싱 결과의 내부 계약이 일치하지 않아 DNS·프록시 엔드포인트 경로 조회를 차단했습니다.");
}
