using FFmpegTest.Helper;
using Microsoft.UI.Xaml;

namespace FFmpegTest;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }
    
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _mWindow = new MainWindow();
        _mWindow.Activate();
        FFmpegHelper.RegisterFFmpegBinaries();
    }

    private Window _mWindow;
}
