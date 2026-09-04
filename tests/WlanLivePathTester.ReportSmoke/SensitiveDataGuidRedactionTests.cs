using System.Runtime.CompilerServices;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class SensitiveDataGuidRedactionTests
{
    private const string UpperGuid =
        "61B2C3D4-E5F6-47A8-9123-1234567890AB";
    private const string LowerGuid =
        "61b2c3d4-e5f6-47a8-9123-1234567890ab";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RedactsBareAndBracedGuids();
        RedactsGuidInsideRedactedUrlPathHint();
        KeepsCsvFormulaProtectionAfterGuidRedaction();
        PreservesVersionsAndNearMatches();
        Console.WriteLine("PASS shared interface GUID redaction tests");
    }

    private static void RedactsBareAndBracedGuids()
    {
        string source =
            $"adapter={UpperGuid}; secondary={{{LowerGuid}}}.";
        string redacted = SensitiveDataRedactor.RedactText(source)
            ?? throw new InvalidOperationException(
                "GUID 마스킹 결과가 null이면 안 됩니다.");

        Ensure(!redacted.Contains(
                UpperGuid,
                StringComparison.OrdinalIgnoreCase),
            "대문자 bare GUID가 남으면 안 됩니다.");
        Ensure(!redacted.Contains(
                LowerGuid,
                StringComparison.OrdinalIgnoreCase),
            "소문자 braced GUID가 남으면 안 됩니다.");
        Ensure(CountOccurrences(
                redacted,
                "[GUID 마스킹됨]") == 2,
            "서로 다른 GUID 위치를 각각 마스킹해야 합니다.");
    }

    private static void RedactsGuidInsideRedactedUrlPathHint()
    {
        string source =
            $"https://internal.example.invalid/download/{UpperGuid}.bin";
        string redacted = SensitiveDataRedactor.RedactText(source)
            ?? throw new InvalidOperationException(
                "URL 마스킹 결과가 null이면 안 됩니다.");

        Ensure(redacted.Contains(
                "[호스트 마스킹됨]",
                StringComparison.Ordinal),
            "URL 호스트 마스킹을 유지해야 합니다.");
        Ensure(redacted.Contains(
                "[GUID 마스킹됨]",
                StringComparison.Ordinal),
            "보존된 파일명 힌트 안의 GUID도 마스킹해야 합니다.");
        Ensure(!redacted.Contains(
                UpperGuid,
                StringComparison.OrdinalIgnoreCase),
            "URL 파일명 힌트에 GUID 원문이 남으면 안 됩니다.");
    }

    private static void KeepsCsvFormulaProtectionAfterGuidRedaction()
    {
        string protectedValue = SensitiveDataRedactor.ProtectCsvFormula(
            $"=HYPERLINK(\"https://example.invalid/{UpperGuid}\",\"open\")");

        Ensure(protectedValue.StartsWith("'=HYPERLINK", StringComparison.Ordinal),
            "GUID 마스킹 뒤에도 CSV 수식 비활성화를 유지해야 합니다.");
        Ensure(!protectedValue.Contains(
                UpperGuid,
                StringComparison.OrdinalIgnoreCase),
            "CSV 보호 결과에 GUID 원문이 남으면 안 됩니다.");
    }

    private static void PreservesVersionsAndNearMatches()
    {
        string source =
            "version=0.1.0-alpha.10; token=61B2C3D4-E5F6-47A8-9123-1234567890AZ";
        string redacted = SensitiveDataRedactor.RedactText(source)
            ?? throw new InvalidOperationException(
                "일반 문자열 마스킹 결과가 null이면 안 됩니다.");

        Ensure(redacted.Contains(
                "0.1.0-alpha.10",
                StringComparison.Ordinal),
            "릴리스 버전 문자열을 GUID로 오인하면 안 됩니다.");
        Ensure(redacted.Contains(
                "61B2C3D4-E5F6-47A8-9123-1234567890AZ",
                StringComparison.Ordinal),
            "16진수가 아닌 near-match를 GUID로 오인하면 안 됩니다.");
    }

    private static int CountOccurrences(
        string source,
        string value)
    {
        int count = 0;
        int offset = 0;
        while ((offset = source.IndexOf(
                   value,
                   offset,
                   StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }

        return count;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
