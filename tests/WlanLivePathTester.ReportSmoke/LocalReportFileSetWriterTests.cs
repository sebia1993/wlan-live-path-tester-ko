using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using WlanLivePathTester.Core.Reporting;

namespace WlanLivePathTester.ReportSmoke;

internal static class LocalReportFileSetWriterTests
{
    private const string Name = "WlanRouteComparison_20260905_230000";
    private const string Json = "{\"case\":\"local-only\"}\n";
    private const string Csv = "section,key,value\nmetadata,case,local-only\n";
    private const string Html = "<!doctype html><html lang=\"ko\"><body>로컬 보고서</body></html>";

#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Run()
    {
        RecognizesVanishedReservationsWithoutRetryingDiskFull();
        SavesFourFilesWithActualHashesAndCsvBom();
        ReservesDistinctNamesForConcurrentSaves();
        PreservesExistingFilesAndDirectories();
        SkipsAnExistingReservationWithoutRemovingIt();
        PreCanceledSaveCreatesNoOutputDirectory();
        WriteFailuresRollbackAllOwnedFiles();
        PublicationFailuresNeverPublishCompletionMarker();
        LateCollisionPreservesTheForeignFile();
        CancellationBeforeCommitRollsBack();
        CancellationAfterCommitKeepsTheCompletedReport();
        CleanupFailureAfterCommitIsAWarningNotSaveFailure();
        FailedRollbackReportsRecoveryRequired();
        CompletionMarkerIsAlwaysLast();
        UnexpectedStageContentIsNotRecursivelyDeleted();
        RejectsUnsafeBaseNamesBeforeFilesystemWrites();
        RejectsInvalidUtf16WithoutLeavingPartialFiles();
        Console.WriteLine("PASS local report file-set save: 17 scenario groups (fault matrices included)");
    }

    private static void RecognizesVanishedReservationsWithoutRetryingDiskFull()
    {
        using TestDirectory directory = new();
        string missing = System.IO.Path.Combine(directory.Path, "already-closed.lock");
        Ensure(LocalReportFileSetWriter.IsReservationCollision(
            new IOException("synthetic exists", unchecked((int)0x80070050)), missing),
            "File-exists must stay a collision even if the reservation has just been deleted.");
        Ensure(LocalReportFileSetWriter.IsReservationCollision(
            new IOException("synthetic exists", unchecked((int)0x800700b7)), missing),
            "Already-exists must not depend on a racy existence probe.");
        Ensure(!LocalReportFileSetWriter.IsReservationCollision(
            new IOException("synthetic disk full", unchecked((int)0x80070070)), missing),
            "Disk-full must not be retried as a basename collision.");
        AssertEmpty(directory.Path);
    }

    private static void SavesFourFilesWithActualHashesAndCsvBom()
    {
        using TestDirectory directory = new();
        var result = Save(directory.Path);
        Ensure(Directory.GetFiles(directory.Path).Length == 4, "Expected exactly four report files.");
        Ensure(Directory.GetDirectories(directory.Path).Length == 0, "Staging directory must be removed.");
        Ensure(!result.CleanupIncomplete, "Successful cleanup must not report a warning.");
        VerifyHashes(result);
        Ensure(File.ReadAllBytes(result.CsvPath).Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }),
            "CSV must retain the Excel-compatible UTF-8 BOM.");
        Ensure(!File.ReadAllBytes(result.JsonPath).Take(3).SequenceEqual(new byte[] { 0xef, 0xbb, 0xbf }),
            "JSON must not have a BOM.");
    }

    private static void ReservesDistinctNamesForConcurrentSaves()
    {
        using TestDirectory directory = new();
        var results = Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            string marker = "save-" + index;
            var result = LocalReportFileSetWriter.Write(directory.Path, Name, marker, marker, marker);
            Ensure(File.ReadAllText(result.JsonPath) == marker
                && File.ReadAllText(result.CsvPath) == marker
                && File.ReadAllText(result.HtmlPath) == marker, "Concurrent reports must not be mixed.");
            return result;
        }))).GetAwaiter().GetResult();
        Ensure(results.Select(result => result.JsonPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 32,
            "Every concurrent operation needs a unique basename.");
        Ensure(Directory.GetFiles(directory.Path).Length == 128, "Concurrent saves must leave 128 report files.");
        Ensure(Directory.GetDirectories(directory.Path).Length == 0, "Concurrent saves must clean staging directories.");
        foreach (var result in results) VerifyHashes(result);
    }

    private static void PreservesExistingFilesAndDirectories()
    {
        using TestDirectory directory = new();
        string oldFile = System.IO.Path.Combine(directory.Path, Name + ".html");
        string oldDirectory = System.IO.Path.Combine(directory.Path, Name + "_1.csv");
        File.WriteAllText(oldFile, "existing-report");
        Directory.CreateDirectory(oldDirectory);
        var result = Save(directory.Path);
        Ensure(System.IO.Path.GetFileName(result.JsonPath) == Name + "_2.json", "File and directory collisions need new suffixes.");
        Ensure(File.ReadAllText(oldFile) == "existing-report" && Directory.Exists(oldDirectory),
            "Existing output must never be replaced or removed.");
    }

    private static void SkipsAnExistingReservationWithoutRemovingIt()
    {
        using TestDirectory directory = new();
        string lockPath = System.IO.Path.Combine(directory.Path, "." + Name + ".writing.lock");
        File.WriteAllText(lockPath, "foreign-reservation");
        var result = Save(directory.Path);
        Ensure(System.IO.Path.GetFileName(result.JsonPath) == Name + "_1.json", "Reserved basename must be skipped.");
        Ensure(File.ReadAllText(lockPath) == "foreign-reservation", "Never remove another operation's reservation.");
    }

    private static void PreCanceledSaveCreatesNoOutputDirectory()
    {
        using TestDirectory directory = new();
        string missing = System.IO.Path.Combine(directory.Path, "not-created");
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        Throws<OperationCanceledException>(() => Save(missing, cancellation.Token));
        Ensure(!Directory.Exists(missing), "Pre-cancellation must precede output directory creation.");
    }

    private static void WriteFailuresRollbackAllOwnedFiles()
    {
        for (int index = 1; index <= 4; index++)
        {
            using TestDirectory directory = new();
            var operations = new FaultOperations { FailWrite = index };
            Throws<IOException>(() => Save(directory.Path, operations: operations));
            AssertEmpty(directory.Path);
        }
    }

    private static void PublicationFailuresNeverPublishCompletionMarker()
    {
        for (int index = 1; index <= 4; index++)
        {
            using TestDirectory directory = new();
            var operations = new FaultOperations { FailMove = index };
            Throws<IOException>(() => Save(directory.Path, operations: operations));
            AssertEmpty(directory.Path);
        }
    }

    private static void LateCollisionPreservesTheForeignFile()
    {
        using TestDirectory directory = new();
        string? foreignFile = null;
        var operations = new FaultOperations
        {
            BeforeMove = (number, path) =>
            {
                if (number == 2)
                {
                    foreignFile = path;
                    File.WriteAllText(path, "foreign-file");
                }
            }
        };
        Throws<IOException>(() => Save(directory.Path, operations: operations));
        Ensure(foreignFile is not null && File.ReadAllText(foreignFile) == "foreign-file",
            "A late non-cooperating writer's file must survive rollback.");
        Ensure(Directory.GetFiles(directory.Path).Length == 1, "Only the foreign collision file should remain.");
        Ensure(Directory.GetDirectories(directory.Path).Length == 0, "Owned staging must be removed.");
    }

    private static void CancellationBeforeCommitRollsBack()
    {
        using TestDirectory directory = new();
        using CancellationTokenSource cancellation = new();
        var operations = new FaultOperations
        {
            AfterMove = (number, path) => { if (number == 2) cancellation.Cancel(); }
        };
        Throws<OperationCanceledException>(() => Save(directory.Path, cancellation.Token, operations));
        AssertEmpty(directory.Path);
    }

    private static void CancellationAfterCommitKeepsTheCompletedReport()
    {
        using TestDirectory directory = new();
        using CancellationTokenSource cancellation = new();
        var operations = new FaultOperations
        {
            AfterMove = (number, path) => { if (number == 4) cancellation.Cancel(); }
        };
        var result = Save(directory.Path, cancellation.Token, operations);
        Ensure(cancellation.IsCancellationRequested && File.Exists(result.Sha256Path),
            "Cancellation after commit must not report a failed save.");
        VerifyHashes(result);
    }

    private static void CleanupFailureAfterCommitIsAWarningNotSaveFailure()
    {
        using TestDirectory directory = new();
        var result = Save(directory.Path, operations: new FaultOperations { FailDirectoryCleanup = true });
        Ensure(result.CleanupIncomplete, "Successful publication with incomplete cleanup needs an explicit warning.");
        VerifyHashes(result);
        Ensure(Directory.GetFiles(directory.Path).Length == 4, "A valid committed report must be preserved.");
    }

    private static void FailedRollbackReportsRecoveryRequired()
    {
        using TestDirectory directory = new();
        var operations = new FaultOperations { FailMove = 2, FailPublishedJsonCleanup = true };
        var error = Throws<ReportFileSetRecoveryException>(() => Save(directory.Path, operations: operations));
        Ensure(error.Message.StartsWith("REPORT_FILE_SET_RECOVERY_REQUIRED", StringComparison.Ordinal),
            "Recovery-required condition needs a stable safe message.");
        Ensure(Directory.GetFiles(directory.Path, "*_SHA256SUMS.txt").Length == 0,
            "Failed rollback must not have a completion marker.");
        Ensure(Directory.GetFiles(directory.Path, "*.json").Length == 1, "Failed cleanup fixture must retain its partial JSON.");
    }

    private static void CompletionMarkerIsAlwaysLast()
    {
        using TestDirectory directory = new();
        List<string> moved = [];
        var operations = new FaultOperations
        {
            AfterMove = (number, path) =>
            {
                moved.Add(path);
                Ensure(Directory.GetFiles(directory.Path, "*_SHA256SUMS.txt").Length == (number == 4 ? 1 : 0),
                    "Checksum manifest must be visible only after all three data files.");
            }
        };
        Save(directory.Path, operations: operations);
        Ensure(moved.Count == 4 && moved[3].EndsWith("_SHA256SUMS.txt", StringComparison.Ordinal),
            "Manifest must be the fourth publication.");
    }

    private static void UnexpectedStageContentIsNotRecursivelyDeleted()
    {
        using TestDirectory directory = new();
        string? foreign = null;
        var operations = new FaultOperations
        {
            AfterWrite = (number, path) =>
            {
                if (number == 1)
                {
                    foreign = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "unrelated.txt");
                    File.WriteAllText(foreign, "preserve-this");
                }
            }
        };
        var result = Save(directory.Path, operations: operations);
        Ensure(result.CleanupIncomplete && foreign is not null && File.ReadAllText(foreign) == "preserve-this",
            "Cleanup must never recursively delete unexpected files.");
        VerifyHashes(result);
    }

    private static void RejectsUnsafeBaseNamesBeforeFilesystemWrites()
    {
        using TestDirectory directory = new();
        foreach (string bad in new[] { "../escape", "C:\\report", "report\nname", "CON", "nul.txt", "LPT1", "COM9", "file.", "file ", new string('x', 161) })
            Throws<ArgumentException>(() => LocalReportFileSetWriter.Write(directory.Path, bad, Json, Csv, Html));
        AssertEmpty(directory.Path);
    }

    private static void RejectsInvalidUtf16WithoutLeavingPartialFiles()
    {
        using TestDirectory directory = new();
        Throws<EncoderFallbackException>(() => LocalReportFileSetWriter.Write(directory.Path, Name,
            new string((char)0xd800, 1), Csv, Html));
        AssertEmpty(directory.Path);
    }

    private static InternalProxyRouteComparisonRunReportExportResult Save(
        string directory, CancellationToken token = default, ReportFileOperations? operations = null) =>
        LocalReportFileSetWriter.Write(directory, Name, Json, Csv, Html, token, operations);

    private static void VerifyHashes(InternalProxyRouteComparisonRunReportExportResult result)
    {
        Ensure(result.Sha256.Count == 3, "Expected three hashes.");
        string manifest = File.ReadAllText(result.Sha256Path);
        foreach ((string file, string expected) in result.Sha256)
        {
            string actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                System.IO.Path.Combine(result.OutputDirectory, file)))).ToLowerInvariant();
            Ensure(actual == expected && manifest.Contains(expected + "  " + file, StringComparison.Ordinal),
                "Manifest and actual bytes must match.");
        }
    }
    private static void AssertEmpty(string directory) =>
        Ensure(Directory.GetFileSystemEntries(directory).Length == 0, "No owned outputs, locks or stages should remain.");
    private static TException Throws<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException("Expected " + typeof(TException).Name);
    }
    private static void Ensure(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class FaultOperations : ReportFileOperations
    {
        internal int FailWrite { get; init; }
        internal int FailMove { get; init; }
        internal bool FailDirectoryCleanup { get; init; }
        internal bool FailPublishedJsonCleanup { get; init; }
        internal Action<int, string>? BeforeMove { get; init; }
        internal Action<int, string>? AfterMove { get; init; }
        internal Action<int, string>? AfterWrite { get; init; }
        private int _writes;
        private int _moves;
        internal override void WriteNewText(string path, string content, Encoding encoding)
        {
            base.WriteNewText(path, content, encoding);
            _writes++;
            AfterWrite?.Invoke(_writes, path);
            if (_writes == FailWrite) throw new IOException("synthetic partial-stage write failure");
        }
        internal override void MoveNew(string source, string destination)
        {
            _moves++;
            BeforeMove?.Invoke(_moves, destination);
            if (_moves == FailMove) throw new IOException("synthetic publish failure");
            base.MoveNew(source, destination);
            AfterMove?.Invoke(_moves, destination);
        }
        internal override void DeleteFile(string path)
        {
            if (FailPublishedJsonCleanup && path.EndsWith(".json", StringComparison.Ordinal))
                throw new IOException("synthetic rollback failure");
            base.DeleteFile(path);
        }
        internal override void DeleteDirectory(string path)
        {
            if (FailDirectoryCleanup) throw new IOException("synthetic staging cleanup failure");
            base.DeleteDirectory(path);
        }
    }
    private sealed class TestDirectory : IDisposable
    {
        internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "WlanReportFileSetTests", Guid.NewGuid().ToString("N"));
        internal TestDirectory() => Directory.CreateDirectory(Path);
        public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, recursive: true); }
    }
}
