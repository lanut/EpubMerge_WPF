using System.Windows;

namespace EpubMerge.Gui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        ThemeManager.Initialize();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        base.OnExit(e);
    }
}
