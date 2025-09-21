using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using uwebhost.Rendering;
using uwebhost.Rendering.Models;
using uwebhost.Utilities;

namespace uwebhost.Hosting;

internal sealed class RequestRouter
{
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 8 * 1024 * 1024;

    private static ReadOnlySpan<byte> HeaderTerminator => "\r\n\r\n"u8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _wwwRoot;
    private readonly string _wwwFullPath;
    private readonly int _port;
    private readonly PageRenderer _renderer;
    private readonly ContentTypeProvider _contentTypeProvider;
    private readonly StringComparison _pathComparison;
    private readonly StringComparer _nameComparer;

    public RequestRouter(string wwwRoot, int port, PageRenderer renderer, ContentTypeProvider contentTypeProvider)
    {
        _wwwRoot = wwwRoot;
        _wwwFullPath = Path.GetFullPath(wwwRoot);
        _port = port;
        _renderer = renderer;
        _contentTypeProvider = contentTypeProvider;
        _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        _nameComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    }

    public async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        NetworkStream? networkStream = null;
        try
        {
            networkStream = client.GetStream();
            var request = await ReadRequestAsync(networkStream, cancellationToken).ConfigureAwait(false);
            if (request is null)
            {
                return;
            }

            if (request.Path.Contains("..", StringComparison.Ordinal))
            {
                await WriteStatusAsync(networkStream, "400 Bad Request", "Invalid path.", head: false).ConfigureAwait(false);
                return;
            }

            if (request.Path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleApiRequestAsync(networkStream, request, cancellationToken).ConfigureAwait(false);
                return;
            }

            var isHead = string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase);
            if (!string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) && !isHead)
            {
                await WriteStatusAsync(networkStream, "405 Method Not Allowed", "Only GET and HEAD are supported.", isHead).ConfigureAwait(false);
                return;
            }

            if (request.Path == "/" || string.Equals(request.Path, "/index.html", StringComparison.OrdinalIgnoreCase))
            {
                await HandleHomeAsync(networkStream, isHead).ConfigureAwait(false);
                return;
            }

            string decodedPath;
            try
            {
                decodedPath = Uri.UnescapeDataString(request.Path);
            }
            catch (UriFormatException)
            {
                await WriteStatusAsync(networkStream, "400 Bad Request", "Unable to decode request path.", isHead).ConfigureAwait(false);
                return;
            }

            var relative = decodedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(_wwwRoot, relative));

            if (!fullPath.StartsWith(_wwwFullPath, _pathComparison))
            {
                await WriteStatusAsync(networkStream, "403 Forbidden", "Access denied.", isHead).ConfigureAwait(false);
                return;
            }

            if (Directory.Exists(fullPath))
            {
                await HandleDirectoryAsync(networkStream, decodedPath, fullPath, isHead, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (File.Exists(fullPath))
            {
                var contentType = _contentTypeProvider.GetContentType(Path.GetExtension(fullPath));
                await HttpResponseWriter.WriteFileAsync(networkStream, fullPath, contentType, isHead, cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteStatusAsync(networkStream, "404 Not Found", "The requested resource was not found.", isHead).ConfigureAwait(false);
        }
        catch (HttpStatusException ex)
        {
            if (networkStream is null || !networkStream.CanWrite)
            {
                try
                {
                    networkStream = client.GetStream();
                }
                catch
                {
                    return;
                }
            }

            try
            {
                if (ex.PreferJson)
                {
                    await WriteJsonAsync(networkStream, ex.Status, new { error = ex.Message }).ConfigureAwait(false);
                }
                else
                {
                    await WriteStatusAsync(networkStream, ex.Status, ex.Message, head: false).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore secondary failures.
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Request failed: {ex.Message}");
        }
        finally
        {
            networkStream?.Dispose();
            client.Dispose();
        }
    }

    private async Task HandleHomeAsync(Stream stream, bool isHead)
    {
        var applications = Directory.Exists(_wwwRoot)
            ? Directory.GetDirectories(_wwwRoot)
                .Select(path => new { Path = path, Name = Path.GetFileName(path) })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .Where(item => !IsSystemDirectory(item.Name!))
                .Select(item => WebAppManifestLoader.Load(item.Path, item.Name!))
                .OrderBy(app => app.DisplayName, _nameComparer)
                .ToList()
            : new List<HostedApplication>();

        var content = _renderer.RenderHomePage(_port, applications);
        await HttpResponseWriter.WriteHtmlAsync(stream, "200 OK", content, isHead).ConfigureAwait(false);
    }

    private async Task HandleDirectoryAsync(Stream stream, string decodedPath, string fullPath, bool isHead, CancellationToken cancellationToken)
    {
        if (!decodedPath.EndsWith('/'))
        {
            var location = decodedPath + "/";
            var body = _renderer.RenderRedirectPage(location);
            await HttpResponseWriter.WriteRedirectAsync(stream, "301 Moved Permanently", location, body, isHead).ConfigureAwait(false);
            return;
        }

        var indexPath = Path.Combine(fullPath, "index.html");
        if (File.Exists(indexPath))
        {
            var contentType = _contentTypeProvider.GetContentType(Path.GetExtension(indexPath));
            await HttpResponseWriter.WriteFileAsync(stream, indexPath, contentType, isHead, cancellationToken).ConfigureAwait(false);
            return;
        }

        var directories = Directory.GetDirectories(fullPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, _nameComparer)
            .Select(name => name!)
            .Select(name => new DirectoryEntry(name + "/", CombineUrl(decodedPath, name, appendSlash: true), true))
            .ToList();

        var files = Directory.GetFiles(fullPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, _nameComparer)
            .Select(name => name!)
            .Select(name => new DirectoryEntry(name, CombineUrl(decodedPath, name, appendSlash: false), false))
            .ToList();

        var parentUrl = GetParentUrl(decodedPath);
        var content = _renderer.RenderDirectoryListing(decodedPath, parentUrl, directories, files);
        await HttpResponseWriter.WriteHtmlAsync(stream, "200 OK", content, isHead).ConfigureAwait(false);
    }

    private async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var headerBuffer = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            int headerEndIndex = -1;
            while (headerEndIndex == -1)
            {
                var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
                if (bytesRead == 0)
                {
                    return headerBuffer.Length == 0 ? null : throw new HttpStatusException("400 Bad Request", "Unexpected end of stream.");
                }

                headerBuffer.Write(buffer, 0, bytesRead);
                if (headerBuffer.Length > MaxHeaderBytes)
                {
                    throw new HttpStatusException("431 Request Header Fields Too Large", "Headers exceed allowed size.");
                }

                headerEndIndex = FindHeaderTerminator(headerBuffer);
            }

            var headerLength = headerEndIndex + HeaderTerminator.Length;
            var headerBytes = headerBuffer.GetBuffer();
            var headerText = Encoding.ASCII.GetString(headerBytes, 0, headerLength);

            var remainder = (int)headerBuffer.Length - headerLength;
            var bodyStream = new MemoryStream();
            if (remainder > 0)
            {
                bodyStream.Write(headerBytes, headerLength, remainder);
            }

            var lines = headerText.Split(new[] { "\r\n" }, StringSplitOptions.None);
            if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
            {
                throw new HttpStatusException("400 Bad Request", "Malformed request line.");
            }

            var requestLineParts = lines[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestLineParts.Length < 3)
            {
                throw new HttpStatusException("400 Bad Request", "Malformed request line.");
            }

            var method = requestLineParts[0];
            var rawTarget = requestLineParts[1];
            var path = rawTarget.Split('?', 2)[0];
            if (string.IsNullOrEmpty(path))
            {
                path = "/";
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrEmpty(line))
                {
                    continue;
                }

                var separatorIndex = line.IndexOf(':');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var name = line[..separatorIndex].Trim();
                var value = line[(separatorIndex + 1)..].Trim();
                headers[name] = value;
            }

            var contentLength = 0;
            if (headers.TryGetValue("Content-Length", out var lengthValue) && !string.IsNullOrEmpty(lengthValue))
            {
                if (!int.TryParse(lengthValue, out contentLength) || contentLength < 0)
                {
                    throw new HttpStatusException("400 Bad Request", "Invalid Content-Length header.");
                }

                if (contentLength > MaxBodyBytes)
                {
                    throw new HttpStatusException("413 Payload Too Large", "Request body exceeds configured limit.");
                }
            }

            var alreadyBuffered = (int)bodyStream.Length;
            if (contentLength > alreadyBuffered)
            {
                var remaining = contentLength - alreadyBuffered;
                while (remaining > 0)
                {
                    var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        throw new HttpStatusException("400 Bad Request", "Unexpected end of request body.");
                    }

                    bodyStream.Write(buffer, 0, read);
                    remaining -= read;
                }
            }

            var bodyBytes = bodyStream.ToArray();
            return new HttpRequest(method, rawTarget, path, headers, bodyBytes);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private async Task HandleApiRequestAsync(Stream stream, HttpRequest request, CancellationToken cancellationToken)
    {
        if (string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpStatusException("405 Method Not Allowed", "HEAD is not supported for API endpoints.", preferJson: true);
        }

        var segments = request.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 3 && string.Equals(segments[1], "apps", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 4 && string.Equals(segments[3], "manifest", StringComparison.OrdinalIgnoreCase))
            {
                var appId = segments[2];
                EnsureValidAppId(appId);

                if (string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleManifestGetAsync(stream, appId).ConfigureAwait(false);
                    return;
                }

                if (string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase))
                {
                    await HandleManifestPostAsync(stream, appId, request.Body).ConfigureAwait(false);
                    return;
                }

                throw new HttpStatusException("405 Method Not Allowed", "Only GET and POST are allowed for manifest endpoints.", preferJson: true);
            }
        }
        else if (segments.Length >= 3 && string.Equals(segments[1], "uploads", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 3 && string.Equals(request.Method, "POST", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[2], "temp", StringComparison.OrdinalIgnoreCase))
            {
                await HandleTemporaryUploadPostAsync(stream, request.Body).ConfigureAwait(false);
                return;
            }

            if (segments.Length == 4 && string.Equals(request.Method, "DELETE", StringComparison.OrdinalIgnoreCase) && string.Equals(segments[2], "temp", StringComparison.OrdinalIgnoreCase))
            {
                await HandleTemporaryUploadDeleteAsync(stream, segments[3]).ConfigureAwait(false);
                return;
            }
        }

        throw new HttpStatusException("404 Not Found", "API endpoint not found.", preferJson: true);
    }

    private async Task HandleManifestGetAsync(Stream stream, string appId)
    {
        var appDirectory = Path.Combine(_wwwRoot, appId);
        if (!Directory.Exists(appDirectory))
        {
            throw new HttpStatusException("404 Not Found", "Application directory was not found.", preferJson: true);
        }

        var hostedApp = WebAppManifestLoader.Load(appDirectory, appId, out var hasManifest);
        var response = new ManifestResponse
        {
            Id = appId,
            Name = hostedApp.DisplayName,
            Description = hostedApp.Description,
            Image = hostedApp.ImageUrl,
            Tags = hostedApp.Tags,
            HasManifest = hasManifest
        };

        await WriteJsonAsync(stream, "200 OK", response).ConfigureAwait(false);
    }

    private async Task HandleManifestPostAsync(Stream stream, string appId, byte[] body)
    {
        if (body.Length == 0)
        {
            throw new HttpStatusException("400 Bad Request", "Request body is required.", preferJson: true);
        }

        ManifestUpdateRequest? update;
        try
        {
            update = JsonSerializer.Deserialize<ManifestUpdateRequest>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new HttpStatusException("400 Bad Request", $"Invalid JSON payload: {ex.Message}", preferJson: true);
        }

        if (update is null)
        {
            throw new HttpStatusException("400 Bad Request", "Payload is empty.", preferJson: true);
        }

        var appDirectory = Path.Combine(_wwwRoot, appId);
        if (!Directory.Exists(appDirectory))
        {
            throw new HttpStatusException("404 Not Found", "Application directory was not found.", preferJson: true);
        }

        var currentManifest = WebAppManifestLoader.ReadManifest(appDirectory);
        var tags = WebAppManifestLoader.NormalizeTags(update.Tags);

        var imagePath = currentManifest?.Image;
        if (update.RemoveImage)
        {
            TryDeleteAppImage(appId, imagePath);
            imagePath = null;
        }
        else if (update.Image is not null)
        {
            var newImage = ProcessImageUpdate(appId, update.Image);
            if (!string.IsNullOrWhiteSpace(newImage))
            {
                imagePath = newImage;
            }
        }

        var manifest = new WebAppManifest
        {
            Name = string.IsNullOrWhiteSpace(update.Name) ? null : update.Name,
            Description = string.IsNullOrWhiteSpace(update.Description) ? null : update.Description,
            Image = imagePath,
            Tags = tags.ToList()
        };

        WebAppManifestLoader.SaveManifest(appDirectory, manifest);

        var hostedApp = WebAppManifestLoader.Load(appDirectory, appId, out var hasManifest);
        var response = new ManifestResponse
        {
            Id = appId,
            Name = hostedApp.DisplayName,
            Description = hostedApp.Description,
            Image = hostedApp.ImageUrl,
            Tags = hostedApp.Tags,
            HasManifest = hasManifest
        };

        await WriteJsonAsync(stream, "200 OK", response).ConfigureAwait(false);
    }

    private string? ProcessImageUpdate(string appId, ManifestImagePayload payload)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(payload.TempId))
            {
                var desiredName = string.IsNullOrWhiteSpace(payload.FileName) ? "image" : payload.FileName;
                return UploadManager.PromoteTemporaryUpload(_wwwRoot, appId, payload.TempId!, desiredName);
            }

            if (!string.IsNullOrWhiteSpace(payload.ContentBase64))
            {
                byte[] data;
                try
                {
                    data = Convert.FromBase64String(payload.ContentBase64);
                }
                catch (FormatException)
                {
                    throw new HttpStatusException("400 Bad Request", "Image payload is not valid Base64 content.", preferJson: true);
                }

                var fileName = string.IsNullOrWhiteSpace(payload.FileName) ? "image" : payload.FileName!;
                var staged = UploadManager.StageTemporaryUpload(_wwwRoot, fileName, data, appId);
                return UploadManager.PromoteTemporaryUpload(_wwwRoot, appId, staged.Identifier, fileName);
            }
        }
        catch (HttpStatusException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new HttpStatusException("400 Bad Request", ex.Message, preferJson: true);
        }

        return null;
    }

    private async Task HandleTemporaryUploadPostAsync(Stream stream, byte[] body)
    {
        if (body.Length == 0)
        {
            throw new HttpStatusException("400 Bad Request", "Request body is required.", preferJson: true);
        }

        TemporaryUploadRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<TemporaryUploadRequest>(body, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new HttpStatusException("400 Bad Request", $"Invalid JSON payload: {ex.Message}", preferJson: true);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.FileName) || string.IsNullOrWhiteSpace(request.ContentBase64))
        {
            throw new HttpStatusException("400 Bad Request", "File name and content are required.", preferJson: true);
        }

        byte[] data;
        try
        {
            data = Convert.FromBase64String(request.ContentBase64);
        }
        catch (FormatException)
        {
            throw new HttpStatusException("400 Bad Request", "Image payload is not valid Base64 content.", preferJson: true);
        }

        TemporaryUpload staged;
        try
        {
            staged = UploadManager.StageTemporaryUpload(_wwwRoot, request.FileName, data, request.AppId);
        }
        catch (Exception ex)
        {
            throw new HttpStatusException("400 Bad Request", ex.Message, preferJson: true);
        }

        var response = new TemporaryUploadResponse
        {
            TempId = staged.Identifier,
            FileName = staged.OriginalFileName,
            SizeBytes = staged.SizeBytes
        };

        await WriteJsonAsync(stream, "200 OK", response).ConfigureAwait(false);
    }

    private async Task HandleTemporaryUploadDeleteAsync(Stream stream, string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new HttpStatusException("400 Bad Request", "Upload identifier is required.", preferJson: true);
        }

        var deleted = UploadManager.TryDeleteTemporaryUpload(_wwwRoot, identifier);
        await WriteJsonAsync(stream, "200 OK", new { deleted }).ConfigureAwait(false);
    }

    private static void EnsureValidAppId(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId) || appId.Contains("../", StringComparison.Ordinal) || appId.Contains("..\\", StringComparison.Ordinal) || appId.Contains('/') || appId.Contains('\\'))
        {
            throw new HttpStatusException("400 Bad Request", "Invalid application identifier.", preferJson: true);
        }
    }

    private void TryDeleteAppImage(string appId, string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
        {
            return;
        }

        var encodedApp = $"/{Uri.EscapeDataString(appId)}/";
        if (!imagePath.StartsWith(encodedApp, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = imagePath[encodedApp.Length..];
        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(relative);
        }
        catch
        {
            return;
        }

        var absolutePath = Path.Combine(_wwwRoot, appId, decoded);
        if (!File.Exists(absolutePath))
        {
            return;
        }

        try
        {
            File.Delete(absolutePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to delete previous image '{absolutePath}': {ex.Message}");
        }
    }

    private static async Task WriteJsonAsync(Stream stream, string status, object payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, JsonOptions);
        await HttpResponseWriter.WriteBytesAsync(stream, status, "application/json; charset=utf-8", bytes, head: false).ConfigureAwait(false);
    }

    private static int FindHeaderTerminator(MemoryStream stream)
    {
        if (!stream.TryGetBuffer(out var segment))
        {
            var copy = stream.ToArray();
            return copy.AsSpan().IndexOf(HeaderTerminator);
        }

        return segment.AsSpan(0, (int)stream.Length).IndexOf(HeaderTerminator);
    }

    private static bool IsSystemDirectory(string name)
        => name.Length > 0 && (name[0] == '_' || name[0] == '.');

    private static string CombineUrl(string basePath, string name, bool appendSlash)
    {
        var prefix = basePath.EndsWith('/') ? basePath : basePath + "/";
        var encodedName = Uri.EscapeDataString(name);
        var combined = prefix + encodedName;
        if (appendSlash && !combined.EndsWith('/'))
        {
            combined += "/";
        }

        return combined;
    }

    private static string? GetParentUrl(string requestPath)
    {
        if (string.Equals(requestPath, "/", StringComparison.Ordinal))
        {
            return null;
        }

        var trimmed = requestPath.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash <= 0 ? "/" : trimmed[..lastSlash] + "/";
    }

    private Task WriteStatusAsync(Stream stream, string status, string message, bool head)
    {
        var body = _renderer.RenderStatusPage(status, message);
        return HttpResponseWriter.WriteStatusAsync(stream, status, body, head);
    }

    private sealed record HttpRequest(string Method, string RawTarget, string Path, IReadOnlyDictionary<string, string> Headers, byte[] Body);

    private sealed class HttpStatusException : Exception
    {
        public HttpStatusException(string status, string message, bool preferJson = false)
            : base(message)
        {
            Status = status;
            PreferJson = preferJson;
        }

        public string Status { get; }

        public bool PreferJson { get; }
    }
}
