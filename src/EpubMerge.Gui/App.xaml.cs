using System.Windows;
using EpubMerge.Core.Pure;

namespace EpubMerge.Gui;

/// <summary>Initializes the WPF application and selects GUI or command-line startup mode.</summary>
public partial class App : Application
{
    /// <summary>Initializes application resources and the persisted theme.</summary>
    public App()
    {
        InitializeComponent();
        ThemeManager.Initialize();
    }
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var viewModel = new MainViewModel(
            new EpubMergeService(),
            new EpubCoverExtractor(),
            new FileMergeHistoryStore(),
            new FileTemporaryCoverStore());
        var window = new MainWindow(viewModel);
        MainWindow = window;
        window.Show();
    }

    /// <summary>Releases application-wide theme event handlers before shutdown.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        if (MainWindow?.DataContext is MainViewModel viewModel) viewModel.Dispose();
        ThemeManager.Shutdown();
        base.OnExit(e);
    }
}
