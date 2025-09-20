using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using uwebhost.Rendering;
using uwebhost.Rendering.Models;
using uwebhost.Utilities;

namespace uwebhost.Hosting;

internal sealed class RequestRouter
{
    private static readonly HashSet<string> HiddenRootDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "templates"
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
        using var connection = client;

        try
        {
            using var networkStream = connection.GetStream();
            using var reader = new StreamReader(networkStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            var parts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                await WriteStatusAsync(networkStream, "400 Bad Request", "Malformed request.", head: false).ConfigureAwait(false);
                return;
            }

            var method = parts[0];
            var rawTarget = parts[1];
            var isHead = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && !isHead)
            {
                await WriteStatusAsync(networkStream, "405 Method Not Allowed", "Only GET and HEAD are supported.", isHead).ConfigureAwait(false);
                return;
            }

            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync().ConfigureAwait(false)))
            {
                // Headers ignored.
            }

            var path = rawTarget.Split('?', 2)[0];
            if (string.IsNullOrEmpty(path))
            {
                path = "/";
            }

            if (path.Contains("..", StringComparison.Ordinal))
            {
                await WriteStatusAsync(networkStream, "400 Bad Request", "Invalid path.", isHead).ConfigureAwait(false);
                return;
            }

            if (path == "/" || string.Equals(path, "/index.html", StringComparison.OrdinalIgnoreCase))
            {
                await HandleHomeAsync(networkStream, isHead).ConfigureAwait(false);
                return;
            }

            string decodedPath;
            try
            {
                decodedPath = Uri.UnescapeDataString(path);
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
        catch (Exception ex)
        {
            Console.WriteLine($"Request failed: {ex.Message}");
        }
    }

    private async Task HandleHomeAsync(Stream stream, bool isHead)
    {
        var projects = Directory.Exists(_wwwRoot)
            ? Directory.GetDirectories(_wwwRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Where(name => !HiddenRootDirectories.Contains(name!))
                .OrderBy(name => name, _nameComparer)
                .Select(name => name!)
                .ToList()
            : new List<string>();

        var content = _renderer.RenderHomePage(_port, projects);
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
}
