using System.Text.Json;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Security;

namespace WlanLivePathTester.Core.Configuration;

public static class TargetConfigurationLoader
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static IReadOnlyList<MeasurementTargetDefinition> LoadFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        TargetConfigurationDocument document =
            JsonSerializer.Deserialize<TargetConfigurationDocument>(json, SerializerOptions)
            ?? throw new InvalidDataException("설정 문서를 읽을 수 없습니다.");

        if (document.SchemaVersion != 1)
        {
            throw new InvalidDataException($"지원하지 않는 schemaVersion입니다: {document.SchemaVersion}");
        }

        TargetDefaults defaults = document.Defaults ?? new TargetDefaults();
        List<MeasurementTargetDefinition> targets = [];

        AddTargets(targets, document.InternalTargets, NetworkPathKind.Internal, defaults);
        AddTargets(targets, document.ExternalTargets, NetworkPathKind.External, defaults);

        if (targets.Count == 0)
        {
            throw new InvalidDataException("측정 대상이 하나도 없습니다.");
        }

        foreach (MeasurementTargetDefinition target in targets)
        {
            IReadOnlyList<string> errors = TargetValidator.Validate(target);
            if (errors.Count > 0)
            {
                throw new InvalidDataException(
                    $"측정 대상 '{target.Name}' 설정 오류: {string.Join(" ", errors)}");
            }
        }

        return targets;
    }

    private static void AddTargets(
        ICollection<MeasurementTargetDefinition> destination,
        IEnumerable<TargetItem>? source,
        NetworkPathKind pathKind,
        TargetDefaults defaults)
    {
        if (source is null)
        {
            return;
        }

        foreach (TargetItem item in source)
        {
            IReadOnlyList<string>? allowedRedirectHosts = item.AllowedRedirectHosts?
                .Where(host => !string.IsNullOrWhiteSpace(host))
                .Select(host => host.Trim().TrimEnd('.'))
                .ToArray();

            destination.Add(new MeasurementTargetDefinition(
                Name: item.Name ?? string.Empty,
                Url: item.Url ?? string.Empty,
                PathKind: pathKind,
                RequireProxy: item.RequireProxy ?? false,
                RequireDirect: item.RequireDirect ?? false,
                MaxBytes: item.MaxBytes ?? defaults.MaxBytes,
                TimeoutSeconds: item.TimeoutSeconds ?? defaults.TimeoutSeconds,
                Streams: item.Streams ?? defaults.Streams,
                MaxRedirects: item.MaxRedirects ?? defaults.MaxRedirects,
                AllowedRedirectHosts: allowedRedirectHosts));
        }
    }

    private sealed class TargetConfigurationDocument
    {
        public int SchemaVersion { get; init; }
        public TargetDefaults? Defaults { get; init; }
        public List<TargetItem>? InternalTargets { get; init; }
        public List<TargetItem>? ExternalTargets { get; init; }
    }

    private sealed class TargetDefaults
    {
        public int TimeoutSeconds { get; init; } = 30;
        public long MaxBytes { get; init; } = 100 * 1024 * 1024;
        public int Streams { get; init; } = 1;
        public int MaxRedirects { get; init; } = 5;
    }

    private sealed class TargetItem
    {
        public string? Name { get; init; }
        public string? Url { get; init; }
        public bool? RequireProxy { get; init; }
        public bool? RequireDirect { get; init; }
        public long? MaxBytes { get; init; }
        public int? TimeoutSeconds { get; init; }
        public int? Streams { get; init; }
        public int? MaxRedirects { get; init; }
        public List<string>? AllowedRedirectHosts { get; init; }
    }
}
