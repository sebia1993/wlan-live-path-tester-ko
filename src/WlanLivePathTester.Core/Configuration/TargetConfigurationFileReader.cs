using System.Text;

namespace WlanLivePathTester.Core.Configuration;

public static class TargetConfigurationFileReader
{
    public const long MaximumConfigurationBytes = 1024 * 1024;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static string ReadStrictUtf8(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath = Path.GetFullPath(path);
        FileInfo file = new(fullPath);
        if (!file.Exists)
        {
            throw new FileNotFoundException(
                "승인 대상 설정 파일이 존재하지 않습니다.");
        }

        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                "심볼릭 링크 또는 reparse point의 승인 대상 설정 파일은 허용하지 않습니다.");
        }

        if (file.Length is <= 0 or > MaximumConfigurationBytes)
        {
            throw new InvalidDataException(
                $"승인 대상 설정 파일은 1바이트 이상 {MaximumConfigurationBytes}바이트 이하여야 합니다.");
        }

        byte[] bytes = new byte[checked((int)file.Length)];
        using (FileStream stream = new(
                   fullPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   bufferSize: 16 * 1024,
                   FileOptions.SequentialScan))
        {
            int offset = 0;
            while (offset < bytes.Length)
            {
                int read = stream.Read(bytes, offset, bytes.Length - offset);
                if (read == 0)
                {
                    throw new EndOfStreamException(
                        "승인 대상 설정 파일을 끝까지 읽지 못했습니다.");
                }

                offset += read;
            }

            if (stream.ReadByte() != -1)
            {
                throw new InvalidDataException(
                    "승인 대상 설정 파일 크기가 읽는 동안 변경되었습니다.");
            }
        }

        int start = bytes.Length >= 3
            && bytes[0] == 0xEF
            && bytes[1] == 0xBB
            && bytes[2] == 0xBF
                ? 3
                : 0;

        try
        {
            return StrictUtf8.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException(
                "승인 대상 설정 파일은 올바른 UTF-8이어야 합니다.",
                exception);
        }
        finally
        {
            Array.Clear(bytes);
        }
    }
}
