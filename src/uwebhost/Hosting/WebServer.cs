using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using uwebhost.Rendering;
using uwebhost.Utilities;

namespace uwebhost.Hosting;

internal sealed class WebServer : IDisposable
{
    private readonly int _port;
    private readonly string _wwwRoot;
    private readonly TcpListener _listener;
    private readonly RequestRouter _router;

    public WebServer(int port, string wwwRoot)
    {
        _port = port;
        _wwwRoot = wwwRoot;
        _listener = new TcpListener(IPAddress.Loopback, port);

        UploadManager.CleanTemporaryUploads(wwwRoot);

        var templateProvider = new TemplateProvider(wwwRoot);
        var contentTypeProvider = new ContentTypeProvider();
        var pageRenderer = new PageRenderer(templateProvider);

        _router = new RequestRouter(wwwRoot, port, pageRenderer, contentTypeProvider);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _listener.Start();

        while (!cancellationToken.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            _ = Task.Run(() => _router.HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    public void Dispose()
    {
        _listener.Stop();
    }
}
