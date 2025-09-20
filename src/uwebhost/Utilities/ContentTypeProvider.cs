using System.Collections.Generic;

namespace uwebhost.Utilities;

internal sealed class ContentTypeProvider
{
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".htm"] = "text/html; charset=utf-8",
        [".css"] = "text/css",
        [".js"] = "application/javascript",
        [".mjs"] = "application/javascript",
        [".json"] = "application/json",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".txt"] = "text/plain; charset=utf-8",
        [".xml"] = "application/xml",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".ttf"] = "font/ttf",
        [".otf"] = "font/otf",
        [".wasm"] = "application/wasm",
        [".mp4"] = "video/mp4",
        [".webm"] = "video/webm",
        [".webmanifest"] = "application/manifest+json",
        [".csv"] = "text/csv; charset=utf-8"
    };

    public string GetContentType(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return "application/octet-stream";
        }

        return ContentTypes.TryGetValue(extension, out var value)
            ? value
            : "application/octet-stream";
    }
}
