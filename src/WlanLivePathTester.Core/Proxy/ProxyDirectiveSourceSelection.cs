using System.Diagnostics;
using System.Text.Json.Serialization;

namespace WlanLivePathTester.Core.Proxy;

public enum ProxyDirectiveSourceKind
{
    None,
    TargetSpecificAutoProxy,
    ManualProxyConfiguration
}

public enum ProxyDirectiveSourceSelectionStatus
{
    Selected,
    SelectedWithWarnings,
    Direct,
    Unavailable,
    Invalid
}

public enum ProxyDirectiveSourceSelectionCode
{
    TargetSpecificProxy,
    TargetSpecificDirect,
    ManualProxy,
    ManualDirect,
    TargetDecisionInvalid,
    ManualConfigurationInvalid,
    NoAvailableDirective
}

[DebuggerDisplay("{RedactedDisplay,nq}")]
public sealed class ProxyDirectiveSourceSelectionResult
{
    internal ProxyDirectiveSourceSelectionResult(
        ProxyDirectiveSourceSelectionStatus status,
        ProxyDirectiveSourceKind sourceKind,
        ProxyDirectiveSourceSelectionCode code,
        string? selectedDirectiveText,
        ProxyDirectiveParseResult? parseResult,
        string message)
    {
        Status = status;
        SourceKind = sourceKind;
        Code = code;
        SelectedDirectiveText = selectedDirectiveText;
        ParseResult = parseResult;
        Message = message;
        ProxyEndpointCount = parseResult?.Directives.Count(
            directive => !directive.IsDirect) ?? 0;
        DirectDirectiveCount = parseResult?.Directives.Count(
            directive => directive.IsDirect) ?? 0;
        HasDirectFallback = ProxyEndpointCount > 0
            && DirectDirectiveCount > 0;
        HasParseErrors = parseResult?.Issues.Any(issue =>
            issue.Severity == ProxyDirectiveIssueSeverity.Error) == true;
        RedactedDisplay =
            $"{Status} · {SourceKind} · {Code} · 프록시 후보 {ProxyEndpointCount}개 · DIRECT {DirectDirectiveCount}개 · 파싱 오류 {(HasParseErrors ? "있음" : "없음")}";
    }

    public ProxyDirectiveSourceSelectionStatus Status { get; }

    public ProxyDirectiveSourceKind SourceKind { get; }

    public ProxyDirectiveSourceSelectionCode Code { get; }

    [JsonIgnore]
    public string? SelectedDirectiveText { get; }

    public ProxyDirectiveParseResult? ParseResult { get; }

    public int ProxyEndpointCount { get; }

    public int DirectDirectiveCount { get; }

    public bool HasDirectFallback { get; }

    public bool HasParseErrors { get; }

    public string Message { get; }

    public string RedactedDisplay { get; }

    public bool HasUsableSelection => Status is
        ProxyDirectiveSourceSelectionStatus.Selected
            or ProxyDirectiveSourceSelectionStatus.SelectedWithWarnings
            or ProxyDirectiveSourceSelectionStatus.Direct;

    public override string ToString() => RedactedDisplay;
}

public static class ProxyDirectiveSourceSelectionPolicy
{
    public static ProxyDirectiveSourceSelectionResult Select(
        bool targetDecisionWasEvaluated,
        bool targetDecisionIsDirect,
        string? targetSpecificDirective,
        bool manualProxyConfigured,
        string? manualProxyDirective)
    {
        if (targetDecisionWasEvaluated)
        {
            return SelectTargetSpecific(
                targetDecisionIsDirect,
                targetSpecificDirective);
        }

        if (manualProxyConfigured)
        {
            return SelectManual(manualProxyDirective);
        }

        return Unavailable(
            "대상별 PAC/WPAD 판정을 수행하지 않았고 사용할 수 있는 수동 프록시 설정도 확인되지 않았습니다. DIRECT로 추정하지 않습니다.");
    }

    internal static ProxyDirectiveSourceSelectionResult
        InvalidTargetRead(string message) =>
        InvalidTarget(parseResult: null, message);

    internal static ProxyDirectiveSourceSelectionResult
        InvalidManualRead(string message) =>
        InvalidManual(parseResult: null, message);

    private static ProxyDirectiveSourceSelectionResult
        SelectTargetSpecific(
            bool targetDecisionIsDirect,
            string? targetSpecificDirective)
    {
        // Only parse the authoritative source, and validate it before Trim.
        // Otherwise control-only DIRECT input could become a canonical DIRECT.
        ProxyDirectiveParseResult rawParsed =
            ProxyRouteDirectiveParser.Parse(targetSpecificDirective);
        if (rawParsed.Status == ProxyDirectiveParseStatus.InvalidInput)
        {
            return InvalidTarget(
                rawParsed,
                "대상별 프록시 판정 원문을 안전하게 해석하지 못했습니다.");
        }

        string value = (targetSpecificDirective ?? string.Empty).Trim();
        if (targetDecisionIsDirect)
        {
            if (value.Length == 0)
            {
                ProxyDirectiveParseResult canonicalDirect =
                    ProxyRouteDirectiveParser.Parse("DIRECT");
                return new ProxyDirectiveSourceSelectionResult(
                    ProxyDirectiveSourceSelectionStatus.Direct,
                    ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
                    ProxyDirectiveSourceSelectionCode.TargetSpecificDirect,
                    selectedDirectiveText: "DIRECT",
                    canonicalDirect,
                    "대상별 PAC/WPAD 판정이 DIRECT이므로 프록시 엔드포인트를 추정하거나 수동 프록시로 대체하지 않습니다.");
            }

            ProxyDirectiveParseResult parsed = rawParsed;
            bool consistent = parsed.HasUsableDirective
                && parsed.Directives.Any(directive => directive.IsDirect)
                && !parsed.HasProxyEndpoint
                && !HasErrors(parsed);
            if (!consistent)
            {
                return InvalidTarget(
                    parsed,
                    parsed.HasProxyEndpoint
                        ? "대상별 판정은 DIRECT인데 프록시 후보도 함께 제공돼 결과가 서로 모순됩니다."
                        : "대상별 DIRECT 판정과 함께 제공된 지시문을 안전하고 일관되게 해석하지 못했습니다.");
            }

            return new ProxyDirectiveSourceSelectionResult(
                ProxyDirectiveSourceSelectionStatus.Direct,
                ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
                ProxyDirectiveSourceSelectionCode.TargetSpecificDirect,
                selectedDirectiveText: value,
                parsed,
                "대상별 PAC/WPAD 판정에서 DIRECT만 확인했습니다. 프록시 엔드포인트를 추정하거나 수동 프록시로 대체하지 않습니다.");
        }

        ProxyDirectiveParseResult targetParsed = rawParsed;
        if (!targetParsed.HasProxyEndpoint)
        {
            return InvalidTarget(
                targetParsed,
                targetParsed.Directives.Any(directive => directive.IsDirect)
                    ? "대상별 판정은 프록시인데 DIRECT만 확인돼 결과가 서로 모순됩니다."
                    : "대상별 PAC/WPAD 판정에서 사용할 수 있는 프록시 엔드포인트를 확인하지 못했습니다.");
        }

        ProxyDirectiveSourceSelectionStatus targetStatus =
            HasErrors(targetParsed)
                ? ProxyDirectiveSourceSelectionStatus.SelectedWithWarnings
                : ProxyDirectiveSourceSelectionStatus.Selected;
        return new ProxyDirectiveSourceSelectionResult(
            targetStatus,
            ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            ProxyDirectiveSourceSelectionCode.TargetSpecificProxy,
            selectedDirectiveText: value,
            targetParsed,
            targetStatus == ProxyDirectiveSourceSelectionStatus.Selected
                ? $"대상별 PAC/WPAD 판정의 프록시 후보 {CountProxyEndpoints(targetParsed)}개를 선택했습니다."
                : $"대상별 PAC/WPAD 판정에서 유효한 프록시 후보 {CountProxyEndpoints(targetParsed)}개를 선택했지만 제외된 구간이 있습니다.");
    }

    private static ProxyDirectiveSourceSelectionResult SelectManual(
        string? manualProxyDirective)
    {
        ProxyDirectiveParseResult parsed =
            ProxyRouteDirectiveParser.Parse(manualProxyDirective);
        if (!parsed.HasUsableDirective)
        {
            return InvalidManual(
                parsed,
                "수동 프록시가 설정됨으로 표시됐지만 안전하게 사용할 수 있는 프록시 또는 DIRECT 지시문을 확인하지 못했습니다.");
        }

        // The parser has already checked the original length and characters.
        string value = (manualProxyDirective ?? string.Empty).Trim();
        bool hasErrors = HasErrors(parsed);
        if (!parsed.HasProxyEndpoint)
        {
            if (hasErrors)
            {
                return InvalidManual(
                    parsed,
                    "수동 설정에 DIRECT와 해석할 수 없는 구간이 함께 있어 DIRECT-only 정책으로 축소하지 않았습니다.");
            }

            return new ProxyDirectiveSourceSelectionResult(
                ProxyDirectiveSourceSelectionStatus.Direct,
                ProxyDirectiveSourceKind.ManualProxyConfiguration,
                ProxyDirectiveSourceSelectionCode.ManualDirect,
                selectedDirectiveText: value,
                parsed,
                "수동 설정 문자열에서 DIRECT만 확인했습니다. 범위 정보를 유지하며 프록시 엔드포인트를 추정하지 않습니다.");
        }

        ProxyDirectiveSourceSelectionStatus status = hasErrors
            ? ProxyDirectiveSourceSelectionStatus.SelectedWithWarnings
            : ProxyDirectiveSourceSelectionStatus.Selected;
        return new ProxyDirectiveSourceSelectionResult(
            status,
            ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxyDirectiveSourceSelectionCode.ManualProxy,
            selectedDirectiveText: value,
            parsed,
            status == ProxyDirectiveSourceSelectionStatus.Selected
                ? $"수동 프록시 설정의 후보 {CountProxyEndpoints(parsed)}개를 선택했습니다."
                : $"수동 프록시 설정에서 유효한 후보 {CountProxyEndpoints(parsed)}개를 선택했지만 제외된 구간이 있습니다.");
    }

    private static ProxyDirectiveSourceSelectionResult InvalidTarget(
        ProxyDirectiveParseResult? parseResult,
        string message) =>
        new(
            ProxyDirectiveSourceSelectionStatus.Invalid,
            ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            ProxyDirectiveSourceSelectionCode.TargetDecisionInvalid,
            selectedDirectiveText: null,
            parseResult,
            message
            + " 대상별 판정이 수동 설정보다 우선하므로 자동 fallback을 수행하지 않았습니다.");

    private static ProxyDirectiveSourceSelectionResult InvalidManual(
        ProxyDirectiveParseResult? parseResult,
        string message) =>
        new(
            ProxyDirectiveSourceSelectionStatus.Invalid,
            ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxyDirectiveSourceSelectionCode.ManualConfigurationInvalid,
            selectedDirectiveText: null,
            parseResult,
            message
            + " DIRECT 또는 임의 프록시로 추정하지 않습니다.");

    private static ProxyDirectiveSourceSelectionResult Unavailable(
        string message) =>
        new(
            ProxyDirectiveSourceSelectionStatus.Unavailable,
            ProxyDirectiveSourceKind.None,
            ProxyDirectiveSourceSelectionCode.NoAvailableDirective,
            selectedDirectiveText: null,
            parseResult: null,
            message);

    private static bool HasErrors(ProxyDirectiveParseResult result) =>
        result.Issues.Any(issue =>
            issue.Severity == ProxyDirectiveIssueSeverity.Error);

    private static int CountProxyEndpoints(
        ProxyDirectiveParseResult result) =>
        result.Directives.Count(directive => !directive.IsDirect);
}
