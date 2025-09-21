using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace uwebhost.Hosting;

internal sealed class ManifestResponse
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }

    [JsonPropertyName("image")]
    public required string Image { get; init; }

    [JsonPropertyName("tags")]
    public required IReadOnlyList<string> Tags { get; init; }

    [JsonPropertyName("hasManifest")]
    public bool HasManifest { get; init; }
}

internal sealed class ManifestUpdateRequest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("image")]
    public ManifestImagePayload? Image { get; set; }

    [JsonPropertyName("removeImage")]
    public bool RemoveImage { get; set; }
}

internal sealed class ManifestImagePayload
{
    [JsonPropertyName("tempId")]
    public string? TempId { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("contentBase64")]
    public string? ContentBase64 { get; set; }
}

internal sealed class TemporaryUploadRequest
{
    [JsonPropertyName("appId")]
    public string? AppId { get; set; }

    [JsonPropertyName("fileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("contentBase64")]
    public string? ContentBase64 { get; set; }
}

internal sealed class TemporaryUploadResponse
{
    [JsonPropertyName("tempId")]
    public required string TempId { get; init; }

    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; init; }
}
