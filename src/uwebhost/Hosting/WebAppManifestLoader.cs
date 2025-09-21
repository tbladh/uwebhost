using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace uwebhost.Hosting;

internal static class WebAppManifestLoader
{
    private const string ManifestFileName = "manifest.json";
    private const string DefaultDescription = "No description provided.";
    private const string DefaultImage = "/assets/icons/favicon-256x256.png";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static HostedApplication Load(string directoryPath, string directoryName)
    {
        WebAppManifest? manifest = null;
        var manifestPath = Path.Combine(directoryPath, ManifestFileName);

        if (File.Exists(manifestPath))
        {
            try
            {
                using var stream = File.OpenRead(manifestPath);
                manifest = JsonSerializer.Deserialize<WebAppManifest>(stream, SerializerOptions);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load manifest for '{directoryName}': {ex.Message}");
            }
        }

        var name = !string.IsNullOrWhiteSpace(manifest?.Name) ? manifest!.Name!.Trim() : directoryName;
        var description = !string.IsNullOrWhiteSpace(manifest?.Description)
            ? manifest!.Description!.Trim()
            : DefaultDescription;
        var imageUrl = ResolveImageUrl(directoryName, manifest?.Image);
        var tags = NormalizeTags(manifest?.Tags);
        var url = $"/{Uri.EscapeDataString(directoryName)}/";

        return new HostedApplication(
            DirectoryName: directoryName,
            DisplayName: name,
            Description: description,
            ImageUrl: imageUrl,
            Tags: tags,
            Url: url);
    }

    private static IReadOnlyList<string> NormalizeTags(IEnumerable<string>? tags)
    {
        if (tags is null)
        {
            return Array.Empty<string>();
        }

        return tags
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ResolveImageUrl(string directoryName, string? image)
    {
        if (string.IsNullOrWhiteSpace(image))
        {
            return DefaultImage;
        }

        var trimmed = image.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            return trimmed;
        }

        if (trimmed.StartsWith('/'))
        {
            return trimmed.Replace("\\", "/", StringComparison.Ordinal);
        }

        var segments = trimmed
            .Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(segment => Uri.EscapeDataString(segment));

        var encodedDirectory = Uri.EscapeDataString(directoryName);
        return $"/{encodedDirectory}/{string.Join('/', segments)}";
    }
}
