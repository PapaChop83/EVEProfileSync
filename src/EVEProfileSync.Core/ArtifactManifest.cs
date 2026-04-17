using System.Text.Json;
using System.Text.Json.Serialization;

namespace EVEProfileSync.Core;

public sealed class ArtifactManifestRoot
{
    public string Version { get; init; } = "2026.1";

    public IReadOnlyList<ArtifactMappingRule> Mappings { get; init; } = Array.Empty<ArtifactMappingRule>();
}

public sealed class ArtifactMappingRule
{
    public SyncOption Option { get; init; }

    public ArtifactKind ArtifactKind { get; init; }

    public string Description { get; init; } = string.Empty;

    public IReadOnlyList<string> FileNamePatterns { get; init; } = Array.Empty<string>();
}

public static class ArtifactManifestLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static ArtifactManifestRoot LoadFromFile(string manifestPath)
    {
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException("Artifact mapping manifest was not found.", manifestPath);
        }

        var json = File.ReadAllText(manifestPath);
        return JsonSerializer.Deserialize<ArtifactManifestRoot>(json, JsonOptions)
            ?? throw new InvalidOperationException("Artifact mapping manifest could not be deserialized.");
    }
}
