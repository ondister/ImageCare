using ImageCare.Modules.Logging.Services;
using ImageCare.Modules.Logging.Views;

using Prism.Ioc;
using Prism.Modularity;

using Serilog;
using Serilog.Core;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;

namespace ImageCare.Modules.Logging;

public class LoggerModule : IModule
{
	private Logger? _serilogLogger;

	/// <inheritdoc />
	public void RegisterTypes(IContainerRegistry containerRegistry)
	{
		var logService = new LogEventService();
		containerRegistry.RegisterInstance<ILogEventService>(logService);
		containerRegistry.RegisterInstance<ILogNotificationService>(logService);

		_serilogLogger = CreateLogger(logService);
		containerRegistry.RegisterInstance<ILogger>(_serilogLogger);

		containerRegistry.RegisterDialog<LogViewerView>("logViewer");
	}

	/// <inheritdoc />
	public void OnInitialized(IContainerProvider containerProvider)
	{
		Log.Logger = _serilogLogger;
	}

	private static Logger CreateLogger(ILogEventSink logEventSink)
	{
		var options = new DestructuringOptionsBuilder()
			.WithDefaultDestructurers();

		return new LoggerConfiguration()
		       .MinimumLevel.Warning()
		       .Enrich.FromLogContext()
		       .Enrich.WithExceptionDetails(options)
		       .WriteTo.File(
			       @"Logs\Errors.log",
			       rollingInterval: RollingInterval.Day,
			       outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message} {NewLine}{Exception}")
		       .WriteTo.Sink(logEventSink)
		       .CreateLogger();
	}
}