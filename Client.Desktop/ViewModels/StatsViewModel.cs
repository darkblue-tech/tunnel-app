using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using Client.Desktop.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace Client.Desktop.ViewModels;

public partial class StatsViewModel : ViewModelBase, IDisposable
{
    private readonly MainWindowViewModel _parent;

    [ObservableProperty]
    private string _currentRxText = "0 KB/s";

    [ObservableProperty]
    private string _currentTxText = "0 KB/s";

    [ObservableProperty]
    private TimeSpan _chartAnimationsSpeed = TimeSpan.FromMilliseconds(1000);

    public ObservableCollection<ISeries> Series { get; set; }
    public Axis[] XAxes { get; set; }
    public Axis[] YAxes { get; set; }

    private readonly ObservableCollection<double> _rxValues;
    private readonly ObservableCollection<double> _txValues;

    public StatsViewModel(MainWindowViewModel parent)
    {
        _parent = parent;

        // Populate initial values from tracker
        _rxValues = new ObservableCollection<double>(BandwidthTracker.RxHistory);
        _txValues = new ObservableCollection<double>(BandwidthTracker.TxHistory);

        var rxColor = new SKColor(76, 175, 80); // Green for Download
        var txColor = new SKColor(33, 150, 243); // Blue for Upload

        Series = new ObservableCollection<ISeries>
        {
            new LineSeries<double>
            {
                Values = _rxValues,
                Name = "Download (Rx)",
                Stroke = new SolidColorPaint(rxColor) { StrokeThickness = 3 },
                Fill = new SolidColorPaint(rxColor.WithAlpha(50)),
                GeometrySize = 0,
                LineSmoothness = 0.5
            },
            new LineSeries<double>
            {
                Values = _txValues,
                Name = "Upload (Tx)",
                Stroke = new SolidColorPaint(txColor) { StrokeThickness = 3 },
                Fill = new SolidColorPaint(txColor.WithAlpha(50)),
                GeometrySize = 0,
                LineSmoothness = 0.5
            }
        };

        XAxes = new Axis[]
        {
            new Axis
            {
                IsVisible = false, // Hide X axis for cleaner look
                MinLimit = 0,
                MaxLimit = 60
            }
        };

        YAxes = new Axis[]
        {
            new Axis
            {
                Name = "Speed (KB/s)",
                NameTextSize = 14,
                NamePaint = new SolidColorPaint(new SKColor(160, 160, 160)),
                TextSize = 12,
                LabelsPaint = new SolidColorPaint(new SKColor(160, 160, 160)),
                MinLimit = 0
            }
        };

        BandwidthTracker.OnTick += OnTick;
    }

    private void OnTick(double rx, double tx)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (ChartAnimationsSpeed.TotalMilliseconds > 0)
            {
                ChartAnimationsSpeed = TimeSpan.Zero;
            }

            CurrentRxText = $"{rx:F1} KB/s";
            CurrentTxText = $"{tx:F1} KB/s";

            _rxValues.Add(rx);
            if (_rxValues.Count > 60) _rxValues.RemoveAt(0);

            _txValues.Add(tx);
            if (_txValues.Count > 60) _txValues.RemoveAt(0);
        });
    }

    [RelayCommand]
    private void Close()
    {
        _parent.CurrentViewModel = _parent.MainVM!;
    }

    public void Dispose()
    {
        BandwidthTracker.OnTick -= OnTick;
    }
}
