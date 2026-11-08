using Avalonia.Controls;
using Client.Desktop.ViewModels;

namespace Client.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(Avalonia.Controls.WindowClosingEventArgs e)
    {
        if (!App.IsExiting && DataContext is MainWindowViewModel vm && vm.CloseToTray)
        {
            e.Cancel = true;
            this.Hide();
            return;
        }

        base.OnClosing(e);
    }
}
