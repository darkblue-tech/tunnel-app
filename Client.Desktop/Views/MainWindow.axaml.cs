using Avalonia.Controls;

namespace Client.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(Avalonia.Controls.WindowClosingEventArgs e)
    {
        // Don't close the window, just hide it to the system tray
        e.Cancel = true;
        this.Hide();
        base.OnClosing(e);
    }
}
