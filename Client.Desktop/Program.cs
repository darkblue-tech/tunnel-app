using Avalonia;
using Avalonia.ReactiveUI;
using System;

namespace Client.Desktop;

internal static class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (Client.Desktop.Services.SingleInstanceIpc.CheckAndForwardArgs(args))
        {
            return;
        }

        Client.Desktop.Services.ProtocolRegistry.Register();
        Client.Desktop.Services.SingleInstanceIpc.StartServer();
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            try
            {
                var folder = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var org_dir = System.IO.Path.Join(folder, "darkblue.tech");
                var dir = System.IO.Path.Join(org_dir, "Tunnel");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Join(dir, "crash.txt"), ex.ToString());
            }
            catch { }
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
}