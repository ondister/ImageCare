
using ImageCare.Modules.Logging.Mapping;

using Prism.Ioc;
using Prism.Modularity;

using Serilog;
using Serilog.Core;
using Serilog.Exceptions.Core;
using Serilog.Exceptions;
using ImageCare.Modules.Logging.Services;
using ImageCare.Modules.Logging.Views;

namespace ImageCare.Modules.Logging
{
    public class LoggerModule:IModule
    {
        /// <inheritdoc />
        public void RegisterTypes(IContainerRegistry containerRegistry)
        {
            var logServiceSink = new LogEventService();
            var serilogLogger = CreateLogger(logServiceSink);

            containerRegistry.RegisterDialog<LogViewerView>("logViewer");

            containerRegistry.RegisterInstance<ILogEventService>(logServiceSink);
            containerRegistry.RegisterInstance<ILogNotificationService>(logServiceSink);
            containerRegistry.RegisterInstance<ILogger>(serilogLogger);
        }

        /// <inheritdoc />
        public void OnInitialized(IContainerProvider containerProvider)
        {
        }

        private static Logger CreateLogger(ILogEventSink logServicEventSink)
        {
            var options = new DestructuringOptionsBuilder()
                .WithDefaultDestructurers();

            var serilogLogger = new LoggerConfiguration()
                                .MinimumLevel.Warning()
                                .Enrich.FromLogContext()
                                .Enrich.WithExceptionDetails(options)
                                .WriteTo.File(@"Logs\Errors.log", rollingInterval: RollingInterval.Day, outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message} " + "{NewLine}{Exception}")
                                .WriteTo.Sink(logServicEventSink)
                                .CreateLogger();

            Log.Logger = serilogLogger;

            return serilogLogger;
        }
    }
}
