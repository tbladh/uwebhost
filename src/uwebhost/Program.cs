using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Microsoft.Extensions.Configuration;
using uwebhost.Hosting;

namespace uwebhost;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        var configuration = BuildConfiguration(args);

        var hostingOptions = configuration
            .GetSection(HostingOptions.SectionName)
            .Get<HostingOptions>() ?? new HostingOptions();

        var port = ResolvePort(args, hostingOptions);

        var wwwRoot = Path.Combine(AppContext.BaseDirectory, "www");
        if (!Directory.Exists(wwwRoot))
        {
            Directory.CreateDirectory(wwwRoot);
            Console.WriteLine($"Created www directory at {wwwRoot}");
        }

        using var server = new WebServer(port, wwwRoot);
        Console.WriteLine($"uWebHost serving {wwwRoot}");
        Console.WriteLine($"Browse to http://localhost:{port}/");

        BrowserLauncher.TryLaunch(port);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => cts.Cancel();

        try
        {
            await server.RunAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            Console.WriteLine($"Port {port} is already in use. Specify a different port or stop the running instance.");
        }
    }

    private static IConfiguration BuildConfiguration(string[] args)
    {
        var switchMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["--port"] = $"{HostingOptions.SectionName}:Port",
            ["-p"] = $"{HostingOptions.SectionName}:Port"
        };

        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables(prefix: "UWEBHOST_")
            .AddCommandLine(args, switchMappings)
            .Build();
    }

    private static int ResolvePort(string[] args, HostingOptions hostingOptions)
    {
        if (args.Length > 0 && int.TryParse(args[0], out var requestedPort) && requestedPort is > 0 and <= 65535)
        {
            return requestedPort;
        }

        if (hostingOptions.Port is > 0 and <= 65535)
        {
            return hostingOptions.Port;
        }

        Console.WriteLine($"Configured port '{hostingOptions.Port}' is outside the valid range (1-65535). Using default {HostingOptions.DefaultPort}.");
        return HostingOptions.DefaultPort;
    }
}
