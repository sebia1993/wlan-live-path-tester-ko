using System.Runtime.CompilerServices;
using System.Text;
using WlanLivePathTester.Core.Configuration;

namespace WlanLivePathTester.SelfTest;

internal static class TargetConfigurationFileReaderTests
{
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "WlanLivePathTester.TargetConfigReader",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            VerifyUtf8WithoutBom(directory);
            VerifyUtf8WithBom(directory);
            VerifyInvalidUtf8Rejected(directory);
            VerifyEmptyFileRejected(directory);
            Console.WriteLine("PASS strict target configuration file reader tests");
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Temporary cleanup failure must not hide the validation result.
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary cleanup failure must not hide the validation result.
            }
        }
    }

    private static void VerifyUtf8WithoutBom(string directory)
    {
        string path = Path.Combine(directory, "utf8.json");
        const string content = "{\"schemaVersion\":1}";
        File.WriteAllText(path, content, new UTF8Encoding(false));

        string loaded = TargetConfigurationFileReader.ReadStrictUtf8(path);
        Ensure(loaded == content,
            "BOM 없는 UTF-8 설정 파일을 그대로 읽어야 합니다.");
    }

    private static void VerifyUtf8WithBom(string directory)
    {
        string path = Path.Combine(directory, "utf8-bom.json");
        const string content = "{\"schemaVersion\":1}";
        File.WriteAllText(path, content, new UTF8Encoding(true));

        string loaded = TargetConfigurationFileReader.ReadStrictUtf8(path);
        Ensure(loaded == content,
            "UTF-8 BOM은 제거하고 JSON 본문만 반환해야 합니다.");
    }

    private static void VerifyInvalidUtf8Rejected(string directory)
    {
        string path = Path.Combine(directory, "invalid-utf8.json");
        File.WriteAllBytes(path, [0x7B, 0x22, 0xFF, 0x22, 0x7D]);

        EnsureThrows<InvalidDataException>(
            () => TargetConfigurationFileReader.ReadStrictUtf8(path),
            "잘못된 UTF-8을 거부해야 합니다.");
    }

    private static void VerifyEmptyFileRejected(string directory)
    {
        string path = Path.Combine(directory, "empty.json");
        File.WriteAllBytes(path, []);

        EnsureThrows<InvalidDataException>(
            () => TargetConfigurationFileReader.ReadStrictUtf8(path),
            "빈 설정 파일을 거부해야 합니다.");
    }

    private static void EnsureThrows<TException>(
        Action action,
        string message)
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

        throw new InvalidOperationException(message);
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
