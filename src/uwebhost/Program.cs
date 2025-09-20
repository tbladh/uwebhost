using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using uwebhost.Hosting;

namespace uwebhost;

internal static class Program
{
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
}
