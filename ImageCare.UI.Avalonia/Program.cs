using System;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Win32;

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
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

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
        var builder = AppBuilder.Configure<App>()
                                .UsePlatformDetect();

        if (OperatingSystem.IsWindows())
        {
            builder.With(new Win32PlatformOptions
            {
                RenderingMode = [Win32RenderingMode.AngleEgl, Win32RenderingMode.Software]
            });
        }

        return builder;
    }

    private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled task exception.");
        e.SetObserved();
    }

    private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.Fatal(exception, "Unhandled application exception. Terminating: {IsTerminating}", e.IsTerminating);
        }
    }
}
