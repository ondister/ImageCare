using System;
using System.Threading.Tasks;

using Avalonia;

using Serilog;

namespace ImageCare.UI.Avalonia;

internal sealed class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        try
        {
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            const string message = "Fatal Exception in the Application.\nApplication will be terminated.";
            Log.Fatal(exception, message);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    // Exposed so the Avalonia tooling and designer can discover the app configuration.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
                         .UsePlatformDetect();
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled task exception.");
    }
}
