using System;
using System.IO;
using System.IO.Pipes;
using System.Threading.Tasks;

namespace Client.Desktop.Services;

public static class SingleInstanceIpc
{
    private const string PipeName = "darkblue.tech Tunnel IPC";

    public static bool CheckAndForwardArgs(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(3000); // Increased timeout to prevent race condition on fast redirects
            
            if (args.Length > 0)
            {
                using var writer = new StreamWriter(client);
                writer.WriteLine(args[0]);
                writer.Flush();
            }
            return true;
        }
        catch (TimeoutException)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static void StartServer()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync();

                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync();

                    if (!string.IsNullOrEmpty(message) && message.StartsWith("darktunnel://auth"))
                    {
                        var uri = new Uri(message);
                        var query = uri.Query;
                        if (query.StartsWith("?code="))
                        {
                            var code = query.Substring(6);
                            // We use Task.Run to not block the IPC thread
                            _ = Task.Run(() => AuthService.AuthCodeCompletionSource.TrySetResult(code));
                        }
                    }
                }
                catch (Exception)
                {
                    // Ignore transient IPC errors
                }
            }
        });
    }
}
