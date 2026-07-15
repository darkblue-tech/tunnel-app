using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Client.Desktop.Services;

public static class ProtocolRegistry
{
    public static void Register()
    {
        var exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrEmpty(exePath)) return;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(@"Software\Classes\darktunnel");
                key.SetValue("", "URL:DarkTunnel Protocol");
                key.SetValue("URL Protocol", "");
                using var cmdKey = key.CreateSubKey(@"shell\open\command");
                cmdKey.SetValue("", $"\"{exePath}\" \"%1\"");
            }
            catch { }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            try
            {
                var desktopFile = $@"
[Desktop Entry]
Name=DarkTunnel
Exec={exePath} %u
Type=Application
Terminal=false
MimeType=x-scheme-handler/darktunnel;
";
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "applications");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "darktunnel.desktop"), desktopFile);
                
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "xdg-mime",
                    Arguments = "default darktunnel.desktop x-scheme-handler/darktunnel",
                    UseShellExecute = true,
                    CreateNoWindow = true
                });
            }
            catch { }
        }
    }
}
