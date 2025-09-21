using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using uwebhost.Hosting;
using uwebhost.Rendering.Models;

namespace uwebhost.Rendering;

internal sealed class PageRenderer
{
    private readonly TemplateProvider _templates;

    public PageRenderer(TemplateProvider templates)
    {
        _templates = templates;
    }

    public string RenderHomePage(int port, IReadOnlyList<HostedApplication> applications)
    {
        var template = _templates.ReadTemplate("index.html");
        var listenUrl = HtmlEncoder.Default.Encode($"http://localhost:{port}/");

        template = template.Replace("{{LISTEN_URL}}", listenUrl);
        template = template.Replace("{{APP_COUNT}}", applications.Count.ToString());

        var galleryMarkup = BuildHomeGallery(applications);
        return template.Replace("{{PROJECT_SECTION}}", galleryMarkup);
    }

    public string RenderDirectoryListing(string requestPath, string? parentUrl, IReadOnlyList<DirectoryEntry> directories, IReadOnlyList<DirectoryEntry> files)
    {
        var template = _templates.ReadTemplate(Path.Combine("_templates", "directory.html"));
        template = template.Replace("{{PATH}}", HtmlEncoder.Default.Encode(requestPath));

        var builder = new StringBuilder();

        if (!string.IsNullOrEmpty(parentUrl))
        {
            var parentTemplate = _templates.ReadTemplate(Path.Combine("_templates", "partials", "directory-parent.html"));
            builder.AppendLine(parentTemplate.Replace("{{PARENT_URL}}", HtmlEncoder.Default.Encode(parentUrl)));
        }

        if (directories.Count == 0 && files.Count == 0)
        {
            var emptyTemplate = _templates.ReadTemplate(Path.Combine("_templates", "partials", "directory-empty.html"));
            builder.AppendLine(emptyTemplate);
        }
        else
        {
            var directoryTemplate = _templates.ReadTemplate(Path.Combine("_templates", "partials", "directory-directory.html"));
            foreach (var entry in directories)
            {
                builder.AppendLine(
                    directoryTemplate
                        .Replace("{{ENTRY_URL}}", HtmlEncoder.Default.Encode(entry.Url))
                        .Replace("{{ENTRY_NAME}}", HtmlEncoder.Default.Encode(entry.Name)));
            }

            var fileTemplate = _templates.ReadTemplate(Path.Combine("_templates", "partials", "directory-file.html"));
            foreach (var entry in files)
            {
                builder.AppendLine(
                    fileTemplate
                        .Replace("{{ENTRY_URL}}", HtmlEncoder.Default.Encode(entry.Url))
                        .Replace("{{ENTRY_NAME}}", HtmlEncoder.Default.Encode(entry.Name)));
            }
        }

        return template.Replace("{{DIRECTORY_ENTRIES}}", builder.ToString());
    }

    public string RenderStatusPage(string status, string message)
    {
        var template = _templates.ReadTemplate(Path.Combine("_templates", "status.html"));
        return template
            .Replace("{{STATUS}}", HtmlEncoder.Default.Encode(status))
            .Replace("{{MESSAGE}}", HtmlEncoder.Default.Encode(message));
    }

    public string RenderRedirectPage(string location)
    {
        var template = _templates.ReadTemplate(Path.Combine("_templates", "redirect.html"));
        return template.Replace("{{LOCATION}}", HtmlEncoder.Default.Encode(location));
    }

    private string BuildHomeGallery(IReadOnlyList<HostedApplication> applications)
    {
        if (applications.Count == 0)
        {
            return _templates.ReadTemplate(Path.Combine("_templates", "partials", "home-empty.html"));
        }

        var gridTemplate = _templates.ReadTemplate(Path.Combine("_templates", "partials", "gallery-grid.html"));
        var itemTemplate = _templates.ReadTemplate(Path.Combine("_templates", "partials", "gallery-item.html"));
        var tagTemplate = _templates.ReadTemplate(Path.Combine("_templates", "partials", "gallery-tag.html"));
        var builder = new StringBuilder();

        foreach (var application in applications)
        {
            var tagsMarkup = BuildTags(tagTemplate, application.Tags);
            var dataTags = string.Join(',', application.Tags.Select(tag => tag.ToLowerInvariant()));
            var dataName = application.DisplayName.ToLowerInvariant();

            var itemMarkup = itemTemplate
                .Replace("{{APP_ID}}", HtmlEncoder.Default.Encode(application.DirectoryName))
                .Replace("{{APP_URL}}", HtmlEncoder.Default.Encode(application.Url))
                .Replace("{{APP_IMAGE}}", HtmlEncoder.Default.Encode(application.ImageUrl))
                .Replace("{{APP_NAME}}", HtmlEncoder.Default.Encode(application.DisplayName))
                .Replace("{{APP_NAME_DATA}}", HtmlEncoder.Default.Encode(dataName))
                .Replace("{{APP_DESCRIPTION}}", HtmlEncoder.Default.Encode(application.Description))
                .Replace("{{APP_TAG_DATA}}", HtmlEncoder.Default.Encode(dataTags))
                .Replace("{{APP_TAG_LIST}}", tagsMarkup);

            builder.AppendLine(itemMarkup);
        }

        return gridTemplate.Replace("{{GALLERY_ITEMS}}", builder.ToString());
    }

    private static string BuildTags(string tagTemplate, IReadOnlyList<string> tags)
    {
        if (tags.Count == 0)
        {
            return "<span class=\"tag tag-empty\">No Tags</span>";
        }

        var builder = new StringBuilder();
        foreach (var tag in tags)
        {
            builder.AppendLine(tagTemplate.Replace("{{TAG_NAME}}", HtmlEncoder.Default.Encode(tag)));
        }

        return builder.ToString();
    }
}
