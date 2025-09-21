using System.Collections.Generic;

namespace uwebhost.Hosting;

internal sealed record HostedApplication(
    string DirectoryName,
    string DisplayName,
    string Description,
    string ImageUrl,
    IReadOnlyList<string> Tags,
    string Url);
