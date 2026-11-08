using System;
using System.IO;
using System.Reflection;

namespace Client.Core.Services;

public static class AutostartHelper
{
    private const string AppName = "DarkTunnelClient";

    public static void SetAutostart(bool enable)
    {
        var execPath = Environment.ProcessPath;

        if (string.IsNullOrEmpty(execPath))
        {
            Console.WriteLine("Failed to determine process path.");
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var appImage = Environment.GetEnvironmentVariable("APPIMAGE");
            if (!string.IsNullOrEmpty(appImage) && File.Exists(appImage))
            {
                execPath = appImage;
            }
        }

        if (execPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var exe = execPath.Substring(0, execPath.Length - 4) + (OperatingSystem.IsWindows() ? ".exe" : "");
            if (File.Exists(exe))
            {
                execPath = exe;
            }
        }

        var launchCommand = $"\"{execPath}\" --minimized";

        if (OperatingSystem.IsWindows())
        {
            SetAutostartWindows(enable, launchCommand);
        }
        else if (OperatingSystem.IsLinux())
        {
            SetAutostartLinux(enable, launchCommand);
        }
        else if (OperatingSystem.IsMacOS())
        {
            SetAutostartMacOs(enable, execPath);
        }
    }

    public static bool IsAutostartEnabled()
    {
        if (OperatingSystem.IsWindows())
        {
            return IsAutostartEnabledWindows();
        }
        else if (OperatingSystem.IsLinux())
        {
            return IsAutostartEnabledLinux();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return IsAutostartEnabledMacOs();
        }
        return false;
    }

    private static void SetAutostartWindows(bool enable, string command)
    {
        try
        {
#pragma warning disable CA1416 
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true);
            if (key != null)
            {
                if (enable)
                {
                    key.SetValue(AppName, command);
                }
                else
                {
                    key.DeleteValue(AppName, false);
                }
            }
#pragma warning restore CA1416
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set Windows autostart: {ex.Message}");
        }
    }

    private static bool IsAutostartEnabledWindows()
    {
        try
        {
#pragma warning disable CA1416
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false);
            return key?.GetValue(AppName) != null;
#pragma warning restore CA1416
        }
        catch
        {
            return false;
        }
    }

    private static void SetAutostartLinux(bool enable, string command)
    {
        try
        {
            var autostartDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart");
            var desktopFile = Path.Combine(autostartDir, $"{AppName}.desktop");

            if (enable)
            {
                Directory.CreateDirectory(autostartDir);
                var content = $"""
[Desktop Entry]
Type=Application
Name=DarkTunnel
Exec={command}
Hidden=false
NoDisplay=false
X-GNOME-Autostart-enabled=true
""";
                File.WriteAllText(desktopFile, content);
            }
            else
            {
                if (File.Exists(desktopFile))
                {
                    File.Delete(desktopFile);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set Linux autostart: {ex.Message}");
        }
    }

    private static bool IsAutostartEnabledLinux()
    {
        var desktopFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "autostart", $"{AppName}.desktop");
        return File.Exists(desktopFile);
    }

    private static string GetMacOsPlistPath()
    {
        var launchAgentsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "LaunchAgents");
        return Path.Combine(launchAgentsDir, "tech.darkblue.tunnel.plist");
    }

    private static void SetAutostartMacOs(bool enable, string execPath)
    {
        try
        {
            var plistPath = GetMacOsPlistPath();
            if (enable)
            {
                var dir = Path.GetDirectoryName(plistPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var content = $"""
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>Label</key>
    <string>tech.darkblue.tunnel</string>
    <key>ProgramArguments</key>
    <array>
        <string>{execPath}</string>
        <string>--minimized</string>
    </array>
    <key>RunAtLoad</key>
    <true/>
    <key>ProcessType</key>
    <string>Interactive</string>
</dict>
</plist>
""";
                File.WriteAllText(plistPath, content);
            }
            else
            {
                if (File.Exists(plistPath))
                {
                    File.Delete(plistPath);
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to set macOS autostart: {ex.Message}");
        }
    }

    private static bool IsAutostartEnabledMacOs()
    {
        return File.Exists(GetMacOsPlistPath());
    }
}
