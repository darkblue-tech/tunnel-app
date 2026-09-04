using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Core.Services;

public static class SingleInstanceIpc
{
    private const string PipeName = "darkblue.tech Tunnel IPC";
    private const string MutexName = "DarkTunnelClient_SingleInstance_Mutex";

    public static bool CheckAndForwardArgs(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(500); // 500ms timeout
            
            if (args.Length > 0)
            {
                using var writer = new StreamWriter(client);
                writer.WriteLine(args[0]);
                writer.Flush();
            }
            else
            {
                // Send a wake-up message if no args are provided
                using var writer = new StreamWriter(client);
                writer.WriteLine("wakeup");
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

    public static event Action? WakeupRequested;

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

                    if (!string.IsNullOrEmpty(message))
                    {
                        if (message.StartsWith("darktunnel://auth"))
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
                        else if (message == "wakeup")
                        {
                            WakeupRequested?.Invoke();
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
