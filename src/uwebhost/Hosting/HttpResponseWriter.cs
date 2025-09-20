using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace uwebhost.Hosting;

internal static class HttpResponseWriter
{
    private const string ServerName = "uWebHost/0.1";

    public static Task WriteHtmlAsync(Stream stream, string status, string body, bool head)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        return WriteAsync(stream, status, "text/html; charset=utf-8", bytes, head, null);
    }

    public static Task WriteRedirectAsync(Stream stream, string status, string location, string body, bool head)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        return WriteAsync(stream, status, "text/html; charset=utf-8", bytes, head, location);
    }

    public static async Task WriteFileAsync(Stream stream, string filePath, string contentType, bool head, CancellationToken cancellationToken)
    {
        var fileInfo = new FileInfo(filePath);
        var header = BuildHeader("200 OK", contentType, fileInfo.Length, null);
        await stream.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);

        if (head)
        {
            return;
        }

        await using var fileStream = File.OpenRead(filePath);
        await fileStream.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    public static Task WriteStatusAsync(Stream stream, string status, string body, bool head)
        => WriteHtmlAsync(stream, status, body, head);

    public static Task WriteBytesAsync(Stream stream, string status, string contentType, byte[] body, bool head)
        => WriteAsync(stream, status, contentType, body, head, null);

    private static async Task WriteAsync(Stream stream, string status, string contentType, byte[] body, bool head, string? location)
    {
        var header = BuildHeader(status, contentType, body.Length, location);
        await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);

        if (!head && body.Length > 0)
        {
            await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
        }
    }

    private static byte[] BuildHeader(string status, string contentType, long contentLength, string? location)
    {
        var builder = new StringBuilder();
        builder.Append("HTTP/1.1 ");
        builder.AppendLine(status);
        builder.Append("Date: ");
        builder.AppendLine(DateTime.UtcNow.ToString("r", CultureInfo.InvariantCulture));
        builder.Append("Server: ");
        builder.AppendLine(ServerName);
        builder.Append("Content-Length: ");
        builder.AppendLine(contentLength.ToString(CultureInfo.InvariantCulture));
        builder.Append("Content-Type: ");
        builder.AppendLine(contentType);
        builder.AppendLine("Connection: close");

        if (!string.IsNullOrEmpty(location))
        {
            builder.Append("Location: ");
            builder.AppendLine(location);
        }

        builder.AppendLine();
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
