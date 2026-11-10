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
        var systemCulture = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var startupErrors = new List<Exception>();
        var languagePreference = new LanguagePreference(LanguagePreferenceStore.Read, LanguagePreferenceStore.Write,
            exception =>
            {
                if (_window is null) startupErrors.Add(exception);
                ApplicationPaths.ReportStorageError(exception);
            });
        LanguageManager.SetCulture(languagePreference.Load(systemCulture));
        _window = new MainWindow(new MainViewModel(
            new EpubMergeService(), new EpubCoverExtractor(),
            new FileMergeHistoryStore(), new FileTemporaryCoverStore()), languagePreference, startupErrors);
        _window.Activate();
    }
}
