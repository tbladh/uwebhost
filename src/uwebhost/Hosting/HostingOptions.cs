namespace uwebhost.Hosting;

internal sealed class HostingOptions
{
    public const string SectionName = "Hosting";
    public const int DefaultPort = 5000;

    public int Port { get; set; } = DefaultPort;
}
