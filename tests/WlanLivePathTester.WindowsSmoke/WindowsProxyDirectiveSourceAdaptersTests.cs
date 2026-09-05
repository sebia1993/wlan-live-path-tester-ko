using System.Runtime.CompilerServices;
using System.Text.Json;
using WlanLivePathTester.Core.Proxy;
using WlanLivePathTester.Windows.Proxy;

namespace WlanLivePathTester.WindowsSmoke;

internal static class WindowsProxyDirectiveSourceAdaptersTests
{
    private static readonly Uri TargetUri = new(
        "https://download.example.invalid/file.bin");

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        FactoryPassesTheSameArgumentsToExistingReaderDelegates();
        DelegateAdaptersRejectNullHandlers();
        DelegateReaderResultsDoNotSerializeRawSettings();
        Console.WriteLine(
            "PASS delegate adapters for existing Windows proxy readers");
    }

    private static void
        FactoryPassesTheSameArgumentsToExistingReaderDelegates()
    {
        const string manualDirective =
            "PROXY adapter-manual.example.invalid:3128";
        const string targetDirective =
            "PROXY adapter-target.example.invalid:8080; DIRECT";
        const string pacUrl =
            "https://adapter-pac.example.invalid/proxy.pac";
        using CancellationTokenSource cancellation = new();
        int manualCalls = 0;
        int targetCalls = 0;
        int analyzerCalls = 0;
        CancellationToken manualToken = default;
        CancellationToken targetToken = default;
        CancellationToken analyzerToken = default;
        WindowsManualProxyConfigurationReadResult? targetManual = null;
        Uri? targetUri = null;

        WindowsProxyDirectiveSourceExecutionCoordinator coordinator =
            WindowsProxyDirectiveSourceCoordinatorFactory.Create(
                token =>
                {
                    manualCalls++;
                    manualToken = token;
                    return Task.FromResult(
                        new WindowsManualProxyConfigurationReadResult(
                            ProxyDirectiveSourceReadStatus.Success,
                            ManualProxyConfigured: true,
                            ManualProxyDirective: manualDirective,
                            AutoDetectEnabled: true,
                            PacConfigured: true,
                            PacUrl: pacUrl));
                },
                (uri, manual, token) =>
                {
                    targetCalls++;
                    targetUri = uri;
                    targetManual = manual;
                    targetToken = token;
                    return Task.FromResult(
                        new WindowsTargetProxyDecisionReadResult(
                            ProxyDirectiveSourceReadStatus.Success,
                            IsDirect: false,
                            DirectiveText: targetDirective));
                },
                static () => DateTimeOffset.UnixEpoch.AddDays(13));

        WindowsProxyDirectiveSourceExecutionResult<string> result =
            coordinator.ReadAndExecuteAsync(
                    TargetUri,
                    (directive, token) =>
                    {
                        analyzerCalls++;
                        analyzerToken = token;
                        Ensure(directive == targetDirective,
                            "분석기에는 대상별 reader가 반환한 정확한 지시문을 전달해야 합니다.");
                        return Task.FromResult("delegate-analysis");
                    },
                    cancellation.Token)
                .GetAwaiter()
                .GetResult();

        Ensure(manualCalls == 1
               && targetCalls == 1
               && analyzerCalls == 1,
            "기존 reader delegate와 analyzer를 각각 정확히 한 번 호출해야 합니다.");
        Ensure(manualToken == cancellation.Token
               && targetToken == cancellation.Token
               && analyzerToken == cancellation.Token,
            "동일한 사용자 취소 토큰을 전체 경로에 전달해야 합니다.");
        Ensure(targetUri == TargetUri,
            "대상별 reader에 정확한 사용자 대상 URL을 전달해야 합니다.");
        Ensure(targetManual?.ManualProxyDirective == manualDirective
               && targetManual.PacUrl == pacUrl,
            "대상별 reader가 기존 수동 설정·PAC 메모리 값을 사용할 수 있어야 합니다.");
        Ensure(result.Status
               == WindowsProxyDirectiveSourceExecutionStatus.Completed
               && result.Analysis == "delegate-analysis",
            "delegate 기반 기존 reader 연결의 완료 결과를 유지해야 합니다.");
    }

    private static void DelegateAdaptersRejectNullHandlers()
    {
        EnsureThrows<ArgumentNullException>(() =>
            new DelegateWindowsManualProxyConfigurationSource(null!));
        EnsureThrows<ArgumentNullException>(() =>
            new DelegateWindowsTargetProxyDecisionSource(null!));
        EnsureThrows<ArgumentNullException>(() =>
            WindowsProxyDirectiveSourceCoordinatorFactory.Create(
                null!,
                (_, _, _) => Task.FromResult(
                    new WindowsTargetProxyDecisionReadResult(
                        ProxyDirectiveSourceReadStatus.Failed,
                        false,
                        null))));
        EnsureThrows<ArgumentNullException>(() =>
            WindowsProxyDirectiveSourceCoordinatorFactory.Create(
                _ => Task.FromResult(
                    new WindowsManualProxyConfigurationReadResult(
                        ProxyDirectiveSourceReadStatus.Failed,
                        false,
                        null,
                        false,
                        false,
                        null)),
                null!));
    }

    private static void
        DelegateReaderResultsDoNotSerializeRawSettings()
    {
        const string manualDirective =
            "PROXY serialize-adapter-manual.example.invalid:3128";
        const string pacUrl =
            "https://serialize-adapter-pac.example.invalid/proxy.pac";
        const string targetDirective =
            "PROXY serialize-adapter-target.example.invalid:8080";
        WindowsManualProxyConfigurationReadResult manual = new(
            ProxyDirectiveSourceReadStatus.Success,
            true,
            manualDirective,
            true,
            true,
            pacUrl);
        WindowsTargetProxyDecisionReadResult target = new(
            ProxyDirectiveSourceReadStatus.Success,
            false,
            targetDirective);

        string manualJson = JsonSerializer.Serialize(manual);
        string targetJson = JsonSerializer.Serialize(target);
        foreach (string secret in new[]
                 {
                     manualDirective,
                     pacUrl,
                     targetDirective,
                     "serialize-adapter-manual.example.invalid",
                     "serialize-adapter-pac.example.invalid",
                     "serialize-adapter-target.example.invalid"
                 })
        {
            Ensure(!manualJson.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"수동 reader 결과 JSON에 원문이 남았습니다: {secret}");
            Ensure(!targetJson.Contains(
                    secret,
                    StringComparison.OrdinalIgnoreCase),
                $"대상별 reader 결과 JSON에 원문이 남았습니다: {secret}");
        }
    }

    private static void EnsureThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"예상 예외가 발생하지 않았습니다: {typeof(TException).Name}");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
