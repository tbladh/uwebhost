using System.IO;

namespace uwebhost.Rendering;

internal sealed class TemplateProvider
{
    private readonly string _wwwRoot;

    public TemplateProvider(string wwwRoot)
    {
        _wwwRoot = wwwRoot;
    }

    public string ReadTemplate(string relativePath)
    {
        var fullPath = Path.Combine(_wwwRoot, relativePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Template not found: {relativePath}", fullPath);
        }

        return File.ReadAllText(fullPath);
    }
}
