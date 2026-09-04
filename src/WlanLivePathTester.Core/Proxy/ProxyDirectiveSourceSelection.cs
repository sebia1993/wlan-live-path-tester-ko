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
        RedactedDisplay =
            $"{Status} · {SourceKind} · {Code} · 프록시 후보 {ProxyEndpointCount}개 · DIRECT {DirectDirectiveCount}개";
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

        return new ProxyDirectiveSourceSelectionResult(
            ProxyDirectiveSourceSelectionStatus.Unavailable,
            ProxyDirectiveSourceKind.None,
            ProxyDirectiveSourceSelectionCode.NoAvailableDirective,
            selectedDirectiveText: null,
            parseResult: null,
            "대상별 PAC/WPAD 판정을 수행하지 않았고 사용할 수 있는 수동 프록시 설정도 확인되지 않았습니다. DIRECT로 추정하지 않습니다.");
    }

    private static ProxyDirectiveSourceSelectionResult
        SelectTargetSpecific(
            bool targetDecisionIsDirect,
            string? targetSpecificDirective)
    {
        string value = (targetSpecificDirective ?? string.Empty).Trim();
        if (targetDecisionIsDirect)
        {
            if (value.Length > 0)
            {
                ProxyDirectiveParseResult supplied =
                    ProxyRouteDirectiveParser.Parse(value);
                if (supplied.HasProxyEndpoint)
                {
                    return InvalidTarget(
                        supplied,
                        "대상별 판정은 DIRECT라고 표시됐지만 프록시 후보 문자열도 함께 제공돼 결과가 서로 모순됩니다. 수동 프록시로 대체하지 않습니다.");
                }

                if (supplied.Status
                    == ProxyDirectiveParseStatus.InvalidInput)
                {
                    return InvalidTarget(
                        supplied,
                        "대상별 DIRECT 판정과 함께 제공된 지시문을 안전하게 해석하지 못했습니다. 수동 프록시로 대체하지 않습니다.");
                }
            }

            ProxyDirectiveParseResult direct =
                ProxyRouteDirectiveParser.Parse("DIRECT");
            return new ProxyDirectiveSourceSelectionResult(
                ProxyDirectiveSourceSelectionStatus.Direct,
                ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
                ProxyDirectiveSourceSelectionCode.TargetSpecificDirect,
                selectedDirectiveText: "DIRECT",
                direct,
                "대상별 PAC/WPAD 판정이 DIRECT이므로 프록시 엔드포인트를 추정하거나 수동 프록시로 대체하지 않습니다.");
        }

        ProxyDirectiveParseResult parsed =
            ProxyRouteDirectiveParser.Parse(value);
        if (!parsed.HasProxyEndpoint)
        {
            return InvalidTarget(
                parsed,
                parsed.HasDirectFallback
                    ? "대상별 판정이 프록시 경로로 표시됐지만 DIRECT만 확인돼 결과가 서로 모순됩니다."
                    : "대상별 PAC/WPAD 판정에서 사용할 수 있는 프록시 엔드포인트를 확인하지 못했습니다.");
        }

        ProxyDirectiveSourceSelectionStatus status = parsed.Status
            == ProxyDirectiveParseStatus.Success
                ? ProxyDirectiveSourceSelectionStatus.Selected
                : ProxyDirectiveSourceSelectionStatus
                    .SelectedWithWarnings;
        return new ProxyDirectiveSourceSelectionResult(
            status,
            ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            ProxyDirectiveSourceSelectionCode.TargetSpecificProxy,
            selectedDirectiveText: value,
            parsed,
            status == ProxyDirectiveSourceSelectionStatus.Selected
                ? $"대상별 PAC/WPAD 판정의 프록시 후보 {parsed.Directives.Count(directive => !directive.IsDirect)}개를 선택했습니다."
                : $"대상별 PAC/WPAD 판정에서 유효한 프록시 후보 {parsed.Directives.Count(directive => !directive.IsDirect)}개를 선택했지만 제외된 구간이 있습니다.");
    }

    private static ProxyDirectiveSourceSelectionResult SelectManual(
        string? manualProxyDirective)
    {
        string value = (manualProxyDirective ?? string.Empty).Trim();
        ProxyDirectiveParseResult parsed =
            ProxyRouteDirectiveParser.Parse(value);
        if (!parsed.HasUsableDirective)
        {
            return new ProxyDirectiveSourceSelectionResult(
                ProxyDirectiveSourceSelectionStatus.Invalid,
                ProxyDirectiveSourceKind.ManualProxyConfiguration,
                ProxyDirectiveSourceSelectionCode
                    .ManualConfigurationInvalid,
                selectedDirectiveText: null,
                parsed,
                "수동 프록시가 설정됨으로 표시됐지만 안전하게 사용할 수 있는 프록시 또는 DIRECT 지시문을 확인하지 못했습니다. DIRECT로 추정하지 않습니다.");
        }

        if (!parsed.HasProxyEndpoint)
        {
            return new ProxyDirectiveSourceSelectionResult(
                ProxyDirectiveSourceSelectionStatus.Direct,
                ProxyDirectiveSourceKind.ManualProxyConfiguration,
                ProxyDirectiveSourceSelectionCode.ManualDirect,
                selectedDirectiveText: "DIRECT",
                parsed,
                "수동 설정 문자열에서 DIRECT만 확인됐습니다. 프록시 엔드포인트를 추정하지 않습니다.");
        }

        ProxyDirectiveSourceSelectionStatus status = parsed.Status
            == ProxyDirectiveParseStatus.Success
                ? ProxyDirectiveSourceSelectionStatus.Selected
                : ProxyDirectiveSourceSelectionStatus
                    .SelectedWithWarnings;
        return new ProxyDirectiveSourceSelectionResult(
            status,
            ProxyDirectiveSourceKind.ManualProxyConfiguration,
            ProxyDirectiveSourceSelectionCode.ManualProxy,
            selectedDirectiveText: value,
            parsed,
            status == ProxyDirectiveSourceSelectionStatus.Selected
                ? $"수동 프록시 설정의 후보 {parsed.Directives.Count(directive => !directive.IsDirect)}개를 선택했습니다."
                : $"수동 프록시 설정에서 유효한 후보 {parsed.Directives.Count(directive => !directive.IsDirect)}개를 선택했지만 제외된 구간이 있습니다.");
    }

    private static ProxyDirectiveSourceSelectionResult InvalidTarget(
        ProxyDirectiveParseResult parsed,
        string message) =>
        new(
            ProxyDirectiveSourceSelectionStatus.Invalid,
            ProxyDirectiveSourceKind.TargetSpecificAutoProxy,
            ProxyDirectiveSourceSelectionCode.TargetDecisionInvalid,
            selectedDirectiveText: null,
            parsed,
            message + " 대상별 판정이 수동 설정보다 우선하므로 자동 fallback을 수행하지 않았습니다.");
}
