using System;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Core.Services;

/// <summary>
/// Provides Inter-Process Communication (IPC) to ensure only a single instance of the client runs.
/// Routes arguments (like deep link authorization URIs) from subsequent instances to the primary instance.
/// Uses dual-channel communication (Local TCP loopback + Named Pipes) to seamlessly bridge
/// different process integrity levels (e.g. Administrator and Standard user on Windows).
/// </summary>
public static class SingleInstanceIpc
{
    private const string PipeName = "darktunnel_ipc_pipe";
    private const string LegacyPipeName = "darkblue.tech Tunnel IPC";

    /// <summary>
    /// Event triggered when a subsequent instance is started or deep link is received,
    /// requesting the primary UI to restore and activate.
    /// </summary>
    public static event Action? WakeupRequested;

    private static string GetPortFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrEmpty(appData))
        {
            appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
        }
        var dir = Path.Combine(appData, "darkblue.tech", "Tunnel");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "single_instance.ipc");
    }

    private static string GetFallbackPortFilePath()
    {
        return Path.Combine(Path.GetTempPath(), "darktunnel_ipc.port");
    }

    private static (int port, string token)? ReadPortFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(fs, Encoding.UTF8);
            var content = reader.ReadToEnd().Trim();
            var parts = content.Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[0], out var port))
            {
                return (port, parts[1]);
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Checks if a primary instance is running and forwards arguments to it.
    /// </summary>
    /// <returns>True if a primary instance was found and args were forwarded, otherwise false.</returns>
    public static bool CheckAndForwardArgs(string[] args)
    {
        var payload = GetPayloadFromArgs(args);

        // 1. Try forwarding via local TCP loopback
        if (TryForwardViaTcp(payload))
        {
            return true;
        }

        // 2. Try forwarding via primary Named Pipe
        if (TryForwardViaNamedPipe(PipeName, payload))
        {
            return true;
        }

        // 3. Try forwarding via legacy Named Pipe
        if (TryForwardViaNamedPipe(LegacyPipeName, payload))
        {
            return true;
        }

        return false;
    }

    private static string GetPayloadFromArgs(string[] args)
    {
        if (args != null && args.Length > 0)
        {
            foreach (var arg in args)
            {
                var clean = arg.Trim().Trim('"', '\'');
                if (clean.StartsWith("darktunnel://", StringComparison.OrdinalIgnoreCase))
                {
                    return clean;
                }
            }

            return string.Join(" ", args);
        }

        return "wakeup";
    }

    private static bool TryForwardViaTcp(string payload)
    {
        var primaryPath = GetPortFilePath();
        var fallbackPath = GetFallbackPortFilePath();

        if (TryConnectAndSendTcp(primaryPath, payload))
        {
            return true;
        }

        if (primaryPath != fallbackPath && TryConnectAndSendTcp(fallbackPath, payload))
        {
            return true;
        }

        return false;
    }

    private static bool TryConnectAndSendTcp(string portFilePath, string payload)
    {
        try
        {
            var info = ReadPortFile(portFilePath);
            if (info == null) return false;

            var (port, token) = info.Value;

            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", port);
            if (!connectTask.Wait(1500)) return false;

            using var stream = client.GetStream();
            stream.ReadTimeout = 2000;
            stream.WriteTimeout = 2000;

            var messageBytes = Encoding.UTF8.GetBytes($"{token}|{payload}\n");
            stream.Write(messageBytes, 0, messageBytes.Length);
            stream.Flush();

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var response = reader.ReadLine();
            return response != null && response.StartsWith("OK");
        }
        catch
        {
            return false;
        }
    }

    private static bool TryForwardViaNamedPipe(string pipeName, string payload)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(1500); // 1.5s timeout

            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(payload);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Starts the IPC servers (TCP Loopback and Named Pipe) to listen for forwarded arguments.
    /// </summary>
    public static void StartServer()
    {
        StartTcpServer();
        StartNamedPipeServer(PipeName);
    }

    private static void StartTcpServer()
    {
        Task.Run(async () =>
        {
            TcpListener? listener = null;
            string? portFile = null;

            try
            {
                listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();

                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                var secretToken = Guid.NewGuid().ToString("N");

                portFile = GetPortFilePath();
                var fallbackPortFile = GetFallbackPortFilePath();
                var content = $"{port}:{secretToken}";

                await File.WriteAllTextAsync(portFile, content);
                try { await File.WriteAllTextAsync(fallbackPortFile, content); } catch { }

                if (!OperatingSystem.IsWindows())
                {
                    try
                    {
                        File.SetUnixFileMode(portFile, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    }
                    catch { }
                }

                AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                {
                    try { if (File.Exists(portFile)) File.Delete(portFile); } catch { }
                    try { if (File.Exists(fallbackPortFile)) File.Delete(fallbackPortFile); } catch { }
                    try { listener.Stop(); } catch { }
                };

                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(async () =>
                    {
                        using (client)
                        {
                            try
                            {
                                using var stream = client.GetStream();
                                using var reader = new StreamReader(stream, Encoding.UTF8);
                                using var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

                                var line = await reader.ReadLineAsync();
                                if (!string.IsNullOrEmpty(line))
                                {
                                    var split = line.Split('|', 2);
                                    if (split.Length == 2 && split[0] == secretToken)
                                    {
                                        await writer.WriteLineAsync("OK");
                                        ProcessIncomingMessage(split[1]);
                                    }
                                }
                            }
                            catch { }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TCP IPC Server error: {ex.Message}");
            }
        });
    }

    private static void StartNamedPipeServer(string pipeName)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(pipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances);
                    await server.WaitForConnectionAsync();

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var message = await reader.ReadLineAsync();

                    if (!string.IsNullOrEmpty(message))
                    {
                        ProcessIncomingMessage(message);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Named Pipe Server error: {ex.Message}");
                    await Task.Delay(500);
                }
            }
        });
    }

    /// <summary>
    /// Processes an incoming IPC message or deep link payload.
    /// </summary>
    public static void ProcessIncomingMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        var clean = message.Trim().Trim('"', '\'');
        var code = ExtractAuthCode(clean);

        if (!string.IsNullOrEmpty(code))
        {
            _ = Task.Run(async () =>
            {
                var handled = AuthService.AuthCodeCompletionSource.TrySetResult(code);
                if (!handled)
                {
                    var auth = new AuthService();
                    await auth.CompletePendingLoginAsync(code);
                }
            });
        }

        WakeupRequested?.Invoke();
    }

    /// <summary>
    /// Robustly extracts OAuth authorization code from any darktunnel:// URI format.
    /// Handles queries with or without trailing slashes, additional query parameters, and encoding.
    /// </summary>
    public static string? ExtractAuthCode(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var trimmed = input.Trim().Trim('"', '\'');
        if (!trimmed.StartsWith("darktunnel://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            // Replace custom scheme with http:// so Uri reliably parses host, path, and query
            var httpEquivalent = "http://" + trimmed.Substring("darktunnel://".Length);
            var uri = new Uri(httpEquivalent);
            var query = uri.Query.TrimStart('?');

            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kvp = part.Split('=', 2);
                if (kvp.Length == 2 && string.Equals(kvp[0], "code", StringComparison.OrdinalIgnoreCase))
                {
                    var val = kvp[1].TrimEnd('/');
                    return Uri.UnescapeDataString(val);
                }
            }
        }
        catch
        {
            // Fallback string matching if Uri parsing encounters unconventional characters
            var codeIndex = trimmed.IndexOf("code=", StringComparison.OrdinalIgnoreCase);
            if (codeIndex >= 0)
            {
                var codePart = trimmed.Substring(codeIndex + 5);
                var ampIndex = codePart.IndexOf('&');
                if (ampIndex >= 0) codePart = codePart.Substring(0, ampIndex);
                var hashIndex = codePart.IndexOf('#');
                if (hashIndex >= 0) codePart = codePart.Substring(0, hashIndex);
                var slashIndex = codePart.IndexOf('/');
                if (slashIndex >= 0) codePart = codePart.Substring(0, slashIndex);
                return Uri.UnescapeDataString(codePart.Trim('"', '\'', '/'));
            }
        }

        return null;
    }
}
