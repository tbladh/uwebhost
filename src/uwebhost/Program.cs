using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;

namespace uwebhost;

internal static class Program
{
    private const string ServerName = "uWebHost/0.1";

    private static async Task Main(string[] args)
    {
        var port = 5000;
        if (args.Length > 0 && int.TryParse(args[0], out var requestedPort) && requestedPort is > 0 and <= 65535)
        {
            port = requestedPort;
        }

        var wwwRoot = Path.Combine(AppContext.BaseDirectory, "www");
        if (!Directory.Exists(wwwRoot))
        {
            Directory.CreateDirectory(wwwRoot);
            Console.WriteLine($"Created www directory at {wwwRoot}");
        }

        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Console.WriteLine($"uWebHost serving {wwwRoot}");
        Console.WriteLine($"Browse to http://localhost:{port}/");
        TryLaunchBrowser(port);

        try
        {
            while (true)
            {
                var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, wwwRoot, port));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Server stopped: {ex.Message}");
        }
    }

    private static void TryLaunchBrowser(int port)
    {
        var url = $"http://localhost:{port}/";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to open browser automatically: {ex.Message}");
        }
    }

    private static async Task HandleClientAsync(TcpClient client, string wwwRoot, int port)
    {
        using var connection = client;

        try
        {
            using var networkStream = connection.GetStream();
            var reader = new StreamReader(networkStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
            var requestLine = await reader.ReadLineAsync().ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            var requestParts = requestLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (requestParts.Length < 2)
            {
                await WriteSimpleResponseAsync(networkStream, "400 Bad Request", "Malformed request.").ConfigureAwait(false);
                return;
            }

            var method = requestParts[0];
            var rawTarget = requestParts[1];
            var isHead = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);

            if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && !isHead)
            {
                await WriteSimpleResponseAsync(networkStream, "405 Method Not Allowed", "Only GET and HEAD are supported.", isHead).ConfigureAwait(false);
                return;
            }

            string? headerLine;
            while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync().ConfigureAwait(false)))
            {
                // Ignoring headers for this minimal server implementation.
            }

            var path = rawTarget.Split('?', 2)[0];
            if (string.IsNullOrEmpty(path))
            {
                path = "/";
            }

            if (path.Contains("..", StringComparison.Ordinal))
            {
                await WriteSimpleResponseAsync(networkStream, "400 Bad Request", "Invalid path.", isHead).ConfigureAwait(false);
                return;
            }

            if (path == "/")
            {
                var homeHtml = BuildHomePage(wwwRoot, port);
                await WriteResponseAsync(networkStream, "200 OK", "text/html; charset=utf-8", homeHtml, isHead).ConfigureAwait(false);
                return;
            }

            string decodedPath;
            try
            {
                decodedPath = Uri.UnescapeDataString(path);
            }
            catch (UriFormatException)
            {
                await WriteSimpleResponseAsync(networkStream, "400 Bad Request", "Unable to decode request path.", isHead).ConfigureAwait(false);
                return;
            }

            var relative = decodedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(wwwRoot, relative));
            var wwwFullPath = Path.GetFullPath(wwwRoot);
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            if (!fullPath.StartsWith(wwwFullPath, comparison))
            {
                await WriteSimpleResponseAsync(networkStream, "403 Forbidden", "Access denied.", isHead).ConfigureAwait(false);
                return;
            }

            if (Directory.Exists(fullPath))
            {
                if (!decodedPath.EndsWith("/", StringComparison.Ordinal))
                {
                    await WriteRedirectResponseAsync(networkStream, decodedPath + "/", isHead).ConfigureAwait(false);
                    return;
                }

                var indexPath = Path.Combine(fullPath, "index.html");
                if (File.Exists(indexPath))
                {
                    await WriteFileResponseAsync(networkStream, indexPath, isHead).ConfigureAwait(false);
                }
                else
                {
                    var listingHtml = BuildDirectoryListing(decodedPath, fullPath);
                    await WriteResponseAsync(networkStream, "200 OK", "text/html; charset=utf-8", listingHtml, isHead).ConfigureAwait(false);
                }

                return;
            }

            if (File.Exists(fullPath))
            {
                await WriteFileResponseAsync(networkStream, fullPath, isHead).ConfigureAwait(false);
                return;
            }

            await WriteSimpleResponseAsync(networkStream, "404 Not Found", "The requested resource was not found.", isHead).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Request failed: {ex.Message}");
        }
    }

    private static string BuildHomePage(string wwwRoot, int port)
    {
        var directories = Directory.Exists(wwwRoot)
            ? Directory.GetDirectories(wwwRoot)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine("  <title>uWebHost</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    :root { color-scheme: light dark; }");
        sb.AppendLine("    body { font-family: system-ui, sans-serif; margin: 2rem; background: #f5f5f5; color: #333; }");
        sb.AppendLine("    @media (prefers-color-scheme: dark) { body { background: #0f172a; color: #e2e8f0; } li { background: #1e293b; color: inherit; } }");
        sb.AppendLine("    h1 { margin-bottom: 1rem; }");
        sb.AppendLine("    p { margin-bottom: 1.5rem; }");
        sb.AppendLine("    ul { list-style: none; padding: 0; margin: 0; max-width: 36rem; }");
        sb.AppendLine("    li { display: flex; justify-content: space-between; align-items: center; background: #fff; padding: 0.75rem 1rem; margin-bottom: 0.75rem; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.08); }");
        sb.AppendLine("    .name { font-weight: 600; }");
        sb.AppendLine("    form { margin: 0; }");
        sb.AppendLine("    button { background: #2563eb; color: white; border: none; border-radius: 6px; padding: 0.4rem 1.2rem; cursor: pointer; font-size: 0.95rem; }");
        sb.AppendLine("    button:hover { background: #1d4ed8; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("  <h1>uWebHost</h1>");
        sb.AppendLine($"  <p>Listening on <code>http://localhost:{port}/</code></p>");

        if (!directories.Any())
        {
            sb.AppendLine("  <p>No projects found in the <code>www</code> folder yet. Add a project directory to get started.</p>");
        }
        else
        {
            sb.AppendLine("  <p>Select a project to open it in your browser.</p>");
            sb.AppendLine("  <ul>");

            foreach (var directory in directories)
            {
                var safeName = HtmlEncoder.Default.Encode(directory!);
                var urlSegment = Uri.EscapeDataString(directory!);
                var playUrl = $"/{urlSegment}/";
                sb.AppendLine("    <li>");
                sb.AppendLine($"      <span class=\"name\">{safeName}</span>");
                sb.AppendLine($"      <form method=\"get\" action=\"{playUrl}\">");
                sb.AppendLine("        <button type=\"submit\">Play</button>");
                sb.AppendLine("      </form>");
                sb.AppendLine("    </li>");
            }

            sb.AppendLine("  </ul>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static string BuildDirectoryListing(string requestPath, string directoryPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"utf-8\">");
        sb.AppendLine("  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        sb.AppendLine($"  <title>{HtmlEncoder.Default.Encode(requestPath)} - Listing</title>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine($"  <h1>Index of {HtmlEncoder.Default.Encode(requestPath)}</h1>");
        sb.AppendLine("  <ul>");

        if (!string.Equals(requestPath, "/", StringComparison.Ordinal))
        {
            var trimmed = requestPath.TrimEnd('/');
            var lastSlashIndex = trimmed.LastIndexOf('/');
            var parentPath = lastSlashIndex <= 0 ? "/" : trimmed[..lastSlashIndex] + "/";
            sb.AppendLine($"    <li><a href=\"{parentPath}\">Parent Directory</a></li>");
        }

        foreach (var dir in Directory.GetDirectories(directoryPath))
        {
            var name = Path.GetFileName(dir);
            var encodedName = HtmlEncoder.Default.Encode(name!);
            var href = requestPath + (requestPath.EndsWith("/", StringComparison.Ordinal) ? string.Empty : "/") + Uri.EscapeDataString(name!) + "/";
            sb.AppendLine($"    <li><a href=\"{href}\">{encodedName}/</a></li>");
        }

        foreach (var file in Directory.GetFiles(directoryPath))
        {
            var name = Path.GetFileName(file);
            var encodedName = HtmlEncoder.Default.Encode(name!);
            var href = requestPath + (requestPath.EndsWith("/", StringComparison.Ordinal) ? string.Empty : "/") + Uri.EscapeDataString(name!);
            sb.AppendLine($"    <li><a href=\"{href}\">{encodedName}</a></li>");
        }

        sb.AppendLine("  </ul>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private static async Task WriteSimpleResponseAsync(Stream stream, string status, string message, bool head = false)
    {
        var html = $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>{HtmlEncoder.Default.Encode(status)}</title></head><body><h1>{HtmlEncoder.Default.Encode(status)}</h1><p>{HtmlEncoder.Default.Encode(message)}</p></body></html>";
        await WriteResponseAsync(stream, status, "text/html; charset=utf-8", html, head).ConfigureAwait(false);
    }

    private static Task WriteResponseAsync(Stream stream, string status, string contentType, string body, bool head)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        return WriteResponseAsync(stream, status, contentType, bodyBytes, head);
    }

    private static async Task WriteResponseAsync(Stream stream, string status, string contentType, byte[] bodyBytes, bool head)
    {
        var header = BuildHeader(status, contentType, bodyBytes.LongLength);
        await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);

        if (!head && bodyBytes.Length > 0)
        {
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
        }
    }

    private static async Task WriteFileResponseAsync(Stream stream, string filePath, bool head)
    {
        var contentType = GetContentType(Path.GetExtension(filePath));
        var fileInfo = new FileInfo(filePath);
        var header = BuildHeader("200 OK", contentType, fileInfo.Length);
        await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);

        if (!head)
        {
            await using var fileStream = File.OpenRead(filePath);
            await fileStream.CopyToAsync(stream).ConfigureAwait(false);
        }
    }

    private static async Task WriteRedirectResponseAsync(Stream stream, string location, bool head)
    {
        if (!location.StartsWith('/'))
        {
            location = "/" + location;
        }

        var body = $"<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Redirecting</title></head><body><p>Redirecting to <a href=\"{HtmlEncoder.Default.Encode(location)}\">{HtmlEncoder.Default.Encode(location)}</a></p></body></html>";
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = BuildHeader("301 Moved Permanently", "text/html; charset=utf-8", bodyBytes.LongLength, location);
        await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);

        if (!head)
        {
            await stream.WriteAsync(bodyBytes, 0, bodyBytes.Length).ConfigureAwait(false);
        }
    }

    private static byte[] BuildHeader(string status, string contentType, long contentLength, string? location = null)
    {
        var sb = new StringBuilder();
        sb.Append("HTTP/1.1 ");
        sb.AppendLine(status);
        sb.Append("Date: ");
        sb.AppendLine(DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture));
        sb.AppendLine($"Server: {ServerName}");
        sb.Append("Content-Length: ");
        sb.AppendLine(contentLength.ToString(CultureInfo.InvariantCulture));
        sb.Append("Content-Type: ");
        sb.AppendLine(contentType);
        sb.AppendLine("Connection: close");

        if (!string.IsNullOrEmpty(location))
        {
            sb.Append("Location: ");
            sb.AppendLine(location);
        }

        sb.AppendLine();
        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    private static string GetContentType(string? extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return "application/octet-stream";
        }

        return extension.ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".mjs" => "application/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".txt" => "text/plain; charset=utf-8",
            ".xml" => "application/xml",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".otf" => "font/otf",
            ".wasm" => "application/wasm",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".webmanifest" => "application/manifest+json",
            ".csv" => "text/csv; charset=utf-8",
            _ => "application/octet-stream"
        };
    }
}




