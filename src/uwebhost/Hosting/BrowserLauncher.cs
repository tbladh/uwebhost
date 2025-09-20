using System;
using System.Diagnostics;

namespace uwebhost.Hosting;

internal static class BrowserLauncher
{
    public static void TryLaunch(int port)
    {
        var url = $"http://localhost:{port}/";

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Unable to open browser automatically: {ex.Message}");
        }
    }
}
