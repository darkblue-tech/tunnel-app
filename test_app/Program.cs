using System;
using System.Diagnostics;

class Program
{
    static void Main()
    {
        var sw = Stopwatch.StartNew();
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "xdg-mime",
            Arguments = "default darktunnel.desktop x-scheme-handler/darktunnel",
            UseShellExecute = false,
            CreateNoWindow = true
        });
        sw.Stop();
        Console.WriteLine($"Process.Start took {sw.ElapsedMilliseconds} ms");
        
        sw.Restart();
        process.WaitForExit();
        sw.Stop();
        Console.WriteLine($"WaitForExit took {sw.ElapsedMilliseconds} ms");
    }
}
