using System.Net;
using System.Net.Sockets;
using System.Text;
using WlanLivePathTester.Core.Http;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Windows.Http;

namespace WlanLivePathTester.ProxyAuthSmoke;

internal static class Program
{
    private static async Task<int> Main()
    {
        (string Name, Func<Task> Test)[] tests =
        [
            ("Negotiate 우선 선택", SelectsNegotiateBeforeNtlm),
            ("NTLM 단독 선택", SelectsNtlm),
            ("Basic 및 Digest 거부", RejectsBasicAndDigest),
            ("서버 인증 대상 거부", RejectsServerAuthenticationTarget),
            ("407 재시도 상한", EnforcesSingleAuthenticationAttempt),
            ("로컬 DIRECT HEAD 요청", ExecutesLocalDirectHead),
            ("로컬 Basic 프록시 407 거부", RejectsBasicProxyChallenge),
            ("GET 응답 바이트 상한", StopsAtResponseByteLimit)
        ];

        int failures = 0;
        foreach ((string name, Func<Task> test) in tests)
        {
            try
            {
                await test();
                Console.WriteLine($"PASS  {name}");
            }
            catch (Exception exception)
            {
                failures++;
                Console.Error.WriteLine($"FAIL  {name}: {exception.Message}");
            }
        }

        Console.WriteLine($"총 {tests.Length}개, 실패 {failures}개");
        return failures == 0 ? 0 : 1;
    }

    private static Task SelectsNegotiateBeforeNtlm()
    {
        ProxyAuthenticationDecision decision = ProxyAuthenticationPolicy.Select(
            ProxyAuthenticationPolicy.AuthSchemeNtlm
                | ProxyAuthenticationPolicy.AuthSchemeNegotiate,
            ProxyAuthenticationPolicy.AuthSchemeNtlm,
            ProxyAuthenticationPolicy.AuthTargetProxy);

        Assert(decision.Status == ProxyAuthenticationDecisionStatus.Selected,
            "지원되는 프록시 인증을 선택해야 합니다.");
        Assert(decision.Choice == ProxyAuthenticationChoice.Negotiate,
            "Negotiate를 NTLM보다 우선해야 합니다.");
        return Task.CompletedTask;
    }

    private static Task SelectsNtlm()
    {
        ProxyAuthenticationDecision decision = ProxyAuthenticationPolicy.Select(
            ProxyAuthenticationPolicy.AuthSchemeNtlm,
            ProxyAuthenticationPolicy.AuthSchemeNtlm,
            ProxyAuthenticationPolicy.AuthTargetProxy);

        Assert(decision.Status == ProxyAuthenticationDecisionStatus.Selected,
            "NTLM을 선택해야 합니다.");
        Assert(decision.Choice == ProxyAuthenticationChoice.Ntlm,
            "NTLM 선택 결과가 필요합니다.");
        return Task.CompletedTask;
    }

    private static Task RejectsBasicAndDigest()
    {
        ProxyAuthenticationDecision decision = ProxyAuthenticationPolicy.Select(
            ProxyAuthenticationPolicy.AuthSchemeBasic
                | ProxyAuthenticationPolicy.AuthSchemeDigest,
            ProxyAuthenticationPolicy.AuthSchemeBasic,
            ProxyAuthenticationPolicy.AuthTargetProxy);

        Assert(decision.Status == ProxyAuthenticationDecisionStatus.Unsupported,
            "Basic과 Digest만 제공되면 거부해야 합니다.");
        Assert(decision.Choice == ProxyAuthenticationChoice.None,
            "지원하지 않는 인증에는 자격 증명을 선택하지 않아야 합니다.");
        return Task.CompletedTask;
    }

    private static Task RejectsServerAuthenticationTarget()
    {
        ProxyAuthenticationDecision decision = ProxyAuthenticationPolicy.Select(
            ProxyAuthenticationPolicy.AuthSchemeNegotiate,
            ProxyAuthenticationPolicy.AuthSchemeNegotiate,
            ProxyAuthenticationPolicy.AuthTargetServer);

        Assert(decision.Status == ProxyAuthenticationDecisionStatus.WrongTarget,
            "원격 서버 인증은 프록시 인증으로 처리하면 안 됩니다.");
        return Task.CompletedTask;
    }

    private static Task EnforcesSingleAuthenticationAttempt()
    {
        Assert(ProxyAuthenticationPolicy.CanAttempt(0, 1),
            "첫 프록시 인증 시도는 허용해야 합니다.");
        Assert(!ProxyAuthenticationPolicy.CanAttempt(1, 1),
            "한 번 시도한 뒤 반복 407 재시도를 중단해야 합니다.");
        return Task.CompletedTask;
    }

    private static async Task ExecutesLocalDirectHead()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        TcpListener listener = StartListener(out int port);

        try
        {
            Task server = ServeOnceAsync(
                listener,
                request =>
                {
                    Assert(request.StartsWith("HEAD /smoke HTTP/1.1", StringComparison.Ordinal),
                        "DIRECT 요청은 HEAD /smoke여야 합니다.");
                    return "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
                },
                timeout.Token);

            WinHttpRequestResult result = WinHttpRequestExecutor.ExecuteExplicitForSmoke(
                new WinHttpRequestOptions(
                    Url: $"http://127.0.0.1:{port}/smoke",
                    ExpectedPath: NetworkPathKind.Internal,
                    Method: WinHttpRequestMethod.Head,
                    TimeoutMilliseconds: 5000,
                    MaxResponseBytes: 1,
                    RequireExpectedPath: true),
                proxyEndpoint: null);

            await server.WaitAsync(TimeSpan.FromSeconds(10));

            Assert(result.Status == WinHttpRequestStatus.Success,
                $"로컬 HEAD 요청이 성공해야 합니다: {result.Status} / {result.Message}");
            Assert(result.HttpStatusCode == 204,
                "HTTP 204 상태를 기록해야 합니다.");
            Assert(!result.ProxyWasUsed,
                "DIRECT 요청은 프록시 사용으로 표시하면 안 됩니다.");
            Assert(result.AuthenticationAttempts == 0,
                "DIRECT 요청에서 프록시 인증을 시도하면 안 됩니다.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task RejectsBasicProxyChallenge()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        TcpListener listener = StartListener(out int port);

        try
        {
            Task server = ServeOnceAsync(
                listener,
                request =>
                {
                    Assert(request.StartsWith("HEAD ", StringComparison.Ordinal),
                        "합성 프록시는 HEAD 요청을 받아야 합니다.");
                    Assert(request.Contains("example.invalid", StringComparison.OrdinalIgnoreCase),
                        "요청은 합성 대상 호스트를 포함해야 합니다.");
                    Assert(!request.Contains("Proxy-Authorization:", StringComparison.OrdinalIgnoreCase),
                        "Basic 인증 거부 전에는 Proxy-Authorization을 보내면 안 됩니다.");

                    return "HTTP/1.1 407 Proxy Authentication Required\r\n"
                        + "Proxy-Authenticate: Basic realm=\"synthetic\"\r\n"
                        + "Content-Length: 0\r\n"
                        + "Connection: close\r\n\r\n";
                },
                timeout.Token);

            WinHttpRequestResult result = WinHttpRequestExecutor.ExecuteExplicitForSmoke(
                new WinHttpRequestOptions(
                    Url: "http://example.invalid/synthetic.bin",
                    ExpectedPath: NetworkPathKind.External,
                    Method: WinHttpRequestMethod.Head,
                    TimeoutMilliseconds: 5000,
                    MaxResponseBytes: 1,
                    RequireExpectedPath: true),
                proxyEndpoint: $"http://127.0.0.1:{port}");

            await server.WaitAsync(TimeSpan.FromSeconds(10));

            Assert(result.Status == WinHttpRequestStatus.ProxyAuthenticationUnsupported,
                $"Basic 전용 407을 거부해야 합니다: {result.Status} / {result.Message}");
            Assert(result.HttpStatusCode == 407,
                "407 상태를 결과에 기록해야 합니다.");
            Assert(result.ProxyWasUsed,
                "합성 프록시 경유를 기록해야 합니다.");
            Assert(result.AuthenticationMethod == ProxyAuthenticationMethod.None,
                "Basic 전용 프록시에 인증 방법을 선택하면 안 됩니다.");
            Assert(result.AuthenticationAttempts == 0,
                "Basic 전용 프록시에 현재 사용자 자격 증명을 재시도하면 안 됩니다.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task StopsAtResponseByteLimit()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(15));
        TcpListener listener = StartListener(out int port);

        try
        {
            Task server = ServeOnceAsync(
                listener,
                request =>
                {
                    Assert(request.StartsWith("GET /payload HTTP/1.1", StringComparison.Ordinal),
                        "바이트 상한 검증은 GET /payload여야 합니다.");
                    return "HTTP/1.1 200 OK\r\n"
                        + "Content-Length: 16\r\n"
                        + "Connection: close\r\n\r\n"
                        + "0123456789ABCDEF";
                },
                timeout.Token);

            WinHttpRequestResult result = WinHttpRequestExecutor.ExecuteExplicitForSmoke(
                new WinHttpRequestOptions(
                    Url: $"http://127.0.0.1:{port}/payload",
                    ExpectedPath: NetworkPathKind.Internal,
                    Method: WinHttpRequestMethod.Get,
                    TimeoutMilliseconds: 5000,
                    MaxResponseBytes: 8,
                    RequireExpectedPath: true),
                proxyEndpoint: null);

            await server.WaitAsync(TimeSpan.FromSeconds(10));

            Assert(result.Status == WinHttpRequestStatus.ResponseLimitReached,
                $"설정한 8바이트에서 응답 읽기를 중단해야 합니다: {result.Status}");
            Assert(result.BytesReceived == 8,
                "수신 바이트를 8로 기록해야 합니다.");
            Assert(result.ResponseWasTruncated,
                "응답 상한 도달을 표시해야 합니다.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static TcpListener StartListener(out int port)
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return listener;
    }

    private static async Task ServeOnceAsync(
        TcpListener listener,
        Func<string, string> responseFactory,
        CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
        using NetworkStream stream = client.GetStream();
        string request = await ReadHeadersAsync(stream, cancellationToken);
        string response = responseFactory(request);
        byte[] bytes = Encoding.ASCII.GetBytes(response);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<string> ReadHeadersAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        const int maximumHeaderBytes = 64 * 1024;
        List<byte> bytes = new(capacity: 2048);
        byte[] oneByte = new byte[1];

        while (bytes.Count < maximumHeaderBytes)
        {
            int read = await stream.ReadAsync(oneByte, cancellationToken);
            if (read == 0)
            {
                break;
            }

            bytes.Add(oneByte[0]);
            int count = bytes.Count;
            if (count >= 4
                && bytes[count - 4] == (byte)'\r'
                && bytes[count - 3] == (byte)'\n'
                && bytes[count - 2] == (byte)'\r'
                && bytes[count - 1] == (byte)'\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }

        throw new InvalidOperationException("합성 HTTP 요청 헤더를 완전히 읽지 못했습니다.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
