using System.Text.RegularExpressions;
using WlanLivePathTester.Core.NetworkEnvironment;
using WlanLivePathTester.Core.Routing;

namespace WlanLivePathTester.Core.Reporting;

public static partial class InternalProxyRouteComparisonRunReportSnapshotMapper
{
    private const int MaximumNarrativeLength = 4096;

    public static InternalProxyRouteComparisonRunReportSnapshot FromResult(
        InternalProxyRouteComparisonRunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ReportFinding finding = InternalProxyRouteComparisonRunFindingMapper.FromResult(result);
        return new InternalProxyRouteComparisonRunReportSnapshot(
            CompletedAt: result.CompletedAt,
            RunStatus: SafeEnum(result.Status),
            ProxySourceKind: SafeEnum(result.ProxySourceKind),
            ProxySelectionStatus: SafeEnum(result.ProxySelectionStatus),
            ProxyPlanStatus: SafeEnum(result.ProxyPlanStatus),
            ProxyPlanCode: SafeEnum(result.ProxyPlanCode),
            ProxyExecutionStatus: SafeOptionalStatus(result.ProxyExecutionStatus),
            ProxyEndpointSourceKind: SafeEnum(result.ProxyEndpointSourceKind),
            ProxyDecision: SafeEnum(result.ProxyDecision),
            TargetScheme: SafeTargetScheme(result.TargetScheme),
            InternalRouteStatus: SafeOptionalStatus(result.InternalRouteStatus),
            ProxyRouteStatus: SafeOptionalStatus(result.ProxyRouteStatus),
            ParsedProxyEndpointCount: Count(result.ParsedProxyEndpointCount),
            ApplicableProxyEndpointCount: Count(result.ApplicableProxyEndpointCount),
            AnalyzedProxyEndpointCount: Count(result.AnalyzedProxyEndpointCount),
            SuccessfulProxyEndpointCount: Count(result.SuccessfulProxyEndpointCount),
            DistinctProxyInterfaceCount: Count(result.DistinctProxyInterfaceCount),
            DirectPresent: result.DirectPresent,
            DirectIsPrimary: result.DirectIsPrimary,
            DirectFallback: result.DirectFallback,
            ProxyParseErrorsPresent: result.ProxyParseErrorsPresent,
            ExpectedWlanIdentityAvailable: result.ExpectedWlanIdentityAvailable,
            InternalRouteReadPerformed: result.InternalRouteReadPerformed,
            ProxyRouteAnalysisPerformed: result.ProxyRouteAnalysisPerformed,
            OperationCompleted: result.OperationCompleted,
            HasComparableResult: result.HasComparableResult,
            Comparison: MapComparison(result.Comparison),
            ProxyEntries: MapProxyEntries(result.ProxyExecution?.Analysis),
            Finding: new InternalProxyRouteComparisonReportFinding(
                SafeCode(finding.Code), SafeSeverity(finding.Severity),
                SafeNarrative(finding.Title), SafeNarrative(finding.Evidence),
                SafeNarrative(finding.Interpretation), SafeNarrative(finding.Limitation),
                SafeNarrative(finding.NextStep)));
    }

    private static InternalProxyRouteComparisonReportComparison? MapComparison(
        InternalProxyRouteComparisonResult? comparison)
    {
        if (comparison is null) return null;
        return new InternalProxyRouteComparisonReportComparison(
            EvaluatedAt: comparison.EvaluatedAt,
            Status: SafeEnum(comparison.Status),
            Relation: SafeEnum(comparison.Relation),
            Code: SafeEnum(comparison.Code),
            InternalRouteStatus: SafeOptionalStatus(comparison.InternalRouteStatus),
            ProxyExecutionStatus: SafeOptionalStatus(comparison.ProxyExecutionStatus),
            ProxyAnalysisStatus: SafeOptionalStatus(comparison.ProxyAnalysisStatus),
            ProxySourceKind: SafeOptionalStatus(comparison.ProxySourceKind),
            ProxyPlanCode: SafeOptionalStatus(comparison.ProxyPlanCode),
            InternalInterfaceFingerprint: SafeFingerprint(comparison.InternalInterfaceFingerprint),
            InternalInterfaceCategory: SafeCategory(comparison.InternalInterfaceCategory),
            ProxyInterfaceFingerprints: comparison.ProxyInterfaceFingerprints
                .Select(SafeFingerprint).Where(value => value is not null).Select(value => value!)
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ProxyInterfaceCategories: comparison.ProxyInterfaceCategories
                .Where(value => Enum.IsDefined(value)).Select(value => value.ToString())
                .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
            ProxyApplicableEndpointCount: Count(comparison.ProxyApplicableEndpointCount),
            ProxyAnalyzedEndpointCount: Count(comparison.ProxyAnalyzedEndpointCount),
            ProxySuccessfulEndpointCount: Count(comparison.ProxySuccessfulEndpointCount),
            ProxyDistinctInterfaceCount: Count(comparison.ProxyDistinctInterfaceCount),
            ProxySkippedAfterDirectCount: Count(comparison.ProxySkippedAfterDirectCount),
            ProxyDirectPresent: comparison.ProxyDirectPresent,
            ProxyDirectIsPrimary: comparison.ProxyDirectIsPrimary,
            ProxyDirectFallbackPresent: comparison.ProxyDirectFallbackPresent,
            ProxyParseErrorsPresent: comparison.ProxyParseErrorsPresent,
            ExactIdentityComparisonPerformed: comparison.ExactIdentityComparisonPerformed,
            HasCompleteComparableEvidence: comparison.HasCompleteComparableEvidence);
    }

    private static IReadOnlyList<InternalProxyRouteComparisonReportProxyEntry> MapProxyEntries(
        ProxyEndpointRouteAnalysisResult? analysis)
    {
        if (analysis is null) return Array.Empty<InternalProxyRouteComparisonReportProxyEntry>();
        // Do not copy endpoint labels, warnings, identities or messages.
        return analysis.Endpoints.OrderBy(endpoint => Math.Max(0, endpoint.Sequence))
            .Select(endpoint => new InternalProxyRouteComparisonReportProxyEntry(
                Sequence: Math.Max(0, endpoint.Sequence),
                AppliesToScheme: SafeEndpointScheme(endpoint.AppliesToScheme),
                Transport: SafeEnum(endpoint.Transport),
                Port: endpoint.Port is >= 1 and <= 65535 ? endpoint.Port : null,
                HostFingerprint: SafeFingerprint(endpoint.HostFingerprint),
                RouteStatus: SafeEnum(endpoint.RouteStatus),
                WlanCorrelationStatus: SafeEnum(endpoint.WlanCorrelationStatus),
                SelectedInterfaceFingerprint: SafeFingerprint(endpoint.SelectedInterfaceFingerprint),
                SelectedInterfaceCategory: SafeCategory(endpoint.SelectedInterfaceCategory),
                SelectedInterfaceIsVirtual: endpoint.SelectedInterfaceIsVirtual,
                SelectedInterfaceIsVpn: endpoint.SelectedInterfaceIsVpn,
                SelectedInterfaceIsUp: endpoint.SelectedInterfaceIsUp,
                SelectedInterfaceHasDefaultGateway: endpoint.SelectedInterfaceHasDefaultGateway,
                ResolvedAddressCount: Count(endpoint.ResolvedAddressCount),
                SuccessfulAddressCount: Count(endpoint.SuccessfulAddressCount),
                FailedAddressCount: Count(endpoint.FailedAddressCount))).ToArray();
    }

    private static string SafeEnum<TEnum>(TEnum value) where TEnum : struct, Enum =>
        Enum.IsDefined(value) ? value.ToString() : "Unknown";
    private static string SafeOptionalStatus<TEnum>(TEnum? value) where TEnum : struct, Enum =>
        value.HasValue ? SafeEnum(value.Value) : "None";
    private static string SafeCategory(NetworkAdapterCategory? value) =>
        value.HasValue && Enum.IsDefined(value.Value) ? value.Value.ToString() : string.Empty;
    private static string SafeTargetScheme(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "http" => "http", "https" => "https", _ => "none"
        };
    private static string SafeEndpointScheme(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "" or "all" => "all", "http" => "http", "https" => "https", _ => "unknown"
        };
    private static string? SafeFingerprint(string? value)
    {
        string candidate = (value ?? string.Empty).Trim().ToLowerInvariant();
        return candidate.Length == 10 && candidate.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f') ? candidate : null;
    }
    private static string SafeCode(string? value)
    {
        string candidate = (value ?? string.Empty).Trim().ToUpperInvariant();
        return candidate.Length is >= 1 and <= 96 && candidate.All(character =>
            character is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_') ? candidate : "INVALID_CODE";
    }
    private static string SafeSeverity(string? value) => (value ?? string.Empty).Trim() switch
    {
        "Information" => "Information", "Warning" => "Warning", "Error" => "Error", _ => "Warning"
    };
    private static string SafeNarrative(string? value)
    {
        string sanitized = SensitiveDataRedactor.RedactText(value) ?? string.Empty;
        sanitized = GuidRegex().Replace(sanitized, "[인터페이스 ID 마스킹됨]");
        sanitized = DnsNameRegex().Replace(sanitized, "[호스트 마스킹됨]");
        sanitized = sanitized.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
        return sanitized.Length == 0 ? "설명 없음"
            : sanitized.Length <= MaximumNarrativeLength ? sanitized
            : sanitized[..(MaximumNarrativeLength - 3)] + "...";
    }
    private static int Count(int value) => Math.Max(0, value);
    [GeneratedRegex(@"(?i)(?<![0-9a-f])\{?[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}\}?(?![0-9a-f])")]
    private static partial Regex GuidRegex();
    [GeneratedRegex(@"(?i)(?<![a-z0-9-])(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}(?![a-z0-9-])")]
    private static partial Regex DnsNameRegex();
}
