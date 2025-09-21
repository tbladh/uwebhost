using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace uwebhost.Hosting;


internal static class UploadManager
{
    private const string TemporaryDirectoryName = "_uploads";
    private static readonly string[] AllowedImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg" };
    private const long MaxUploadBytes = 5 * 1024 * 1024; // 5 MB hard limit.

    public static string GetTemporaryUploadsDirectory(string wwwRoot)
        => Path.Combine(wwwRoot, TemporaryDirectoryName);

    public static void CleanTemporaryUploads(string wwwRoot)
    {
        var tempPath = GetTemporaryUploadsDirectory(wwwRoot);
        if (Directory.Exists(tempPath))
        {
            try
            {
                Directory.Delete(tempPath, recursive: true);
            }
            catch (IOException ex)
            {
                Console.WriteLine($"Unable to purge temporary uploads: {ex.Message}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Unable to purge temporary uploads: {ex.Message}");
            }
        }

        Directory.CreateDirectory(tempPath);
    }

    public static TemporaryUpload StageTemporaryUpload(string wwwRoot, string fileName, byte[] content, string? appId)
    {
        if (content.Length > MaxUploadBytes)
        {
            throw new InvalidOperationException($"Images larger than {MaxUploadBytes / (1024 * 1024)} MB are not permitted.");
        }

        var sanitizedFileName = SanitizeFileName(fileName);
        var extension = Path.GetExtension(sanitizedFileName).ToLowerInvariant();
        if (Array.IndexOf(AllowedImageExtensions, extension) < 0)
        {
            throw new InvalidOperationException($"Unsupported image type '{extension}'.");
        }

        var tempDirectory = GetTemporaryUploadsDirectory(wwwRoot);
        Directory.CreateDirectory(tempDirectory);

        var identifier = GenerateIdentifier(sanitizedFileName, appId);
        var tempFileName = identifier + extension;
        var fullPath = Path.Combine(tempDirectory, tempFileName);

        File.WriteAllBytes(fullPath, content);

        return new TemporaryUpload(identifier, tempFileName, sanitizedFileName, content.Length);
    }

    public static bool TryDeleteTemporaryUpload(string wwwRoot, string identifier)
    {
        var tempDirectory = GetTemporaryUploadsDirectory(wwwRoot);
        if (!Directory.Exists(tempDirectory))
        {
            return false;
        }
        var target = Directory
            .EnumerateFiles(tempDirectory, identifier + ".*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (target is null)
        {
            return false;
        }

        try
        {
            File.Delete(target);
            return true;
        }
        catch (IOException ex)
        {
            Console.WriteLine($"Failed to delete temp upload '{identifier}': {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Failed to delete temp upload '{identifier}': {ex.Message}");
            return false;
        }
    }

    public static string PromoteTemporaryUpload(string wwwRoot, string appDirectoryName, string identifier, string desiredFileName)
    {
        var tempDirectory = GetTemporaryUploadsDirectory(wwwRoot);
        if (!Directory.Exists(tempDirectory))
        {
            throw new FileNotFoundException("Temporary upload was not found.");
        }
        var stagedFile = Directory
            .EnumerateFiles(tempDirectory, identifier + ".*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault();

        if (stagedFile is null)
        {
            throw new FileNotFoundException("Temporary upload was not found.");
        }

        var sanitizedFileName = SanitizeFileName(desiredFileName);
        var extension = Path.GetExtension(sanitizedFileName).ToLowerInvariant();
        if (Array.IndexOf(AllowedImageExtensions, extension) < 0)
        {
            throw new InvalidOperationException($"Unsupported image type '{extension}'.");
        }

        var appDirectory = Path.Combine(wwwRoot, appDirectoryName);
        Directory.CreateDirectory(appDirectory);

        var destinationPath = Path.Combine(appDirectory, sanitizedFileName);
        File.Copy(stagedFile, destinationPath, overwrite: true);
        File.Delete(stagedFile);

        var encodedDirectory = Uri.EscapeDataString(appDirectoryName);
        var encodedFile = Uri.EscapeDataString(sanitizedFileName);
        return $"/{encodedDirectory}/{encodedFile}";
    }

    private static string SanitizeFileName(string fileName)
    {
        var candidate = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return "image";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            candidate = candidate.Replace(invalidChar, '_');
        }

        return candidate.Trim();
    }

    private static string GenerateIdentifier(string sanitizedFileName, string? appId)
    {
        using var sha256 = SHA256.Create();
        var stamp = DateTime.UtcNow.ToString("O");
        var input = string.Concat(appId ?? string.Empty, "|", sanitizedFileName, "|", stamp, "|", Guid.NewGuid());
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}

internal sealed record TemporaryUpload(string Identifier, string TempFileName, string OriginalFileName, long SizeBytes);
