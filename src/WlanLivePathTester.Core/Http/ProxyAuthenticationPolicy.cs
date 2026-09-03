namespace WlanLivePathTester.Core.Http;

internal enum ProxyAuthenticationChoice
{
    None,
    Negotiate,
    Ntlm
}

internal enum ProxyAuthenticationDecisionStatus
{
    Selected,
    Unsupported,
    WrongTarget
}

internal sealed record ProxyAuthenticationDecision(
    ProxyAuthenticationDecisionStatus Status,
    ProxyAuthenticationChoice Choice,
    uint NativeScheme,
    string Message);

internal static class ProxyAuthenticationPolicy
{
    internal const uint AuthTargetServer = 0;
    internal const uint AuthTargetProxy = 1;

    internal const uint AuthSchemeBasic = 0x00000001;
    internal const uint AuthSchemeNtlm = 0x00000002;
    internal const uint AuthSchemePassport = 0x00000004;
    internal const uint AuthSchemeDigest = 0x00000008;
    internal const uint AuthSchemeNegotiate = 0x00000010;

    internal static ProxyAuthenticationDecision Select(
        uint supportedSchemes,
        uint firstScheme,
        uint authTarget)
    {
        if (authTarget != AuthTargetProxy)
        {
            return new ProxyAuthenticationDecision(
                ProxyAuthenticationDecisionStatus.WrongTarget,
                ProxyAuthenticationChoice.None,
                0,
                "프록시 인증 대상이 아닌 인증 요청은 처리하지 않습니다.");
        }

        if (firstScheme == AuthSchemeNegotiate
            || (supportedSchemes & AuthSchemeNegotiate) != 0)
        {
            return new ProxyAuthenticationDecision(
                ProxyAuthenticationDecisionStatus.Selected,
                ProxyAuthenticationChoice.Negotiate,
                AuthSchemeNegotiate,
                "현재 Windows 사용자 자격 증명으로 Negotiate 인증을 시도합니다.");
        }

        if (firstScheme == AuthSchemeNtlm
            || (supportedSchemes & AuthSchemeNtlm) != 0)
        {
            return new ProxyAuthenticationDecision(
                ProxyAuthenticationDecisionStatus.Selected,
                ProxyAuthenticationChoice.Ntlm,
                AuthSchemeNtlm,
                "현재 Windows 사용자 자격 증명으로 NTLM 인증을 시도합니다.");
        }

        return new ProxyAuthenticationDecision(
            ProxyAuthenticationDecisionStatus.Unsupported,
            ProxyAuthenticationChoice.None,
            0,
            "프록시가 Negotiate 또는 NTLM을 제공하지 않아 자격 증명을 전송하지 않았습니다.");
    }
}
