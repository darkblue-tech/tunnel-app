using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Threading.Tasks;

using Client.Core.Services;
using Client.Core.Models;
namespace Client.Desktop.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _parent;

    public LoginViewModel(MainWindowViewModel parent)
    {
        _parent = parent;
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoginAsync()
    {
        await _parent.LoginAndLoadTunnelsAsync();
    }
}
