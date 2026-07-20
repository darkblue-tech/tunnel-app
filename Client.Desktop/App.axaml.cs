using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Client.Desktop.ViewModels;
using Client.Desktop.Views;
using System;

namespace Client.Desktop;

public partial class App : Application
{
    private MainWindowViewModel? _mainViewModel;
    private MainWindow? _mainWindow;
    private TrayIcon? _trayIcon;

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainViewModel = new MainWindowViewModel();
            _mainWindow = new MainWindow
            {
                DataContext = _mainViewModel
            };

            SetupTrayIcon();

            _mainWindow.Closing += (s, e) =>
            {
                if (_mainViewModel.CloseToTray)
                {
                    e.Cancel = true;
                    _mainWindow.Hide();
                }
            };

            desktop.MainWindow = _mainWindow;

            var args = Environment.GetCommandLineArgs();
            bool startMinimized = false;
            foreach (var arg in args)
            {
                if (arg == "--minimized") startMinimized = true;
            }
            if (_mainViewModel.StartMinimized) startMinimized = true;

            if (!startMinimized)
            {
                _mainWindow.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        _trayIcon = new TrayIcon
        {
            ToolTipText = "DarkTunnel Client"
        };
        _trayIcon.Clicked += Show_Clicked;

        UpdateTrayIcon();
        RebuildTrayMenu();

        if (_mainViewModel != null)
        {
            _mainViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainWindowViewModel.Status) ||
                    e.PropertyName == nameof(MainWindowViewModel.IsAuthenticated))
                {
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        UpdateTrayIcon();
                        RebuildTrayMenu();
                    });
                }
                else if (e.PropertyName == nameof(MainWindowViewModel.MainVM) && _mainViewModel.MainVM != null)
                {
                    _mainViewModel.MainVM.PropertyChanged += (ss, ee) =>
                    {
                        if (ee.PropertyName == nameof(MainViewModel.IsConnected) || 
                            ee.PropertyName == nameof(MainViewModel.StatusKey))
                        {
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                UpdateTrayIcon();
                                RebuildTrayMenu();
                            });
                        }
                    };
                    
                    // Trigger immediate update
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        UpdateTrayIcon();
                        RebuildTrayMenu();
                    });
                }
            };

            _mainViewModel.Tunnels.CollectionChanged += (s, e) =>
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(RebuildTrayMenu);
            };
        }

        var trayIcons = new TrayIcons { _trayIcon };
        this.SetValue(TrayIcon.IconsProperty, trayIcons);
    }

    private void UpdateTrayIcon()
    {
        if (_trayIcon == null) return;

        string iconName = "tunnel_waiting_icon_x128.ico";

        if (_mainViewModel != null && _mainViewModel.IsAuthenticated)
        {
            // If connected
            if (_mainViewModel.MainVM != null && _mainViewModel.MainVM.IsConnected)
            {
                iconName = "tunnel_running_icon_x128.ico";
            }
            // Check if any error state exists (for future expansion)
            else if (_mainViewModel.StatusKey == "Str_Status_AuthFailed")
            {
                iconName = "tunnel_error_icon_x128.ico";
            }
        }

        try
        {
            var uri = new Uri($"avares://DarkTunnel Client/Assets/TrayIcons/{iconName}");
            using var stream = Avalonia.Platform.AssetLoader.Open(uri);
            _trayIcon.Icon = new Avalonia.Controls.WindowIcon(stream);
        }
        catch { }
    }

    private string GetString(string key)
    {
        if (Avalonia.Application.Current != null &&
            Avalonia.Application.Current.TryGetResource(key, Avalonia.Styling.ThemeVariant.Default, out var res) &&
            res is string str)
        {
            return str;
        }
        return key;
    }

    private async void RebuildTrayMenu()
    {
        if (_trayIcon == null) return;

        var menu = new NativeMenu();

        // Account section
        string accountText = GetString("Str_Tray_NotLoggedIn");
        if (_mainViewModel != null && _mainViewModel.IsAuthenticated)
        {
            try
            {
                var storage = new Client.Desktop.Services.SecretStorage();
                var result = await storage.GetSecretAsync("profile_name");
                if (!string.IsNullOrEmpty(result))
                {
                    accountText = result;
                }
            }
            catch { }
        }
        menu.Items.Add(new NativeMenuItem { Header = accountText, IsEnabled = false });

        menu.Items.Add(new NativeMenuItemSeparator());

        // Status section
        if (_mainViewModel != null)
        {
            menu.Items.Add(new NativeMenuItem { Header = GetString(_mainViewModel.StatusKey), IsEnabled = false });
            menu.Items.Add(new NativeMenuItemSeparator());

            // Tunnels
            foreach (var t in _mainViewModel.Tunnels)
            {
                var tItem = new NativeMenuItem
                {
                    Header = $"{t.Data.Name} ({(t.IsConnected ? GetString("Str_Status_Connected") : GetString("Str_Status_Disconnected"))})",
                    ToggleType = NativeMenuItemToggleType.CheckBox,
                    IsChecked = t.IsSelected
                };
                tItem.Click += (s, e) =>
                {
                    t.IsSelected = !t.IsSelected;
                    if (t.IsSelected && !t.IsConnected) t.Start();
                    else if (!t.IsSelected && t.IsConnected) t.Stop();
                };
                menu.Items.Add(tItem);
            }

            if (_mainViewModel.Tunnels.Count > 0)
            {
                menu.Items.Add(new NativeMenuItemSeparator());
            }

            // Global connect / disconnect
            var connectAll = new NativeMenuItem { Header = GetString("Str_Tray_ConnectAll") };
            connectAll.Click += (s, e) =>
            {
                if (_mainViewModel.CurrentViewModel is MainViewModel mvm)
                {
                    if (!mvm.IsConnected) mvm.ToggleGlobalConnectionCommand.Execute(null);
                }
            };
            var disconnectAll = new NativeMenuItem { Header = GetString("Str_Tray_DisconnectAll") };
            disconnectAll.Click += (s, e) =>
            {
                if (_mainViewModel.CurrentViewModel is MainViewModel mvm)
                {
                    if (mvm.IsConnected) mvm.ToggleGlobalConnectionCommand.Execute(null);
                }
            };

            menu.Items.Add(connectAll);
            menu.Items.Add(disconnectAll);
            menu.Items.Add(new NativeMenuItemSeparator());
        }

        var showItem = new NativeMenuItem { Header = GetString("Str_Tray_Show") };
        showItem.Click += Show_Clicked;
        menu.Items.Add(showItem);

        var exitItem = new NativeMenuItem { Header = GetString("Str_Tray_Exit") };
        exitItem.Click += Exit_Clicked;
        menu.Items.Add(exitItem);

        _trayIcon.Menu = menu;
    }

    private void Show_Clicked(object? sender, EventArgs e)
    {
        if (_mainWindow != null)
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }
    }

    private void Exit_Clicked(object? sender, EventArgs e)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
