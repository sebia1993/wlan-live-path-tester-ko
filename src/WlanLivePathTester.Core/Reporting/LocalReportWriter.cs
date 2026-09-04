using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WlanLivePathTester.Core.Reporting;

public static class LocalReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static LocalReportExportResult WriteAll(
        LocalDiagnosticReport report,
        string outputDirectory,
        string filePrefix = "WlanLivePathTester")
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        string fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);

        string safePrefix = SensitiveDataRedactor.SafeFileComponent(
            filePrefix,
            "WlanLivePathTester");
        string timestamp = report.Metadata.GeneratedAt.ToLocalTime()
            .ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        string baseName = GetAvailableBaseName(
            fullDirectory,
            $"{safePrefix}_{timestamp}");

        string jsonPath = Path.Combine(fullDirectory, baseName + ".json");
        string csvPath = Path.Combine(fullDirectory, baseName + ".csv");
        string htmlPath = Path.Combine(fullDirectory, baseName + ".html");
        string sha256Path = Path.Combine(
            fullDirectory,
            baseName + "_SHA256SUMS.txt");

        WriteAtomic(jsonPath, RenderJson(report), new UTF8Encoding(false));
        WriteAtomic(csvPath, RenderCsv(report), new UTF8Encoding(true));
        WriteAtomic(htmlPath, RenderHtml(report), new UTF8Encoding(false));

        Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase)
        {
            [Path.GetFileName(jsonPath)] = ComputeSha256(jsonPath),
            [Path.GetFileName(csvPath)] = ComputeSha256(csvPath),
            [Path.GetFileName(htmlPath)] = ComputeSha256(htmlPath)
        };
        string checksumText = string.Join(
            Environment.NewLine,
            hashes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => $"{pair.Value}  {pair.Key}"))
            + Environment.NewLine;
        WriteAtomic(sha256Path, checksumText, new UTF8Encoding(false));

        return new LocalReportExportResult(
            OutputDirectory: fullDirectory,
            JsonPath: jsonPath,
            CsvPath: csvPath,
            HtmlPath: htmlPath,
            Sha256Path: sha256Path,
            Sha256: hashes);
    }

    public static string RenderJson(LocalDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions)
            + Environment.NewLine;
    }

    public static string RenderCsv(LocalDiagnosticReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        List<CsvRow> rows = [];
        AddMetadataRows(rows, report);
        AddWlanRows(rows, report.Wlan);
        AddProxyRows(rows, report.Proxy);
        AddMeasurementRows(rows, report.Measurements);
        AddObservationRows(rows, report.BrowserObservation);
        AddFindingRows(rows, report.Findings);
        AddLimitationRows(rows, report.Limitations);

        StringBuilder builder = new();
        builder.AppendLine("section,key,value");
        foreach (CsvRow row in rows)
        {
            builder.Append(Csv(row.Section));
            builder.Append(',');
            builder.Append(Csv(row.Key));
            builder.Append(',');
            builder.AppendLine(Csv(row.Value));
        }

        return builder.ToString();
    }

    public static string RenderHtml(LocalDiagnosticReport report) =>
        LocalHtmlReportRenderer.Render(report);

    private static void AddMetadataRows(
        ICollection<CsvRow> rows,
        LocalDiagnosticReport report)
    {
        Add(rows, "metadata", "schemaVersion", report.SchemaVersion);
        Add(rows, "metadata", "generatedAt", Iso(report.Metadata.GeneratedAt));
        Add(rows, "metadata", "applicationName", report.Metadata.ApplicationName);
        Add(rows, "metadata", "applicationVersion", report.Metadata.ApplicationVersion);
        Add(rows, "metadata", "operatingSystem", report.Metadata.OperatingSystem);
        Add(rows, "metadata", "runtimeVersion", report.Metadata.RuntimeVersion);
        Add(rows, "metadata", "culture", report.Metadata.Culture);
        Add(rows, "metadata", "sensitiveValuesIncluded", Boolean(report.Metadata.SensitiveValuesIncluded));
        Add(rows, "metadata", "dataHandling", report.Metadata.DataHandlingStatement);
    }

    private static void AddWlanRows(
        ICollection<CsvRow> rows,
        ReportWlanSection wlan)
    {
        Add(rows, "wlan", "capturedAt", Iso(wlan.CapturedAt));
        Add(rows, "wlan", "isConnected", Boolean(wlan.IsConnected));
        Add(rows, "wlan", "interfaceDescription", wlan.InterfaceDescription);
        Add(rows, "wlan", "interfaceState", wlan.InterfaceState);
        Add(rows, "wlan", "ssid", wlan.Ssid);
        Add(rows, "wlan", "bssid", wlan.Bssid);
        Add(rows, "wlan", "rssiDbm", Number(wlan.RssiDbm));
        Add(rows, "wlan", "signalQualityPercent", Number(wlan.SignalQualityPercent));
        Add(rows, "wlan", "channel", Number(wlan.Channel));
        Add(rows, "wlan", "centerFrequencyMhz", Number(wlan.CenterFrequencyMhz));
        Add(rows, "wlan", "band", wlan.Band);
        Add(rows, "wlan", "phyType", wlan.PhyType);
        Add(rows, "wlan", "receiveLinkMbps", Number(wlan.ReceiveLinkMbps));
        Add(rows, "wlan", "transmitLinkMbps", Number(wlan.TransmitLinkMbps));
        Add(rows, "wlan", "authentication", wlan.Authentication);
        Add(rows, "wlan", "cipher", wlan.Cipher);
        Add(rows, "wlan", "readError", wlan.ReadError);
    }

    private static void AddProxyRows(
        ICollection<CsvRow> rows,
        ReportProxySection proxy)
    {
        Add(rows, "proxy", "readSucceeded", Boolean(proxy.ReadSucceeded));
        Add(rows, "proxy", "mode", proxy.Mode);
        Add(rows, "proxy", "autoDetectEnabled", Boolean(proxy.AutoDetectEnabled));
        Add(rows, "proxy", "pacConfigured", Boolean(proxy.PacConfigured));
        Add(rows, "proxy", "manualProxyConfigured", Boolean(proxy.ManualProxyConfigured));
        Add(rows, "proxy", "bypassConfigured", Boolean(proxy.BypassConfigured));
        Add(rows, "proxy", "win32Error", Number(proxy.Win32Error));
        Add(rows, "proxy", "statement", proxy.Statement);
    }

    private static void AddMeasurementRows(
        ICollection<CsvRow> rows,
        IReadOnlyList<ReportTextSection> measurements)
    {
        foreach (ReportTextSection measurement in measurements)
        {
            string section = $"measurement.{measurement.SectionId}";
            Add(rows, section, "title", measurement.Title);
            Add(rows, section, "capturedAt", Iso(measurement.CapturedAt));
            Add(rows, section, "content", measurement.Content);
        }
    }

    private static void AddObservationRows(
        ICollection<CsvRow> rows,
        ReportObservationSection? observation)
    {
        if (observation is null)
        {
            return;
        }

        Add(rows, "browserObservation", "status", observation.Status);
        Add(rows, "browserObservation", "startedAt", Iso(observation.StartedAt));
        Add(rows, "browserObservation", "completedAt", Iso(observation.CompletedAt));
        Add(rows, "browserObservation", "observedSeconds", Number(observation.ObservedSeconds));
        Add(rows, "browserObservation", "baselineReceiveMbps", Number(observation.BaselineReceiveMbps));
        Add(rows, "browserObservation", "averageAdjustedReceiveMbps", Number(observation.AverageAdjustedReceiveMbps));
        Add(rows, "browserObservation", "peakAdjustedReceiveMbps", Number(observation.PeakAdjustedReceiveMbps));
        Add(rows, "browserObservation", "totalReceiveBytes", Number(observation.TotalReceiveBytes));
        Add(rows, "browserObservation", "activeSampleCount", Number(observation.ActiveSampleCount));
        Add(rows, "browserObservation", "pauseCount", Number(observation.PauseCount));
        Add(rows, "browserObservation", "suddenDropCount", Number(observation.SuddenDropCount));
        Add(rows, "browserObservation", "bssidChangeCount", Number(observation.BssidChangeCount));
        Add(rows, "browserObservation", "adapterChangeCount", Number(observation.AdapterChangeCount));
        Add(rows, "browserObservation", "counterResetCount", Number(observation.CounterResetCount));
        Add(rows, "browserObservation", "wlanDisconnectedSampleCount", Number(observation.WlanDisconnectedSampleCount));
        Add(rows, "browserObservation", "confidence", observation.Confidence);
        Add(rows, "browserObservation", "message", observation.Message);
        Add(rows, "browserObservation", "limitation", observation.Limitation);

        for (int index = 0; index < observation.Samples.Count; index++)
        {
            ReportObservationSample sample = observation.Samples[index];
            string section = $"browserObservation.sample.{index + 1}";
            Add(rows, section, "timestamp", Iso(sample.Timestamp));
            Add(rows, section, "intervalSeconds", Number(sample.IntervalSeconds));
            Add(rows, section, "isBaseline", Boolean(sample.IsBaseline));
            Add(rows, section, "receiveBytesDelta", Number(sample.ReceiveBytesDelta));
            Add(rows, section, "transmitBytesDelta", Number(sample.TransmitBytesDelta));
            Add(rows, section, "rawReceiveMbps", Number(sample.RawReceiveMbps));
            Add(rows, section, "rawTransmitMbps", Number(sample.RawTransmitMbps));
            Add(rows, section, "adjustedReceiveMbps", Number(sample.AdjustedReceiveMbps));
            Add(rows, section, "rssiDbm", Number(sample.RssiDbm));
            Add(rows, section, "receiveLinkMbps", Number(sample.ReceiveLinkMbps));
            Add(rows, section, "transmitLinkMbps", Number(sample.TransmitLinkMbps));
            Add(rows, section, "bssidChanged", Boolean(sample.BssidChanged));
            Add(rows, section, "adapterChanged", Boolean(sample.AdapterChanged));
            Add(rows, section, "counterReset", Boolean(sample.CounterReset));
            Add(rows, section, "wlanDisconnected", Boolean(sample.WlanDisconnected));
            Add(rows, section, "pauseDetected", Boolean(sample.PauseDetected));
            Add(rows, section, "suddenDropDetected", Boolean(sample.SuddenDropDetected));
            Add(rows, section, "note", sample.Note);
        }
    }

    private static void AddFindingRows(
        ICollection<CsvRow> rows,
        IReadOnlyList<ReportFinding> findings)
    {
        for (int index = 0; index < findings.Count; index++)
        {
            ReportFinding finding = findings[index];
            string section = $"finding.{index + 1}";
            Add(rows, section, "code", finding.Code);
            Add(rows, section, "severity", finding.Severity);
            Add(rows, section, "title", finding.Title);
            Add(rows, section, "evidence", finding.Evidence);
            Add(rows, section, "interpretation", finding.Interpretation);
            Add(rows, section, "limitation", finding.Limitation);
            Add(rows, section, "nextStep", finding.NextStep);
        }
    }

    private static void AddLimitationRows(
        ICollection<CsvRow> rows,
        IReadOnlyList<string> limitations)
    {
        for (int index = 0; index < limitations.Count; index++)
        {
            Add(
                rows,
                "limitation",
                (index + 1).ToString(CultureInfo.InvariantCulture),
                limitations[index]);
        }
    }

    private static string GetAvailableBaseName(
        string directory,
        string desired)
    {
        for (int suffix = 0; suffix <= 9999; suffix++)
        {
            string candidate = suffix == 0
                ? desired
                : $"{desired}_{suffix}";
            if (!File.Exists(Path.Combine(directory, candidate + ".json"))
                && !File.Exists(Path.Combine(directory, candidate + ".csv"))
                && !File.Exists(Path.Combine(directory, candidate + ".html"))
                && !File.Exists(Path.Combine(
                    directory,
                    candidate + "_SHA256SUMS.txt")))
            {
                return candidate;
            }
        }

        throw new IOException("사용 가능한 보고서 파일 이름을 만들지 못했습니다.");
    }

    private static void WriteAtomic(
        string destination,
        string content,
        Encoding encoding)
    {
        string directory = Path.GetDirectoryName(destination)
            ?? throw new InvalidOperationException(
                "보고서 출력 디렉터리를 확인할 수 없습니다.");
        string temporary = Path.Combine(
            directory,
            $".{Path.GetFileName(destination)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporary, content, encoding);
            File.Move(temporary, destination, overwrite: false);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream))
            .ToLowerInvariant();
    }

    private static void Add(
        ICollection<CsvRow> rows,
        string section,
        string key,
        string? value) =>
        rows.Add(new CsvRow(section, key, value ?? string.Empty));

    private static string Csv(string value)
    {
        string safe = SensitiveDataRedactor.ProtectCsvFormula(value);
        return '"' + safe.Replace("\"", "\"\"") + '"';
    }

    private static string Boolean(bool value) =>
        value.ToString(CultureInfo.InvariantCulture);

    private static string Number<T>(T? value)
        where T : struct, IFormattable =>
        value.HasValue
            ? value.Value.ToString(null, CultureInfo.InvariantCulture)
            : string.Empty;

    private static string Iso(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private static string Iso(DateTimeOffset? value) =>
        value?.ToString("O", CultureInfo.InvariantCulture)
        ?? string.Empty;

    private sealed record CsvRow(string Section, string Key, string Value);
}
