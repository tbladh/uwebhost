using System.Collections.Generic;

namespace uwebhost.Hosting;

internal sealed class WebAppManifest
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? Image { get; set; }

    public List<string>? Tags { get; set; }
}
