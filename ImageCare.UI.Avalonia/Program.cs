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
            AppBuilder.Configure<App>()
                      .UsePlatformDetect()
                      .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception exception)
        {
            var message = "Fatal Exception in the Application.\nApplication will be terminated.";
            Log.Fatal(exception, message);
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled task exception.");
    }
}