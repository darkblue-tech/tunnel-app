using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Core.Services;

public static class BandwidthTracker
{
    private static long _rxBytes;
    private static long _txBytes;
    
    // Using a concurrent queue to store history points. Max 60 items for 1 minute.
    public static ConcurrentQueue<double> RxHistory { get; } = new();
    public static ConcurrentQueue<double> TxHistory { get; } = new();

    public static event Action<double, double>? OnTick; // Returns Rx KB/s, Tx KB/s

    private static readonly CancellationTokenSource _cts = new();

    static BandwidthTracker()
    {
        // Pre-fill history with 0
        for (int i = 0; i < 60; i++)
        {
            RxHistory.Enqueue(0);
            TxHistory.Enqueue(0);
        }

        _ = Task.Run(async () =>
        {
            while (!_cts.IsCancellationRequested)
            {
                await Task.Delay(1000, _cts.Token);

                var currentRx = Interlocked.Exchange(ref _rxBytes, 0);
                var currentTx = Interlocked.Exchange(ref _txBytes, 0);

                var rxKbps = currentRx / 1024.0;
                var txKbps = currentTx / 1024.0;

                RxHistory.Enqueue(rxKbps);
                if (RxHistory.Count > 60) RxHistory.TryDequeue(out _);

                TxHistory.Enqueue(txKbps);
                if (TxHistory.Count > 60) TxHistory.TryDequeue(out _);

                OnTick?.Invoke(rxKbps, txKbps);
            }
        });
    }

    public static void AddRx(long bytes)
    {
        Interlocked.Add(ref _rxBytes, bytes);
    }

    public static void AddTx(long bytes)
    {
        Interlocked.Add(ref _txBytes, bytes);
    }
}
