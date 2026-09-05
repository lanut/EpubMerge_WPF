using System.Windows;
using System.Runtime.InteropServices;

namespace EpubMerge.Gui;

public partial class App : Application
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();
    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();

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

    protected override void OnExit(ExitEventArgs e)
    {
        ThemeManager.Shutdown();
        base.OnExit(e);
    }
}
