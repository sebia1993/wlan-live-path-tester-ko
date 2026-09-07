using WlanLivePathTester.Core.Models;

namespace WlanLivePathTester.Core.Security;

public sealed record RedirectValidationResult(bool IsAllowed, Uri? Destination, string? ErrorCode, string Message);

public static class RedirectTargetValidator
{
    public static RedirectValidationResult Evaluate(Uri current, string? location, NetworkPathKind pathKind)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (!Enum.IsDefined(pathKind) || !NetworkInputBoundary.IsHttpUri(current, out _))
            return Denied("REDIRECT_ORIGIN_INVALID", "최초 URL 또는 경로 종류가 올바르지 않습니다.");
        if (string.IsNullOrEmpty(location) || location.All(character => character == ' '))
            return Denied("REDIRECT_LOCATION_MISSING", "리다이렉트 응답에 Location 헤더가 없습니다.");
        if (!NetworkInputBoundary.TryUriReference(location, out string reference, out string rawError))
            return Denied("REDIRECT_RAW_INPUT_INVALID", rawError);
        if (!Uri.TryCreate(current, reference, out Uri? destination) || !destination.IsAbsoluteUri)
            return Denied("REDIRECT_URL_INVALID", "리다이렉트 URL을 해석할 수 없습니다.");
        if (destination.Scheme is not ("http" or "https"))
            return Denied("REDIRECT_SCHEME_DENIED", "리다이렉트 대상은 HTTP 또는 HTTPS만 허용합니다.");
        if (!string.IsNullOrEmpty(destination.UserInfo))
            return Denied("REDIRECT_USERINFO_DENIED", "리다이렉트 URL에 사용자 정보가 포함되어 있습니다.");
        if (reference.Contains('#', StringComparison.Ordinal) || !string.IsNullOrEmpty(destination.Fragment))
            return Denied("REDIRECT_FRAGMENT_DENIED", "리다이렉트 URL fragment는 허용하지 않습니다.");
        if (!NetworkInputBoundary.IsHttpUri(destination, out string resolvedError))
            return Denied("REDIRECT_URL_INVALID", resolvedError);
        if (pathKind == NetworkPathKind.External && current.Scheme == "https" && destination.Scheme == "http")
            return Denied("REDIRECT_HTTPS_DOWNGRADE", "외부 HTTPS 요청을 HTTP로 낮추는 리다이렉트는 차단합니다.");

        MeasurementTargetDefinition synthetic = new(
            Name: "리다이렉트 대상", Url: destination.AbsoluteUri, PathKind: pathKind,
            RequireProxy: pathKind == NetworkPathKind.External,
            RequireDirect: pathKind == NetworkPathKind.Internal,
            MaxBytes: 1024 * 1024, TimeoutSeconds: 30, Streams: 1, MaxRedirects: 0);
        // This checks destination safety, not membership using invented limits.
        // ExecuteChain separately applies TargetHostPolicy to the ORIGINAL
        // approved target and its actual limits/redirect allowlist on every hop.
        IReadOnlyList<string> errors = TargetValidator.ValidateDefinition(synthetic);
        if (errors.Count > 0) return Denied("REDIRECT_TARGET_DENIED", string.Join(" ", errors));
        return new(true, destination, null, "리다이렉트 대상이 보안 검사를 통과했습니다.");
    }

    private static RedirectValidationResult Denied(string code, string message) => new(false, null, code, message);
}
