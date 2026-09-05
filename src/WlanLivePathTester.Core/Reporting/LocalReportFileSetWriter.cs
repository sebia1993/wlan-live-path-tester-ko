using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;

[assembly: InternalsVisibleTo("WlanLivePathTester.ReportSmoke")]

namespace WlanLivePathTester.Core.Reporting;

/// <summary>A save failed and at least one owned temporary/output file could not be removed.</summary>
public sealed class ReportFileSetRecoveryException : IOException
{
    internal ReportFileSetRecoveryException(Exception cause)
        : base("REPORT_FILE_SET_RECOVERY_REQUIRED: report save did not complete; local cleanup requires review.", cause)
    {
    }
}

/// <summary>
/// Serializes cooperating writers by basename and publishes the checksum manifest last.
/// This is not a four-file atomic transaction and does not promise power-loss recovery.
/// </summary>
internal static class LocalReportFileSetWriter
{
    private const int MaximumSuffix = 9999;
    private static readonly string[] Extensions = [".json", ".csv", ".html", "_SHA256SUMS.txt"];
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly UTF8Encoding CsvUtf8 = new(true, true);

    internal static InternalProxyRouteComparisonRunReportExportResult Write(
        string outputDirectory,
        string desiredBaseName,
        string json,
        string csv,
        string html,
        CancellationToken cancellationToken = default,
        ReportFileOperations? operations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredBaseName);
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(csv);
        ArgumentNullException.ThrowIfNull(html);
        ValidateBaseName(desiredBaseName);
        cancellationToken.ThrowIfCancellationRequested();

        string directory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(directory);
        operations ??= new ReportFileOperations();

        for (int suffix = 0; suffix <= MaximumSuffix; suffix++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string name = suffix == 0 ? desiredBaseName
                : desiredBaseName + "_" + suffix.ToString(CultureInfo.InvariantCulture);
            string[] destinations = Extensions.Select(extension =>
                Path.Combine(directory, name + extension)).ToArray();
            if (destinations.Any(Path.Exists))
            {
                continue;
            }

            string reservationPath = Path.Combine(directory, "." + name + ".writing.lock");
            FileStream reservation;
            try
            {
                // CreateNew owns this basename across cooperating processes.
                reservation = new FileStream(reservationPath, FileMode.CreateNew,
                    FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            }
            catch (IOException exception) when (IsReservationCollision(exception, reservationPath))
            {
                continue;
            }

            if (destinations.Any(Path.Exists))
            {
                reservation.Dispose();
                continue;
            }
            return WriteReserved(directory, destinations, reservation,
                json, csv, html, cancellationToken, operations);
        }
        throw new IOException("REPORT_FILE_SET_NAME_EXHAUSTED: no unused report basename is available.");
    }

    private static InternalProxyRouteComparisonRunReportExportResult WriteReserved(
        string directory, string[] destinations, FileStream reservation,
        string json, string csv, string html, CancellationToken cancellationToken,
        ReportFileOperations operations)
    {
        string stageDirectory = Path.Combine(directory, ".wlan-report-stage-" + Guid.NewGuid().ToString("N"));
        string[] staged = Enumerable.Range(0, Extensions.Length)
            .Select(index => Path.Combine(stageDirectory, index.ToString(CultureInfo.InvariantCulture) + ".tmp"))
            .ToArray();
        int publishedCount = 0;
        bool stageOwned = false;
        bool committed = false;
        Exception? failure = null;
        InternalProxyRouteComparisonRunReportExportResult? export = null;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Path.Exists(stageDirectory))
                throw new IOException("REPORT_STAGE_COLLISION: temporary directory already exists.");
            Directory.CreateDirectory(stageDirectory);
            stageOwned = true;
            string[] contents = [json, csv, html];
            for (int index = 0; index < contents.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operations.WriteNewText(staged[index], contents[index], index == 1 ? CsvUtf8 : Utf8);
            }

            Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < contents.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using FileStream stream = File.OpenRead(staged[index]);
                hashes.Add(Path.GetFileName(destinations[index]),
                    Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant());
            }
            string manifest = string.Join(Environment.NewLine,
                hashes.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(pair => pair.Value + "  " + pair.Key)) + Environment.NewLine;
            cancellationToken.ThrowIfCancellationRequested();
            operations.WriteNewText(staged[3], manifest, Utf8);
            export = new InternalProxyRouteComparisonRunReportExportResult(
                directory, destinations[0], destinations[1], destinations[2], destinations[3],
                new ReadOnlyDictionary<string, string>(hashes));
            for (int index = 0; index < staged.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                operations.MoveNew(staged[index], destinations[index]);
                publishedCount++;
            }
            committed = true;
            // Cancellation after checksum publication must not revoke a completed report.
        }
        catch (Exception exception) { failure = exception; }

        bool cleanupIncomplete = false;
        if (!committed)
        {
            for (int index = publishedCount - 1; index >= 0; index--)
                cleanupIncomplete |= !TryCleanup(() => operations.DeleteFile(destinations[index]));
        }
        if (stageOwned)
        {
            foreach (string path in staged)
                cleanupIncomplete |= !TryCleanup(() => operations.DeleteFile(path));
            // Unexpected files must be preserved; never delete recursively.
            cleanupIncomplete |= !TryCleanup(() => operations.DeleteDirectory(stageDirectory));
        }
        cleanupIncomplete |= !TryCleanup(reservation.Dispose);
        if (failure is not null)
        {
            if (cleanupIncomplete) throw new ReportFileSetRecoveryException(failure);
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
        return (export ?? throw new InvalidOperationException("REPORT_FILE_SET_RESULT_MISSING")) with
        {
            CleanupIncomplete = cleanupIncomplete
        };
    }

    private static bool TryCleanup(Action cleanup)
    {
        try { cleanup(); return true; }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    internal static bool IsReservationCollision(IOException exception, string path)
    {
        int code = exception.HResult & 0xffff;
        return code is 80 or 183 || (!OperatingSystem.IsWindows() && code == 17) || Path.Exists(path);
    }

    private static void ValidateBaseName(string value)
    {
        if (value.Length > 160 || value is "." or ".."
            || value.EndsWith('.') || value.EndsWith(' ')
            || value.Any(character => char.IsControl(character) || "<>:\"/\\|?*".Contains(character)))
            throw new ArgumentException("Report basename must be a single safe Windows filename.", nameof(value));
        string stem = value.Split('.')[0];
        if (stem.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || ((stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && stem.Length == 4 && stem[3] is >= '1' and <= '9'))
            throw new ArgumentException("Reserved Windows device name is not allowed.", nameof(value));
    }
}

/// <summary>Local I/O seam. A failing MoveNew must leave the destination unmodified.</summary>
internal class ReportFileOperations
{
    internal virtual void WriteNewText(string path, string content, Encoding encoding)
    {
        using FileStream stream = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using (StreamWriter writer = new(stream, encoding, 4096, leaveOpen: true))
        {
            writer.Write(content);
            writer.Flush();
        }
        stream.Flush(flushToDisk: true);
    }
    internal virtual void MoveNew(string source, string destination) => File.Move(source, destination, overwrite: false);
    internal virtual void DeleteFile(string path) => File.Delete(path);
    internal virtual void DeleteDirectory(string path) => Directory.Delete(path, recursive: false);
}
