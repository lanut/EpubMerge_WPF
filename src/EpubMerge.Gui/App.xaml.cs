using System.Windows;
using System.Runtime.InteropServices;

namespace EpubMerge.Gui;

/// <summary>Initializes the WPF application and selects GUI or command-line startup mode.</summary>
public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

    /// <summary>Initializes application resources and the persisted theme.</summary>
    public App()
    {
        InitializeComponent();
        ThemeManager.Initialize();
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        if (!e.Args.Any(arg => string.Equals(arg, "--cli", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "-c", StringComparison.OrdinalIgnoreCase)))
        {
            var window = new MainWindow();
            MainWindow = window;
            window.Show();
            return;
        }

        // A WinExe has no console by default; create one only for the explicit CLI entry point.
        AllocConsole();
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        try
        {
            var cliArgs = e.Args.Where(arg => !string.Equals(arg, "--cli", StringComparison.OrdinalIgnoreCase) && !string.Equals(arg, "-c", StringComparison.OrdinalIgnoreCase)).ToArray();
            Shutdown(await CliRunner.RunAsync(cliArgs));
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"未处理错误：{exception.Message}");
            Shutdown(2);
        }
        finally { FreeConsole(); }
    }

    /// <summary>Releases application-wide theme event handlers before shutdown.</summary>
    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        base.OnExit(e);
    }
}
