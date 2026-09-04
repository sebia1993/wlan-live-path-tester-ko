using System.Net;
using System.Net.Sockets;
using System.Text;
using WlanLivePathTester.Core.Measurements;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Security;
using WlanLivePathTester.Windows.Http;
using WlanLivePathTester.Windows.Measurements;

namespace WlanLivePathTester.MeasurementSmoke;

internal static class Program
{
    private static async Task<int> Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("Windows measurement smoke test must run on Windows.");
            return 2;
        }

        (string Name, Func<Task> Test)[] tests =
        [
            ("안전한 상대 리다이렉트 허용", AllowsSafeRelativeRedirect),
            ("외부 HTTPS 다운그레이드 차단", RejectsHttpsDowngrade),
            ("외부 로컬 주소 리다이렉트 차단", RejectsExternalLocalRedirect),
            ("내부 DIRECT 리다이렉트·헤더·처리량 측정", MeasuresInternalRedirectHeadersAndThroughput),
            ("외부 프록시 다중 스트림 총량 상한", MeasuresExternalProxyWithTwoStreams),
            ("사전 취소 처리", HandlesPreCanceledMeasurement),
            ("내부 프록시 필수 설정 차단", RejectsInvalidInternalProxySemantics),
            ("WinHTTP 수신 시간 초과", DetectsReceiveTimeout)
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
                Console.Error.WriteLine($"FAIL  {name}: {exception}");
            }
        }

        Console.WriteLine($"Measurement smoke 총 {tests.Length}개, 실패 {failures}개");
        return failures == 0 ? 0 : 1;
    }

    private static Task AllowsSafeRelativeRedirect()
    {
        RedirectValidationResult result = RedirectTargetValidator.Evaluate(
            new Uri("https://example.invalid/start"),
            "/payload?token=synthetic",
            NetworkPathKind.External);

        Assert(result.IsAllowed, result.Message);
        Assert(result.Destination?.AbsolutePath == "/payload",
            "상대 Location을 절대 URL로 해석해야 합니다.");
        return Task.CompletedTask;
    }

    private static Task RejectsHttpsDowngrade()
    {
        RedirectValidationResult result = RedirectTargetValidator.Evaluate(
            new Uri("https://example.invalid/start"),
            "http://example.invalid/payload",
            NetworkPathKind.External);

        Assert(!result.IsAllowed
            && result.ErrorCode == "REDIRECT_HTTPS_DOWNGRADE",
            "외부 HTTPS에서 HTTP로 낮추는 리다이렉트를 차단해야 합니다.");
        return Task.CompletedTask;
    }

    private static Task RejectsExternalLocalRedirect()
    {
        RedirectValidationResult result = RedirectTargetValidator.Evaluate(
            new Uri("https://example.invalid/start"),
            "https://127.0.0.1/payload",
            NetworkPathKind.External);

        Assert(!result.IsAllowed
            && result.ErrorCode == "REDIRECT_TARGET_DENIED",
            "외부 측정이 루프백 주소로 리다이렉트되면 차단해야 합니다.");
        return Task.CompletedTask;
    }

    private static async Task MeasuresInternalRedirectHeadersAndThroughput()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        TcpListener listener = StartListener(out int port);
        const int payloadBytes = 256 * 1024;

        Task server = ServeRequestsAsync(
            listener,
            expectedRequests: 3,
            request =>
            {
                if (request.Method == "HEAD" && request.Target == "/start")
                {
                    return SyntheticResponse.Redirect("/payload");
                }

                if (request.Method == "HEAD" && request.Target == "/payload")
                {
                    return SyntheticResponse.Success(
                        Array.Empty<byte>(),
                        declaredLength: payloadBytes,
                        extraHeaders: new Dictionary<string, string>
                        {
                            ["Age"] = "12",
                            ["X-Cache"] = "HIT"
                        });
                }

                if (request.Method == "GET" && request.Target == "/payload")
                {
                    return SyntheticResponse.Success(
                        new byte[payloadBytes],
                        declaredLength: payloadBytes,
                        extraHeaders: new Dictionary<string, string>
                        {
                            ["Age"] = "12",
                            ["X-Cache"] = "HIT",
                            ["Cache-Status"] = "Synthetic; hit"
                        });
                }

                return SyntheticResponse.NotFound();
            },
            timeout.Token);

        try
        {
            MeasurementTargetDefinition target = new(
                Name: "내부 합성 대상",
                Url: $"http://127.0.0.1:{port}/start",
                PathKind: NetworkPathKind.Internal,
                RequireProxy: false,
                RequireDirect: true,
                MaxBytes: 1024 * 1024,
                TimeoutSeconds: 5,
                Streams: 1,
                MaxRedirects: 3);

            DownloadMeasurementResult result =
                await DownloadMeasurementRunner.RunExplicitForSmokeAsync(
                    target,
                    proxyEndpoint: null,
                    performHeadPreflight: true,
                    timeout.Token);

            await server.WaitAsync(TimeSpan.FromSeconds(15));

            Assert(result.Status == MeasurementStatus.Success,
                $"내부 측정이 성공해야 합니다: {result.Status} / {result.Message}");
            Assert(result.BytesReceived == payloadBytes,
                "실제 수신 바이트를 기록해야 합니다.");
            Assert(result.RedirectCount == 1,
                "HEAD 사전검사의 리다이렉트 1회를 기록해야 합니다.");
            Assert(result.ProxyWasUsed == false,
                "내부 DIRECT 측정은 프록시 미사용이어야 합니다.");
            Assert(result.TimeToFirstByte is not null,
                "TTFB를 기록해야 합니다.");
            Assert(result.AverageMbps is > 0,
                "평균 Mbps를 계산해야 합니다.");
            Assert(result.Samples.Count > 0,
                "최소 하나의 처리량 샘플을 생성해야 합니다.");
            Assert(result.ResponseHeaders.TryGetValue("Age", out string? age)
                && age == "12",
                "Age 헤더를 기록해야 합니다.");
            Assert(result.ResponseHeaders.TryGetValue("X-Cache", out string? cache)
                && cache == "HIT",
                "X-Cache 헤더를 기록해야 합니다.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task MeasuresExternalProxyWithTwoStreams()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        TcpListener listener = StartListener(out int port);
        const int bytesPerStream = 512 * 1024;

        Task server = ServeRequestsAsync(
            listener,
            expectedRequests: 2,
            request =>
            {
                Assert(request.Method == "GET",
                    "다중 스트림 측정은 GET 요청이어야 합니다.");
                Assert(request.Target.Contains(
                        "example.invalid/payload",
                        StringComparison.OrdinalIgnoreCase),
                    "합성 프록시는 절대 대상 URL을 받아야 합니다.");

                return SyntheticResponse.Success(
                    new byte[bytesPerStream],
                    declaredLength: bytesPerStream,
                    extraHeaders: new Dictionary<string, string>
                    {
                        ["Via"] = "1.1 synthetic-proxy",
                        ["X-Cache"] = "MISS"
                    });
            },
            timeout.Token);

        try
        {
            MeasurementTargetDefinition target = new(
                Name: "외부 합성 대상",
                Url: "http://example.invalid/payload",
                PathKind: NetworkPathKind.External,
                RequireProxy: true,
                RequireDirect: false,
                MaxBytes: 1024 * 1024,
                TimeoutSeconds: 5,
                Streams: 2,
                MaxRedirects: 0);

            DownloadMeasurementResult result =
                await DownloadMeasurementRunner.RunExplicitForSmokeAsync(
                    target,
                    proxyEndpoint: $"http://127.0.0.1:{port}",
                    performHeadPreflight: false,
                    timeout.Token);

            await server.WaitAsync(TimeSpan.FromSeconds(15));

            Assert(result.Status == MeasurementStatus.Success,
                $"외부 프록시 측정이 성공해야 합니다: {result.Status} / {result.Message}");
            Assert(result.BytesReceived == 1024 * 1024,
                "두 스트림의 총 수신량이 전체 MaxBytes를 넘지 않아야 합니다.");
            Assert(result.StreamsCompleted == 2,
                "두 스트림이 모두 완료되어야 합니다.");
            Assert(result.ProxyWasUsed == true,
                "외부 합성 측정은 프록시 사용으로 기록해야 합니다.");
            Assert(result.Samples.Select(sample => sample.StreamIndex)
                    .Distinct()
                    .Order()
                    .SequenceEqual([1, 2]),
                "구간 샘플에 스트림 번호 1과 2를 기록해야 합니다.");
            Assert(result.ResponseHeaders.ContainsKey("Via"),
                "Via 헤더를 기록해야 합니다.");
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task HandlesPreCanceledMeasurement()
    {
        using CancellationTokenSource canceled = new();
        canceled.Cancel();

        MeasurementTargetDefinition target = new(
            Name: "취소 합성 대상",
            Url: "http://127.0.0.1/canceled",
            PathKind: NetworkPathKind.Internal,
            RequireProxy: false,
            RequireDirect: true,
            MaxBytes: 1024 * 1024,
            TimeoutSeconds: 5,
            Streams: 1,
            MaxRedirects: 0);

        DownloadMeasurementResult result = await DownloadMeasurementRunner.RunAsync(
            target,
            performHeadPreflight: false,
            canceled.Token);

        Assert(result.Status == MeasurementStatus.Canceled,
            "이미 취소된 토큰은 네트워크 요청 없이 취소 상태를 반환해야 합니다.");
    }

    private static async Task RejectsInvalidInternalProxySemantics()
    {
        MeasurementTargetDefinition target = new(
            Name: "잘못된 내부 대상",
            Url: "http://127.0.0.1/payload",
            PathKind: NetworkPathKind.Internal,
            RequireProxy: true,
            RequireDirect: false,
            MaxBytes: 1024 * 1024,
            TimeoutSeconds: 5,
            Streams: 1,
            MaxRedirects: 0);

        DownloadMeasurementResult result =
            await DownloadMeasurementRunner.RunExplicitForSmokeAsync(
                target,
                proxyEndpoint: null,
                performHeadPreflight: false);

        Assert(result.Status == MeasurementStatus.Blocked
            && result.ErrorCode == "TARGET_VALIDATION_FAILED",
            "내부 대상의 프록시 필수 설정은 요청 전에 차단해야 합니다.");
    }

    private static async Task DetectsReceiveTimeout()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(10));
        TcpListener listener = StartListener(out int port);

        Task server = ServeRequestsAsync(
            listener,
            expectedRequests: 1,
            _ => new SyntheticResponse(
                StatusCode: 204,
                Reason: "No Content",
                Body: Array.Empty<byte>(),
                DeclaredLength: 0,
                ExtraHeaders: null,
                DelayBeforeResponse: TimeSpan.FromSeconds(2)),
            timeout.Token);

        try
        {
            WinHttpRequestResult result = WinHttpRequestExecutor.ExecuteExplicitForSmoke(
                new WinHttpRequestOptions(
                    Url: $"http://127.0.0.1:{port}/timeout",
                    ExpectedPath: NetworkPathKind.Internal,
                    Method: WinHttpRequestMethod.Head,
                    TimeoutMilliseconds: 1000,
                    MaxResponseBytes: 0,
                    RequireExpectedPath: true),
                proxyEndpoint: null);

            Assert(result.Status == WinHttpRequestStatus.TimedOut,
                $"수신 제한 시간을 초과하면 TimedOut이어야 합니다: {result.Status}");

            await server.WaitAsync(TimeSpan.FromSeconds(8));
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

    private static async Task ServeRequestsAsync(
        TcpListener listener,
        int expectedRequests,
        Func<SyntheticRequest, SyntheticResponse> responseFactory,
        CancellationToken cancellationToken)
    {
        List<Task> handlers = [];

        for (int index = 0; index < expectedRequests; index++)
        {
            TcpClient client = await listener.AcceptTcpClientAsync(cancellationToken);
            handlers.Add(HandleClientAsync(
                client,
                responseFactory,
                cancellationToken));
        }

        await Task.WhenAll(handlers);
    }

    private static async Task HandleClientAsync(
        TcpClient client,
        Func<SyntheticRequest, SyntheticResponse> responseFactory,
        CancellationToken cancellationToken)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            string headers = await ReadHeadersAsync(stream, cancellationToken);
            string firstLine = headers.Split(
                new[] { "\r\n" },
                StringSplitOptions.RemoveEmptyEntries)[0];
            string[] parts = firstLine.Split(' ', 3);
            Assert(parts.Length >= 2, "합성 요청 줄을 해석할 수 없습니다.");

            SyntheticResponse response = responseFactory(
                new SyntheticRequest(parts[0], parts[1], headers));

            if (response.DelayBeforeResponse > TimeSpan.Zero)
            {
                await Task.Delay(response.DelayBeforeResponse, cancellationToken);
            }

            Dictionary<string, string> responseHeaders =
                new(StringComparer.OrdinalIgnoreCase)
                {
                    ["Content-Length"] = response.DeclaredLength.ToString(),
                    ["Connection"] = "close"
                };

            if (response.ExtraHeaders is not null)
            {
                foreach ((string name, string value) in response.ExtraHeaders)
                {
                    responseHeaders[name] = value;
                }
            }

            StringBuilder builder = new();
            builder.Append($"HTTP/1.1 {response.StatusCode} {response.Reason}\r\n");
            foreach ((string name, string value) in responseHeaders)
            {
                builder.Append($"{name}: {value}\r\n");
            }

            builder.Append("\r\n");
            byte[] headerBytes = Encoding.ASCII.GetBytes(builder.ToString());

            try
            {
                await stream.WriteAsync(headerBytes, cancellationToken);
                if (response.Body.Length > 0)
                {
                    await stream.WriteAsync(response.Body, cancellationToken);
                }

                await stream.FlushAsync(cancellationToken);
            }
            catch (IOException)
            {
                // A capped or timed-out client may close before the server finishes.
            }
        }
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

        throw new InvalidOperationException(
            "합성 HTTP 요청 헤더를 완전히 읽지 못했습니다.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed record SyntheticRequest(
        string Method,
        string Target,
        string Headers);

    private sealed record SyntheticResponse(
        int StatusCode,
        string Reason,
        byte[] Body,
        int DeclaredLength,
        IReadOnlyDictionary<string, string>? ExtraHeaders,
        TimeSpan DelayBeforeResponse)
    {
        internal static SyntheticResponse Success(
            byte[] body,
            int declaredLength,
            IReadOnlyDictionary<string, string>? extraHeaders = null) =>
            new(
                200,
                "OK",
                body,
                declaredLength,
                extraHeaders,
                TimeSpan.Zero);

        internal static SyntheticResponse Redirect(string location) =>
            new(
                302,
                "Found",
                Array.Empty<byte>(),
                0,
                new Dictionary<string, string>
                {
                    ["Location"] = location
                },
                TimeSpan.Zero);

        internal static SyntheticResponse NotFound() =>
            new(
                404,
                "Not Found",
                Array.Empty<byte>(),
                0,
                null,
                TimeSpan.Zero);
    }
}
