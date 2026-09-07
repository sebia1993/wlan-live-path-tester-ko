using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WlanLivePathTester.Core.Models;
using WlanLivePathTester.Core.Security;

namespace WlanLivePathTester.Core.Configuration;

public sealed record LoadedTargetConfiguration(bool EnforceApprovedTargets, IReadOnlyList<MeasurementTargetDefinition> Targets);

public static class TargetConfigurationLoader
{
    public const int MaximumTargetCount = 64;
    private const int MaximumJsonDepth = 16;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = MaximumJsonDepth
    };

    public static IReadOnlyList<MeasurementTargetDefinition> LoadFromJson(string json) =>
        LoadWithPolicyFromJson(json).Targets;

    public static LoadedTargetConfiguration LoadWithPolicyFromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        if (json.Length > TargetConfigurationFileReader.MaximumConfigurationBytes)
            throw new InvalidDataException("승인 대상 설정 JSON의 원문 크기 제한을 초과했습니다.");
        try
        {
            if (StrictUtf8.GetByteCount(json) > TargetConfigurationFileReader.MaximumConfigurationBytes)
                throw new InvalidDataException("승인 대상 설정 JSON의 UTF-8 크기 제한을 초과했습니다.");
        }
        catch (EncoderFallbackException)
        {
            throw new InvalidDataException("승인 대상 설정 JSON에 잘못된 유니코드 문자가 있습니다.");
        }

        TargetConfigurationDocument document;
        try
        {
            using JsonDocument syntax = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
                MaxDepth = MaximumJsonDepth
            });
            ValidateUniqueProperties(syntax.RootElement);
            document = JsonSerializer.Deserialize<TargetConfigurationDocument>(json, SerializerOptions)
                ?? throw new InvalidDataException("설정 문서를 읽을 수 없습니다.");
        }
        catch (JsonException)
        {
            // JsonException may otherwise include arbitrary property names,
            // paths or tokens supplied by the configuration document.
            throw new JsonException("설정 JSON의 형식·속성·중복 또는 깊이가 허용 범위를 벗어납니다.");
        }

        if (document.SchemaVersion != 1)
            throw new InvalidDataException("지원하지 않는 schemaVersion입니다.");
        TargetDefaults defaults = document.Defaults ?? new TargetDefaults();
        List<MeasurementTargetDefinition> targets = [];
        AddTargets(targets, document.InternalTargets, NetworkPathKind.Internal, defaults);
        AddTargets(targets, document.ExternalTargets, NetworkPathKind.External, defaults);
        if (targets.Count == 0) throw new InvalidDataException("측정 대상이 하나도 없습니다.");

        HashSet<string> targetKeys = new(StringComparer.Ordinal);
        foreach (MeasurementTargetDefinition target in targets)
        {
            IReadOnlyList<string> errors = TargetValidator.ValidateDefinition(target);
            if (errors.Count > 0)
                throw new InvalidDataException($"측정 대상 설정 오류: {string.Join(" ", errors)}");
            Uri targetUri = new(target.Url, UriKind.Absolute);
            string key = $"{target.PathKind}|{NetworkInputBoundary.CreateHttpKey(targetUri)}";
            if (!targetKeys.Add(key))
                throw new InvalidDataException("같은 경로 유형과 URL이 중복되었습니다.");
        }
        return new(document.EnforceApprovedTargets, targets.AsReadOnly());
    }

    private static void ValidateUniqueProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            // Match the serializer's case-insensitive binding, including names
            // spelled with JSON escapes. First-wins/last-wins is not a policy.
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (!seen.Add(property.Name)) throw new JsonException("중복 설정 속성입니다.");
                ValidateUniqueProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (JsonElement item in element.EnumerateArray()) ValidateUniqueProperties(item);
    }

    private static void AddTargets(ICollection<MeasurementTargetDefinition> destination,
        IEnumerable<TargetItem?>? source, NetworkPathKind pathKind, TargetDefaults defaults)
    {
        if (source is null) return;
        foreach (TargetItem? item in source)
        {
            if (item is null) throw new InvalidDataException("측정 대상 항목은 null일 수 없습니다.");
            if (destination.Count >= MaximumTargetCount)
                throw new InvalidDataException($"측정 대상은 최대 {MaximumTargetCount}개까지 허용합니다.");
            // Do not filter empty hosts or Trim fields here. The validator must
            // see their original length/control characters and reject them.
            IReadOnlyList<string>? hosts = item.AllowedRedirectHosts is null ? null
                : Array.AsReadOnly(item.AllowedRedirectHosts.Select(host => host ?? string.Empty).ToArray());
            destination.Add(new MeasurementTargetDefinition(
                Name: item.Name ?? string.Empty, Url: item.Url ?? string.Empty, PathKind: pathKind,
                RequireProxy: item.RequireProxy ?? pathKind == NetworkPathKind.External,
                RequireDirect: item.RequireDirect ?? pathKind == NetworkPathKind.Internal,
                MaxBytes: item.MaxBytes ?? defaults.MaxBytes,
                TimeoutSeconds: item.TimeoutSeconds ?? defaults.TimeoutSeconds,
                Streams: item.Streams ?? defaults.Streams,
                MaxRedirects: item.MaxRedirects ?? defaults.MaxRedirects,
                AllowedRedirectHosts: hosts));
        }
    }

    private sealed class TargetConfigurationDocument
    {
        public int SchemaVersion { get; init; }
        public bool EnforceApprovedTargets { get; init; }
        public TargetDefaults? Defaults { get; init; }
        public List<TargetItem?>? InternalTargets { get; init; }
        public List<TargetItem?>? ExternalTargets { get; init; }
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
        public List<string?>? AllowedRedirectHosts { get; init; }
    }
}
