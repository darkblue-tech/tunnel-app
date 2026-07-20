using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

namespace Client.Desktop.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;

    public LoginViewModel(MainWindowViewModel parent)
    {
        _parent = parent;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        await _parent.LoginAndLoadTunnelsAsync();
    }
}
