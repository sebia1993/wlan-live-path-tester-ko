using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Windows.Http;

namespace WlanLivePathTester.MeasurementSmoke;

internal static class ActiveCancellationSmoke
{
    [ModuleInitializer]
    internal static void RunBeforeMain()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RunAsync().GetAwaiter().GetResult();
        Console.WriteLine("PASS  진행 중 WinHTTP 요청 즉시 취소");
    }

    private static async Task RunAsync()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using CancellationTokenSource serverStop = new(TimeSpan.FromSeconds(8));
        Task server = ServeDelayedResponseAsync(listener, serverStop.Token);

        try
        {
            using CancellationTokenSource cancellation = new();
            cancellation.CancelAfter(TimeSpan.FromMilliseconds(350));
            Stopwatch stopwatch = Stopwatch.StartNew();

            WinHttpRequestResult result = await Task.Run(() =>
                WinHttpRequestExecutor.ExecuteExplicitForSmoke(
                    new WinHttpRequestOptions(
                        Url: $"http://127.0.0.1:{port}/wait",
                        ExpectedPath: NetworkPathKind.Internal,
                        Method: WinHttpRequestMethod.Get,
                        TimeoutMilliseconds: 7000,
                        MaxResponseBytes: 1024 * 1024,
                        RequireExpectedPath: true,
                        CancellationToken: cancellation.Token),
                    proxyEndpoint: null));

            stopwatch.Stop();
            if (result.Status != WinHttpRequestStatus.Canceled)
            {
                throw new InvalidOperationException(
                    $"진행 중 요청이 Canceled가 아닙니다: {result.Status} / {result.Message}");
            }

            if (stopwatch.Elapsed >= TimeSpan.FromSeconds(4))
            {
                throw new InvalidOperationException(
                    $"요청 취소가 제한 시간보다 충분히 빠르지 않습니다: {stopwatch.Elapsed.TotalSeconds:F2}초");
            }

            if (!string.Equals(
                    result.ErrorCode,
                    "MEASUREMENT_CANCELED",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"취소 오류 코드가 올바르지 않습니다: {result.ErrorCode}");
            }
        }
        finally
        {
            serverStop.Cancel();
            listener.Stop();
            try
            {
                await server.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception exception) when (
                exception is OperationCanceledException
                    or IOException
                    or SocketException
                    or TimeoutException)
            {
                // The test intentionally closes a pending loopback connection.
            }
        }
    }

    private static async Task ServeDelayedResponseAsync(
        TcpListener listener,
        CancellationToken cancellationToken)
    {
        using TcpClient client = await listener.AcceptTcpClientAsync(
            cancellationToken);
        using NetworkStream stream = client.GetStream();
        await ReadRequestHeadersAsync(stream, cancellationToken);
        await Task.Delay(TimeSpan.FromSeconds(6), cancellationToken);

        byte[] response = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\nContent-Length: 1\r\nConnection: close\r\n\r\nX");
        await stream.WriteAsync(response, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    private static async Task ReadRequestHeadersAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        byte[] oneByte = new byte[1];
        Queue<byte> tail = new();

        for (int count = 0; count < 64 * 1024; count++)
        {
            int read = await stream.ReadAsync(oneByte, cancellationToken);
            if (read == 0)
            {
                throw new IOException("클라이언트가 요청 헤더 전송 전에 연결을 닫았습니다.");
            }

            tail.Enqueue(oneByte[0]);
            while (tail.Count > 4)
            {
                tail.Dequeue();
            }

            if (tail.Count == 4
                && tail.ElementAt(0) == (byte)'\r'
                && tail.ElementAt(1) == (byte)'\n'
                && tail.ElementAt(2) == (byte)'\r'
                && tail.ElementAt(3) == (byte)'\n')
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "합성 요청 헤더가 64 KiB 제한을 초과했습니다.");
    }
}
