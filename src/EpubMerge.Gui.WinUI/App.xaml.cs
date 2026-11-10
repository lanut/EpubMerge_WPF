using EpubMerge.Core.Pure;
using Microsoft.UI.Xaml;

namespace EpubMerge.Gui.WinUI;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        UnhandledException += (_, args) => ApplicationPaths.ReportStorageError(args.Exception);
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow(new MainViewModel(
            new EpubMergeService(), new EpubCoverExtractor(),
            new FileMergeHistoryStore(), new FileTemporaryCoverStore()));
        _window.Activate();
    }
}
