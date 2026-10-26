using ImageCare.Core.Domain.Configuration;

namespace ImageCare.Core.Services.ConfigurationService;

public interface IConfigurationService
{
	IObservable<Configuration> ConfigurationSaved { get; }

	Lazy<Configuration> Configuration { get; }

    /// <summary>
    /// Gets whether the configuration file was absent when the service started.
    /// </summary>
    bool IsFirstRun => false;

	void SaveConfiguration();
}